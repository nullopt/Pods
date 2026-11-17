using Pods.App.Views;

namespace Pods.App.Tray;

/// <summary>One ring on the tray hover card: still artwork inside, percentage and label below.</summary>
internal sealed record TrayRingViewModel(
    string? Sheet, TurntablePart Part, string Label, string Text, double Value, bool IsCharging, bool IsLow)
{
    public bool IsNormal => !IsLow;
}
