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
    }

    /// <summary>Shows the window centred on the icon (screen pixels), optionally taking focus.</summary>
    public void ShowAt(int left, int top, int right, int bottom, bool activate)
    {
        anchor = (left, top, right, bottom);
        Place();
        AppWindow.Show(activate);
        StyleCorners();
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
}
