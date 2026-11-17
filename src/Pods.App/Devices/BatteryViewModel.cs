using CommunityToolkit.Mvvm.ComponentModel;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>What sits before a ring's percentage: nothing, a circled L or R, or a case glyph.</summary>
internal enum BatteryBadge { None, Left, Right, Case }

/// <summary>One battery ring on an expanded card.</summary>
internal sealed partial class BatteryViewModel(BatteryBadge badge) : ObservableObject
{
    /// <summary>At or below this level the ring turns red, as on iPhone.</summary>
    public const int LowPercent = 20;

    public bool HasLetter { get; } = badge is BatteryBadge.Left or BatteryBadge.Right;
    public string Letter { get; } = badge switch { BatteryBadge.Left => "L", BatteryBadge.Right => "R", _ => "" };
    public bool HasCaseBadge { get; } = badge == BatteryBadge.Case;

    /// <summary>The R's ink sits right of its box centre; this pulls it back by half a pixel.</summary>
    public Microsoft.UI.Xaml.Thickness LetterMargin { get; } = badge == BatteryBadge.Right ? new(0, 0, 1, 0) : default;

    [ObservableProperty] public partial string Text { get; private set; } = BatteryText.Format(null);
    [ObservableProperty] public partial double Value { get; private set; }
    [ObservableProperty] public partial bool IsCharging { get; private set; }
    /// <summary>Red ring (as on iPhone); otherwise <see cref="IsNormal"/> shows the green ring.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNormal))]
    public partial bool IsLow { get; private set; }

    public bool IsNormal => !IsLow;

    public void Update(int? percent, bool charging)
    {
        Text = BatteryText.Format(percent);
        Value = percent ?? 0;
        IsCharging = charging;
        IsLow = percent is <= LowPercent && !charging;
    }
}
