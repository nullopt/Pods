namespace Pods.Core.Devices;

/// <summary>A part that just ran low: notify once.</summary>
public sealed record LowBatteryAlert(string Key, string Name, BatteryPartKind Part, int Percent);

/// <summary>
/// Decides when to warn about low batteries. Each physical part (left, right, case, or a headset's
/// only battery) warns once when it is low, then stays quiet until it has been seen charging, so a
/// level wobbling around the threshold never repeats. Being low on first sight counts. Only connected
/// headsets are checked; a disconnected one keeps its state, so reconnecting does not warn again.
/// Not thread-safe: call from one thread.
/// </summary>
public sealed class LowBatteryMonitor
{
    readonly HashSet<(string Key, BatteryPartKind Part)> warned = [];

    /// <summary>Whether any part of a connected headset was low at the last <see cref="Update"/>.</summary>
    public bool AnyLow { get; private set; }

    public IReadOnlyList<LowBatteryAlert> Update(IReadOnlyList<HeadphoneSnapshot> snapshots, Func<string, int> thresholdFor)
    {
        List<LowBatteryAlert> alerts = [];
        var anyLow = false;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.State != DeviceState.Connected) continue;
            var threshold = thresholdFor(snapshot.Key);
            foreach (var (part, percent, charging) in Parts(snapshot))
            {
                var id = (snapshot.Key, part);
                if (charging) warned.Remove(id);
                if (!BatteryRings.IsLow(percent, charging, threshold)) continue;
                anyLow = true;
                if (warned.Add(id)) alerts.Add(new(snapshot.Key, snapshot.Name, part, percent!.Value));
            }
        }
        AnyLow = anyLow;
        return alerts;
    }

    static IEnumerable<BatteryRing> Parts(HeadphoneSnapshot snapshot)
    {
        if (snapshot.IsSingleBattery) return BatteryRings.Of(snapshot);
        var b = snapshot.Battery;
        return
        [
            new(BatteryPartKind.Left, b?.Left, b?.LeftCharging ?? false),
            new(BatteryPartKind.Right, b?.Right, b?.RightCharging ?? false),
            new(BatteryPartKind.Case, b?.Case, b?.CaseCharging ?? false),
        ];
    }
}
