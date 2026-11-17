using CommunityToolkit.Mvvm.ComponentModel;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>One battery ring on a device card (left, right, case, or the single Max battery).</summary>
internal sealed partial class BatteryViewModel(string label) : ObservableObject
{
    /// <summary>At or below this level the ring turns red, as on iPhone.</summary>
    const int LowPercent = 20;

    public string Label { get; } = label;

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
