using CommunityToolkit.Mvvm.ComponentModel;
using Pods.App.Views;

namespace Pods.App.Devices;

/// <summary>One row of a collapsed card (earbuds, case, or the whole headphones), as in the macOS Bluetooth menu.</summary>
internal sealed partial class PartRowViewModel(TurntablePart part) : ObservableObject
{
    /// <summary>Inner width of the battery bar, in DIPs.</summary>
    public const double BarInnerWidth = 24;

    public TurntablePart Part { get; } = part;

    [ObservableProperty] public partial string? Sheet { get; private set; }
    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string Text { get; private set; } = "";
    [ObservableProperty] public partial double FillWidth { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNormal))]
    public partial bool IsLow { get; private set; }

    public bool IsNormal => !IsLow;

    /// <param name="level">Percentage the bar shows (the lower earbud when they differ); null = unknown.</param>
    public void Update(string? sheet, string title, string text, int? level, bool charging)
    {
        Sheet = sheet;
        Title = title;
        Text = text;
        FillWidth = (level ?? 0) / 100.0 * BarInnerWidth;
        IsLow = level is <= BatteryViewModel.LowPercent && !charging;
    }
}
