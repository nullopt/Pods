using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace Pods.App.Tray;

/// <summary>
/// A borderless, always-on-top window anchored to the tray icon and sized to its content: above the
/// icon, or below a top taskbar. Created when needed and closed afterwards, so it costs nothing at rest.
/// </summary>
internal abstract partial class TrayAnchoredWindow : Window
{
    const int Gap = 8;   // DIPs between the window and the taskbar
    readonly FrameworkElement view;
    readonly nint hwnd;
    (int Left, int Top, int Right, int Bottom) anchor;

    protected TrayAnchoredWindow(FrameworkElement view)
    {
        // A Canvas measures its child without limits, so the content can grow past the current window
        // size (a headset connects); its SizeChanged then resizes the window both ways.
        this.view = view;
        Content = new Canvas { Children = { view } };
        // The context-menu presenter: a frameless popup that stays on top.
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = Strings.Get("AppDisplayName");
        hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        view.SizeChanged += (_, _) => Place();
        view.Loaded += (_, _) =>
        {
            if (!waitingForLoad) return;
            waitingForLoad = false;
            Reveal(activateWhenLoaded);
        };
    }

    bool waitingForLoad, activateWhenLoaded;

    /// <summary>Shows the window centred on the icon (screen pixels), optionally taking focus.</summary>
    /// <remarks>
    /// The content only loads once the window is shown, and until then it cannot be measured, so a new
    /// window would appear at a wrong size and jump. It is first shown far off screen and moved into
    /// place once its content has loaded.
    /// </remarks>
    public void ShowAt(int left, int top, int right, int bottom, bool activate)
    {
        anchor = (left, top, right, bottom);
        if (view.IsLoaded)
        {
            Reveal(activate);
            return;
        }
        waitingForLoad = true;
        activateWhenLoaded = activate;
        AppWindow.Move(new PointInt32(-32000, -32000));
        AppWindow.Show(activateWindow: false);
    }

    void Reveal(bool activate)
    {
        Place();
        AppWindow.Show(activate);
        StyleCorners();
        // Showing activates only within this app; a tray click lets us take the foreground from the shell.
        if (activate) SetForegroundWindow(hwnd);
        OnShown();
    }

    /// <summary>Called once the window is in place on screen with its content laid out.</summary>
    protected virtual void OnShown() { }

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

    /// <summary>Rounded corners from the system, without the 1 px outline it draws by default. Applied once shown.</summary>
    void StyleCorners()
    {
        var round = DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        var none = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DwmBorderColor, ref none, sizeof(uint));
    }

    void Place()
    {
        if (offScreen || waitingForLoad) return;
        var scale = view.XamlRoot?.RasterizationScale ?? 1.0;
        var work = DisplayArea.GetFromPoint(new PointInt32(anchor.Left, anchor.Top), DisplayAreaFallback.Nearest).WorkArea;
        OnWorkArea(work, scale);
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = (int)Math.Ceiling(view.DesiredSize.Width * scale);
        var height = (int)Math.Ceiling(view.DesiredSize.Height * scale);
        if (width == 0 || height == 0) return;

        var gap = (int)(Gap * scale);
        // Max before Min: a window wider than the screen starts at its left edge instead of throwing.
        var x = Math.Max(work.X, Math.Min((anchor.Left + anchor.Right - width) / 2, work.X + work.Width - width));
        var taskbarAtTop = anchor.Top < work.Y + work.Height / 2;
        OpensUpward = !taskbarAtTop;
        var y = taskbarAtTop ? Math.Max(anchor.Bottom, work.Y) + gap : Math.Min(anchor.Top, work.Y + work.Height) - height - gap;
        AppWindow.ResizeClient(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(x, y));
    }

    const int DwmWindowCornerPreference = 33;
    const int DwmBorderColor = 34;
    const int DwmCornerRound = 2;
    const uint DwmColorNone = 0xFFFFFFFE;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
