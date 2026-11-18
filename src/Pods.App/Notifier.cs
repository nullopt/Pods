using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Serilog;

namespace Pods.App;

/// <summary>
/// Pods' toast notifications. Clicks on one, or on one of its buttons, reach <c>invoked</c> on the UI
/// thread with the notification's arguments. Where notifications are unavailable, showing does nothing.
/// </summary>
internal sealed class Notifier
{
    bool registered;

    public Notifier(DispatcherQueue ui, Action<Dictionary<string, string>> invoked)
    {
        // Subscribed once, before registering, as the notification API requires.
        AppNotificationManager.Default.NotificationInvoked += (_, e) =>
        {
            var arguments = new Dictionary<string, string>(e.Arguments);
            ui.TryEnqueue(() => invoked(arguments));
        };
        Register();
    }

    public void Register()
    {
        if (registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            registered = true;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Notifications unavailable");
        }
    }

    public void Unregister()
    {
        if (!registered) return;
        registered = false;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception e) { Log.Warning(e, "Unregistering notifications failed"); }
    }

    public void Show(AppNotification notification)
    {
        if (!registered) return;
        try
        {
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Showing a notification failed");
        }
    }
}
