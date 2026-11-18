using System.Diagnostics;
using System.Runtime.InteropServices;
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
    RectInt32 outer;
    bool leaving, closed;

    /// <param name="iconRect">The tray icon, to pick its screen; null uses the main screen.</param>
    public LidPopupWindow(DeviceCardViewModel card, TrayHover.Rect? iconRect) : base(CreateView(card, out var close))
    {
        Card = card;
        this.iconRect = iconRect;
        // Until the tile draws, and in the window's corners, show the tile's colour rather than a default one.
        if (Content is Panel root) root.Background = (Brush)Application.Current.Resources["PodsCardBackground"];
        close.Click += (_, _) => Dismiss();
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = ShowFor;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Dismiss();
        View.PointerEntered += (_, _) => timer.Stop();
        View.PointerExited += (_, _) =>
        {
            // Exits bubble up from the tile's parts, and the pointer's last position is still inside, so
            // ask where the cursor really is.
            if (!leaving && !CursorOverWindow()) timer.Start();
        };
    }

    protected override void OnClosed()
    {
        closed = true;
        timer.Stop();
        CompositionTarget.Rendering -= OnSlideFrame;
        Dismissed?.Invoke();
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
        // The tile alone; the window's rounded corners take its colour.
        return new Border
        {
            Background = (Brush)Application.Current.Resources["PodsCardBackground"],
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
        outer = display.OuterBounds;
        AppWindow.ResizeClient(size);
        currentY = offY;
        AppWindow.Move(new PointInt32(x, offY));
    }

    protected override void OnShown()
    {
        TuckUnderTaskbar();
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

    /// <summary>
    /// Puts the popup directly beneath this screen's taskbar in the window stack (still above other
    /// windows), so it rises from behind the taskbar instead of over it.
    /// </summary>
    void TuckUnderTaskbar()
    {
        foreach (var cls in (string[])["Shell_TrayWnd", "Shell_SecondaryTrayWnd"])
        {
            for (var bar = FindWindowEx(0, 0, cls, null); bar != 0; bar = FindWindowEx(0, bar, cls, null))
            {
                if (!GetWindowRect(bar, out var r)) continue;
                var onScreen = r.Left < outer.X + outer.Width && r.Right > outer.X && r.Top < outer.Y + outer.Height && r.Bottom > outer.Y;
                if (!onScreen) continue;
                SetWindowPos(Hwnd, bar, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
                return;
            }
        }
    }

    bool CursorOverWindow()
    {
        if (!GetCursorPos(out var p)) return false;
        var (at, size) = (AppWindow.Position, AppWindow.Size);
        return p.X >= at.X && p.X < at.X + size.Width && p.Y >= at.Y && p.Y < at.Y + size.Height;
    }

    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct Win32Rect { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string cls, string? title);

    [StructLayout(LayoutKind.Sequential)]
    struct Win32Point { public int X, Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Win32Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Win32Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
