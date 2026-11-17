using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace Pods.App.Tray;

/// <summary>
/// A borderless, always-on-top window holding the tray card, shown without taking focus and sized
/// to its content. Created on hover and closed when the pointer leaves, so it costs nothing at rest.
/// </summary>
internal sealed partial class TrayCardWindow : Window
{
    const int Gap = 8;   // DIPs between the card and the taskbar
    readonly TrayCardView view;
    readonly nint hwnd;
    (int Left, int Top, int Right, int Bottom) anchor;

    public TrayCardWindow(TrayCardViewModel card)
    {
        // Sized to its content rather than the window, so rings added while it is open resize the window (SizeChanged).
        view = new TrayCardView(card) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Content = view;
        // The context-menu presenter: a frameless popup that stays on top.
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = Strings.Get("AppDisplayName");
        hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        view.SizeChanged += (_, _) => Place();
        Closed += (_, _) => view.Detach();
    }

    /// <summary>Shows the card centred on the icon (screen pixels), above it, or below a top taskbar.</summary>
    public void ShowAbove(int left, int top, int right, int bottom)
    {
        anchor = (left, top, right, bottom);
        Place();
        AppWindow.Show(activateWindow: false);
        StyleCorners();
    }

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
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = (int)Math.Ceiling(view.DesiredSize.Width * scale);
        var height = (int)Math.Ceiling(view.DesiredSize.Height * scale);
        if (width == 0 || height == 0) return;

        var work = DisplayArea.GetFromPoint(new PointInt32(anchor.Left, anchor.Top), DisplayAreaFallback.Nearest).WorkArea;
        var gap = (int)(Gap * scale);
        // Max before Min: a card wider than the screen starts at its left edge instead of throwing.
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
