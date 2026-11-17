using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pods.App.Views;
using Pods.Core.Apple;
using Pods.Core.Devices;

namespace Pods.App.Tray;

/// <summary>The tray hover card: one ring per battery of each connected headset, as in the macOS Batteries widget.</summary>
internal sealed partial class TrayCardViewModel : ObservableObject
{
    public ObservableCollection<TrayRingViewModel> Rings { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; } = true;

    /// <param name="connected">Connected headsets, in card order.</param>
    public void Update(IReadOnlyList<HeadphoneSnapshot> connected, Func<string, int> thresholdFor)
    {
        List<TrayRingViewModel> rings = [];
        foreach (var snapshot in connected)
        {
            var sheet = Turntable.Resolve(snapshot.ModelId, snapshot.Colour, TurntableView.Available);
            var split = sheet is not null && Turntable.Parts(sheet) is not null;
            var threshold = thresholdFor(snapshot.Key);
            foreach (var ring in BatteryRings.Of(snapshot))
            {
                var part = ring.Kind switch
                {
                    BatteryPartKind.Single => TurntablePart.Whole,
                    BatteryPartKind.Case => TurntablePart.Right,
                    _ => TurntablePart.Left,
                };
                // A sheet without a cut cannot show one part, so such rings go without artwork.
                var ringSheet = part == TurntablePart.Whole || split ? sheet : null;
                rings.Add(new(ringSheet, part, Label(ring.Kind, snapshot.Name), BatteryText.Format(ring.Percent),
                    ring.Percent ?? 0, ring.Charging, BatteryRings.IsLow(ring.Percent, ring.Charging, threshold)));
            }
        }
        if (rings.SequenceEqual(Rings)) return;
        Rings.Clear();
        foreach (var ring in rings) Rings.Add(ring);
        IsEmpty = Rings.Count == 0;
    }

    static string Label(BatteryPartKind kind, string name) => kind switch
    {
        BatteryPartKind.Single => name,
        _ => string.Format(Strings.Get("TrayPart" + kind), name),
    };
}
