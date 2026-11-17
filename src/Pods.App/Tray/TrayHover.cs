using System.Runtime.InteropServices;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;

namespace Pods.App.Tray;

/// <summary>
/// Shows the battery card while the pointer rests on the tray icon. The tray library reports
/// pointer moves over the icon but not when the pointer leaves, so after the first move a short
/// timer follows the cursor until the card is hidden again; nothing runs while the pointer is elsewhere.
/// Clicking hides the card until the pointer next arrives.
/// </summary>
internal sealed partial class TrayHover : IDisposable
{
    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    const int ShowAfterTicks = 4;   // ~400 ms, about the system tooltip delay

    readonly TaskbarIcon icon;
    readonly TrayCardViewModel card;
    readonly DispatcherQueue ui;
    readonly DispatcherQueueTimer timer;
    TrayCardWindow? window;
    int ticksInside;
    bool suppressed;

    public TrayHover(TaskbarIcon icon, TrayCardViewModel card, DispatcherQueue ui)
    {
        this.icon = icon;
        this.card = card;
        this.ui = ui;
        timer = ui.CreateTimer();
        timer.Interval = Tick;
        timer.Tick += (_, _) => OnTick();
        icon.TrayIcon.MessageWindow.MouseEventReceived += OnMouseEvent;
    }

    void OnMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e) =>
        ui.TryEnqueue(() =>
        {
            if (e.MouseEvent == MouseEvent.MouseMove)
            {
                if (!timer.IsRunning) timer.Start();
            }
            else
            {
                suppressed = true;
                Hide();
            }
        });

    void OnTick()
    {
        if (IconRect() is not { } rect || !GetCursorPos(out var cursor) || !rect.Contains(cursor))
        {
            ticksInside = 0;
            suppressed = false;
            Hide();
            timer.Stop();
            return;
        }
        if (++ticksInside == ShowAfterTicks && !suppressed) Show(rect);
    }

    void Show(Rect rect)
    {
        window ??= new TrayCardWindow(card);
        window.ShowAbove(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    void Hide()
    {
        window?.Close();
        window = null;
    }

    Rect? IconRect()
    {
        var id = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Window = icon.TrayIcon.WindowHandle,
            Guid = icon.TrayIcon.Id,
        };
        return Shell_NotifyIconGetRect(id, out var rect) == 0 ? rect : null;
    }

    public void Dispose()
    {
        icon.TrayIcon.MessageWindow.MouseEventReceived -= OnMouseEvent;
        timer.Stop();
        Hide();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconGetRect(in NotifyIconIdentifier identifier, out Rect iconLocation);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);
}
