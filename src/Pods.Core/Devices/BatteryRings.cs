namespace Pods.Core.Devices;

/// <summary>A battery a headset shows: combined earbuds, one earbud, the case, or a headset's only battery.</summary>
public enum BatteryPartKind { Buds, Left, Right, Case, Single }

/// <summary>One battery ring; <see cref="Percent"/> is null while unknown.</summary>
public readonly record struct BatteryRing(BatteryPartKind Kind, int? Percent, bool Charging);

/// <summary>Which rings a headset shows, and when a level counts as low. Shared by the cards and the tray.</summary>
public static class BatteryRings
{
    /// <summary>
    /// A single-battery headset shows one ring (its level may arrive in either pod slot). Earbuds show
    /// one combined ring while both report the same level and charging state, else separate L and R
    /// rings; plus the case.
    /// </summary>
    public static IReadOnlyList<BatteryRing> Of(HeadphoneSnapshot snapshot)
    {
        var b = snapshot.Battery;
        if (snapshot.IsSingleBattery)
        {
            var single = b?.Left ?? b?.Right;
            var charging = b?.Left is null ? b?.RightCharging ?? false : b.Value.LeftCharging;
            return [new(BatteryPartKind.Single, single, charging)];
        }
        var @case = new BatteryRing(BatteryPartKind.Case, b?.Case, b?.CaseCharging ?? false);
        if (b is { Left: not null } p && p.Left == p.Right && p.LeftCharging == p.RightCharging)
            return [new(BatteryPartKind.Buds, p.Left, p.LeftCharging), @case];
        return
        [
            new(BatteryPartKind.Left, b?.Left, b?.LeftCharging ?? false),
            new(BatteryPartKind.Right, b?.Right, b?.RightCharging ?? false),
            @case,
        ];
    }

    /// <summary>Known, at or below the threshold, and not charging.</summary>
    public static bool IsLow(int? percent, bool charging, int threshold) => percent <= threshold && !charging;
}
