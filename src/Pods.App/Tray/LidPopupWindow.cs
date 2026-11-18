using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pods.App.Devices;
using Pods.App.Views;
using Windows.Graphics;

namespace Pods.App.Tray;

/// <summary>
/// Shown when a headset's case opens: its flyout tile at the bottom centre of the screen. It slides up
/// from below the screen like the iPhone's card, without taking focus, and slides away after
/// <see cref="ShowFor"/> (held while the pointer is over it), on its close button, or when the lid shuts.
/// </summary>
internal sealed partial class LidPopupWindow : PopupWindow
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(8);
    static readonly TimeSpan SlideUp = TimeSpan.FromMilliseconds(350);
    static readonly TimeSpan SlideDown = TimeSpan.FromMilliseconds(250);
    const int Gap = 24;   // DIPs between the popup and the bottom of the work area

    readonly DispatcherQueueTimer timer;
    readonly TrayHover.Rect? iconRect;
    readonly Stopwatch slideClock = new();
    int restY, offY, x, currentY, slideFrom;
    bool leaving, closed;

    /// <param name="iconRect">The tray icon, to pick its screen; null uses the main screen.</param>
    public LidPopupWindow(DeviceCardViewModel card, TrayHover.Rect? iconRect) : base(CreateView(card, out var close))
    {
        Card = card;
        this.iconRect = iconRect;
        close.Click += (_, _) => Dismiss();
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = ShowFor;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Dismiss();
        View.PointerEntered += (_, _) => timer.Stop();
        View.PointerExited += (_, e) =>
        {
            // Exits bubble up from the tile's parts too; only the popup's own exit means the pointer left.
            if (!leaving && ReferenceEquals(e.OriginalSource, View)) timer.Start();
        };
        Closed += (_, _) =>
        {
            closed = true;
            timer.Stop();
            CompositionTarget.Rendering -= OnSlideFrame;
            Dismissed?.Invoke();
        };
    }

    public DeviceCardViewModel Card { get; }

    public string Key => Card.Key;

    /// <summary>Raised once the popup has gone.</summary>
    public event Action? Dismissed;

    public void Show() => ShowWhenLoaded(activate: false);

    /// <summary>The same case opened again: stay for another full period.</summary>
    public void RestartTimer()
    {
        if (leaving) return;
        timer.Stop();
        timer.Start();
    }

    /// <summary>Slides down out of view, then closes.</summary>
    public void Dismiss()
    {
        if (leaving || closed) return;
        leaving = true;
        timer.Stop();
        if (IsWaitingForLoad) Close();   // never shown
        else StartSlide();
    }

    static FrameworkElement CreateView(DeviceCardViewModel card, out Button close)
    {
        close = new Button
        {
            Style = (Style)Application.Current.Resources["PodsIconButton"],
            Content = new FontIcon { Glyph = "", FontSize = 14 },
        };
        var name = Strings.Get("PopupClose");
        ToolTipService.SetToolTip(close, name);
        AutomationProperties.SetName(close, name);
        // Framed like the flyout: the tile on the flyout's page background.
        return new Border
        {
            Padding = new Thickness(12),
            Background = (Brush)Application.Current.Resources["PodsPageBackground"],
            Child = new DeviceTile { Card = card, Corner = close },
        };
    }

    protected override void Place()
    {
        var display = iconRect is { } r
            ? DisplayArea.GetFromPoint(new PointInt32(r.Left, r.Top), DisplayAreaFallback.Primary)
            : DisplayArea.Primary;
        var work = display.WorkArea;
        var size = MeasureContent();
        x = work.X + (work.Width - size.Width) / 2;
        restY = work.Y + work.Height - size.Height - (int)(Gap * Scale);
        offY = display.OuterBounds.Y + display.OuterBounds.Height;   // just below the screen
        AppWindow.ResizeClient(size);
        currentY = offY;
        AppWindow.Move(new PointInt32(x, offY));
    }

    protected override void OnShown()
    {
        StartSlide();
        timer.Start();
    }

    void StartSlide()
    {
        slideFrom = currentY;   // a dismissal mid-entrance turns back from where it is
        slideClock.Restart();
        CompositionTarget.Rendering -= OnSlideFrame;
        CompositionTarget.Rendering += OnSlideFrame;
    }

    /// <summary>Moves the window each frame: up with a strong ease-out, down with an ease-in.</summary>
    void OnSlideFrame(object? sender, object e)
    {
        var duration = leaving ? SlideDown : SlideUp;
        var t = Math.Min(1, slideClock.Elapsed / duration);
        var progress = leaving ? t * t * t : 1 - Math.Pow(1 - t, 4);
        var to = leaving ? offY : restY;
        currentY = (int)Math.Round(slideFrom + (to - slideFrom) * progress);
        AppWindow.Move(new PointInt32(x, currentY));
        if (t < 1) return;
        CompositionTarget.Rendering -= OnSlideFrame;
        if (leaving) Close();
    }
}
