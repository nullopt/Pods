using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
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
    static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(160);
    const float EnterRise = 12;   // DIPs the content rises while fading in
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
        scroller.Loaded += (_, _) =>
        {
            scroller.Focus(FocusState.Programmatic);
        };
        // Until the content has drawn, the window shows the flyout's own background, not a default one,
        // and the content starts invisible so the entrance hides that first empty frame.
        if (Content is Panel root && Application.Current.Resources.TryGetValue("PodsPageBackground", out var background))
            root.Background = background as Brush;
        ElementCompositionPreview.SetIsTranslationEnabled(scroller, true);
        ElementCompositionPreview.GetElementVisual(scroller).Opacity = 0;
        scroller.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            CloseOnce();
        };
        Activated += OnActivated;
    }

    // Closing inside the activation callback is re-entrant; let it finish first.
    void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated) DispatcherQueue.TryEnqueue(CloseOnce);
    }

    protected override void OnClosed()
    {
        closed = true;
        Activated -= OnActivated;
        Dismissed?.Invoke();
    }

    /// <summary>Raised once when the flyout closes, for whatever reason.</summary>
    public event Action? Dismissed;

    public void ShowAbove(int left, int top, int right, int bottom) => ShowAt(left, top, right, bottom, activate: true);

    /// <summary>The content rises a little and fades in, like the system's tray flyouts (above a top taskbar it drops).</summary>
    protected override void OnShown()
    {
        var visual = ElementCompositionPreview.GetElementVisual(scroller);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = EnterDuration;
        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, OpensUpward ? EnterRise : -EnterRise, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = EnterDuration;
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }

    void CloseOnce()
    {
        if (!closed) Close();
    }

    protected override void OnWorkArea(RectInt32 work, double scale) =>
        scroller.MaxHeight = work.Height * MaxHeightFraction / scale;
}
