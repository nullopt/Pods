using CommunityToolkit.Mvvm.ComponentModel;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>What sits before a ring's percentage: nothing, a circled L or R, or a case glyph.</summary>
internal enum BatteryBadge { None, Left, Right, Case }

/// <summary>One battery ring on a device tile.</summary>
internal sealed partial class BatteryViewModel(BatteryBadge badge) : ObservableObject
{
    public bool HasLetter { get; } = badge is BatteryBadge.Left or BatteryBadge.Right;
    public string Letter { get; } = badge switch { BatteryBadge.Left => "L", BatteryBadge.Right => "R", _ => "" };
    public bool HasCaseBadge { get; } = badge == BatteryBadge.Case;

    [ObservableProperty] public partial string Text { get; private set; } = BatteryText.Format(null);
    [ObservableProperty] public partial double Value { get; private set; }
    [ObservableProperty] public partial bool IsCharging { get; private set; }
    /// <summary>Red ring (as on iPhone); otherwise <see cref="IsNormal"/> shows the green ring.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNormal))]
    public partial bool IsLow { get; private set; }

    public bool IsNormal => !IsLow;

    /// <param name="threshold">The device's low-battery percentage: at or below it, not charging, the ring is red.</param>
    public void Update(int? percent, bool charging, int threshold)
    {
        Text = BatteryText.Format(percent);
        Value = percent ?? 0;
        IsCharging = charging;
        IsLow = BatteryRings.IsLow(percent, charging, threshold);
    }
}
