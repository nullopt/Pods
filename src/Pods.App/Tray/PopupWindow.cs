using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace Pods.App.Tray;

/// <summary>
/// A borderless, always-on-top window sized to its content, kept out of Alt+Tab. Subclasses decide
/// where it goes. Created when needed and closed afterwards, so it costs nothing at rest.
/// </summary>
internal abstract partial class PopupWindow : Window
{
    protected PopupWindow(FrameworkElement view)
    {
        // A Canvas measures its child without limits, so the content can grow past the current window size.
        View = view;
        Content = new Canvas { Children = { view } };
        // The context-menu presenter: a frameless popup that stays on top.
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = Strings.Get("AppDisplayName");
        Hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        view.Loaded += (_, _) =>
        {
            if (!IsWaitingForLoad) return;
            IsWaitingForLoad = false;
            Reveal(activateWhenLoaded);
        };
    }

    protected FrameworkElement View { get; }
    protected nint Hwnd { get; }

    /// <summary>Shown off screen until the content has loaded; placing it now would use a wrong size.</summary>
    protected bool IsWaitingForLoad { get; private set; }

    bool activateWhenLoaded;

    /// <summary>Shows the window in place, optionally taking focus.</summary>
    /// <remarks>
    /// The content only loads once the window is shown, and until then it cannot be measured, so a new
    /// window would appear at a wrong size and jump. It is first shown far off screen and placed once
    /// its content has loaded.
    /// </remarks>
    protected void ShowWhenLoaded(bool activate)
    {
        if (View.IsLoaded)
        {
            Reveal(activate);
            return;
        }
        IsWaitingForLoad = true;
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
        if (activate) SetForegroundWindow(Hwnd);
        OnShown();
    }

    /// <summary>Sizes and moves the window for its content. Called just before it is revealed.</summary>
    protected abstract void Place();

    /// <summary>Called once the window is on screen with its content laid out.</summary>
    protected virtual void OnShown() { }

    protected double Scale => View.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>The content's size in physical pixels.</summary>
    protected SizeInt32 MeasureContent()
    {
        View.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new SizeInt32((int)Math.Ceiling(View.DesiredSize.Width * Scale), (int)Math.Ceiling(View.DesiredSize.Height * Scale));
    }

    /// <summary>Rounded corners from the system, without the 1 px outline it draws by default. Applied once shown.</summary>
    void StyleCorners()
    {
        var round = DwmCornerRound;
        DwmSetWindowAttribute(Hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        var none = DwmColorNone;
        DwmSetWindowAttribute(Hwnd, DwmBorderColor, ref none, sizeof(uint));
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
