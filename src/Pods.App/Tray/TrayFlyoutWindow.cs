using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace Pods.App.Tray;

/// <summary>
/// The click flyout: takes focus and closes itself when focus moves elsewhere or on Esc, like the
/// system's tray flyouts. Taller content than <see cref="MaxHeightFraction"/> of the work area scrolls.
/// </summary>
internal sealed partial class TrayFlyoutWindow : TrayAnchoredWindow
{
    const double MaxHeightFraction = 0.9;
    readonly ScrollViewer scroller;
    bool closed;

    public TrayFlyoutWindow(UIElement content) : this(new ScrollViewer { Content = content }) { }

    TrayFlyoutWindow(ScrollViewer scroller) : base(scroller)
    {
        this.scroller = scroller;
        // Keys reach the content only once something in it has focus: start on the scroller (arrows scroll it).
        // Activation comes before the content loads, so focus is set on load.
        scroller.IsTabStop = true;
        scroller.UseSystemFocusVisuals = false;
        scroller.Loaded += (_, _) => scroller.Focus(FocusState.Programmatic);
        scroller.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            CloseOnce();
        };
        // Closing inside the activation callback is re-entrant; let it finish first.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) DispatcherQueue.TryEnqueue(CloseOnce);
        };
        Closed += (_, _) =>
        {
            closed = true;
            Dismissed?.Invoke();
        };
    }

    /// <summary>Raised once when the flyout closes, for whatever reason.</summary>
    public event Action? Dismissed;

    public void ShowAbove(int left, int top, int right, int bottom) => ShowAt(left, top, right, bottom, activate: true);

    void CloseOnce()
    {
        if (!closed) Close();
    }

    protected override void OnWorkArea(RectInt32 work, double scale) =>
        scroller.MaxHeight = work.Height * MaxHeightFraction / scale;
}
