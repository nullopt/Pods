using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Pods.App.Tray;

/// <summary>A popup anchored to the tray icon: above the icon, or below a top taskbar.</summary>
internal abstract partial class TrayAnchoredWindow : PopupWindow
{
    const int Gap = 8;   // DIPs between the window and the taskbar
    (int Left, int Top, int Right, int Bottom) anchor;

    protected TrayAnchoredWindow(FrameworkElement view) : base(view)
    {
        // The content can grow (a headset connects); the window follows both ways.
        view.SizeChanged += (_, _) =>
        {
            if (!offScreen && !IsWaitingForLoad) Place();
        };
    }

    /// <summary>Shows the window centred on the icon (screen pixels), optionally taking focus.</summary>
    public void ShowAt(int left, int top, int right, int bottom, bool activate)
    {
        anchor = (left, top, right, bottom);
        ShowWhenLoaded(activate);
    }

    /// <summary>True when the window sits above its icon (a bottom taskbar), so it opens upward.</summary>
    protected bool OpensUpward { get; private set; } = true;

    bool offScreen;   // a warm-up window stays off screen even when its content resizes

    /// <summary>
    /// Shows the window far off screen without focus, so its content loads, lays out and draws once.
    /// Used to warm up code and layout at startup; the caller closes it.
    /// </summary>
    public void ShowOffScreen()
    {
        offScreen = true;
        AppWindow.Move(new PointInt32(-32000, -32000));
        AppWindow.Show(activateWindow: false);
    }

    /// <summary>Called before each placement with the work area (pixels) and scale, to cap the content.</summary>
    protected virtual void OnWorkArea(RectInt32 work, double scale) { }

    protected override void Place()
    {
        var work = DisplayArea.GetFromPoint(new PointInt32(anchor.Left, anchor.Top), DisplayAreaFallback.Nearest).WorkArea;
        OnWorkArea(work, Scale);
        var size = MeasureContent();
        int width = size.Width, height = size.Height;
        if (width == 0 || height == 0) return;

        var gap = (int)(Gap * Scale);
        // Max before Min: a window wider than the screen starts at its left edge instead of throwing.
        var x = Math.Max(work.X, Math.Min((anchor.Left + anchor.Right - width) / 2, work.X + work.Width - width));
        var taskbarAtTop = anchor.Top < work.Y + work.Height / 2;
        OpensUpward = !taskbarAtTop;
        var y = taskbarAtTop ? Math.Max(anchor.Bottom, work.Y) + gap : Math.Min(anchor.Top, work.Y + work.Height) - height - gap;
        AppWindow.ResizeClient(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(x, y));
    }
}
