using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.System.Power;
using Pods.App.Devices;
using Pods.App.Logging;
using Pods.App.Media;
using Pods.App.Tray;
using Pods.App.Updates;
using Pods.App.Views;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Media;
using Pods.Core.Settings;
using Pods.Core.Updates;
using Serilog;

namespace Pods.App;

public partial class App : Application
{
    TaskbarIcon? tray;
    TrayHover? trayHover;
    readonly LowBatteryMonitor lowBattery = new();
    MediaAutomation media = null!;
    readonly TrayCardViewModel trayCard = new();
    bool trayLow;
    bool notificationsRegistered;
    TrayFlyoutWindow? flyout;
    LidPopupWindow? lidPopup;
    DevicesViewModel? flyoutDevices;
    long flyoutClosedAt = long.MinValue / 2;
    DispatcherQueue? ui;
    UpdateService? updates;

    public App() => InitializeComponent();

    /// <summary>The running app, or null before <see cref="Application.Start"/> has created it.</summary>
    public static App? Instance => Current as App;

    /// <summary>%LOCALAPPDATA%\Pods: settings and logs.</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pods");

    public SettingsStore Settings { get; private set; } = null!;

    /// <summary>The Animations setting; on before settings load (design time, tests).</summary>
    internal static bool AnimationsOn => Instance?.Settings is not { } settings || settings.Get(SettingKeys.Animations);

    /// <summary>Devices shown in the flyout and tray card; scanning runs while the app runs.</summary>
    internal IHeadphoneSource Headphones { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(DataFolder);
        Settings = SettingsStore.Load(DataFolder);
        LogSetup.Configure(DataFolder, Settings.Get(SettingKeys.LogLevel));
        foreach (var warning in Settings.Warnings) Log.Warning("Settings: {Warning}", warning);
        // Device files are read on first use, so their warnings come later.
        Settings.WarningAdded += warning => Log.Warning("Settings: {Warning}", warning);
        Log.Information("Pods {Version} started", typeof(App).Assembly.GetName().Version);
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");

        ui = DispatcherQueue.GetForCurrentThread();
        // Subscribed once, before registering, as the notification API requires.
        AppNotificationManager.Default.NotificationInvoked += (_, e) =>
        {
            var arguments = new Dictionary<string, string>(e.Arguments);
            ui?.TryEnqueue(() => OnNotificationInvoked(arguments));
        };
        RegisterNotifications();
        var paired = new WindowsPairedDeviceSource();
        Headphones = new HeadphoneSource(paired, new BleAdvertSource(), new KsConnectionControl(paired), TimeProvider.System);
        AttachHeadphones();
        media = CreateMediaAutomation();
        Headphones.Start();
        PowerManager.SystemSuspendStatusChanged += OnSuspendStatusChanged;
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // Tray only: nothing shows until the icon is clicked.
        tray = CreateTray();
        OnHeadphonesChanged();
        ui.TryEnqueue(DispatcherQueuePriority.Low, WarmUpFlyout);
        updates = new UpdateService(Settings, ui, ShowNotification, ExitApp);
        updates.Start();
        // A notification clicked after Pods exited starts it with that activation instead of NotificationInvoked.
        if (AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.AppNotification) OpenFlyout();
    }

    /// <summary>After sleep the watchers may be stale: restart them; the cards stay until the new lists arrive.</summary>
    void OnSuspendStatusChanged(object? sender, object args)
    {
        if (PowerManager.SystemSuspendStatus is not (SystemSuspendStatus.AutoResume or SystemSuspendStatus.ManualResume)) return;
        Log.Information("Resumed from sleep; restarting device watchers");
        Headphones.Stop();
        Headphones.Start();
    }

    /// <summary>A second launch was redirected here (single instance).</summary>
    internal void OnActivatedAgain() => ui?.TryEnqueue(OpenFlyout);

    /// <summary>
    /// Pressing the icon while the flyout is open takes focus from it, so it has already closed by the
    /// time the click arrives; a click this soon after a close is that same press and leaves it closed.
    /// </summary>
    const long ReopenGuardMs = 300;

    /// <summary>The icon's left click: opens the flyout, or closes it when open.</summary>
    void ToggleFlyout()
    {
        if (flyout is not null) flyout.Close();
        else if (Environment.TickCount64 - flyoutClosedAt >= ReopenGuardMs) OpenFlyout();
    }

    /// <summary>Opens the flyout above the tray icon, or brings an open one to the front. Closing destroys it.</summary>
    void OpenFlyout()
    {
        if (flyout is not null)
        {
            flyout.Activate();
            return;
        }
        flyoutDevices = new DevicesViewModel(Headphones, Settings, ui!, OnSettingsChanged);
        flyout = new TrayFlyoutWindow(new DevicesView(flyoutDevices));
        flyout.Dismissed += OnFlyoutClosed;
        if (trayHover is not null) trayHover.Suspended = true;
        // Without the icon's position (the shell did not say), anchor to the corner of the main screen.
        var rect = trayHover?.IconRect();
        var work = DisplayArea.Primary.WorkArea;
        if (rect is { } r) flyout.ShowAbove(r.Left, r.Top, r.Right, r.Bottom);
        else flyout.ShowAbove(work.X + work.Width - 1, work.Y + work.Height, work.X + work.Width, work.Y + work.Height);
    }

    /// <summary>
    /// A setting was written from the flyout: refresh the tray dot, hover card and alerts, and the tiles.
    /// Queued, since it is called from inside a drop-down's selection change.
    /// </summary>
    void OnSettingsChanged() => ui?.TryEnqueue(() =>
    {
        OnHeadphonesChanged();
        flyoutDevices?.Refresh();
    });

    /// <summary>
    /// Opens a throwaway flyout off screen once at startup and closes it after its first frame, so the
    /// first real click does not pay for loading code and laying out the view.
    /// </summary>
    void WarmUpFlyout()
    {
        var devices = new DevicesViewModel(Headphones, Settings, ui!, () => { });
        var view = new DevicesView(devices);
        var window = new TrayFlyoutWindow(view);
        var closed = false;
        window.Dismissed += () =>
        {
            closed = true;
            devices.Dispose();
        };
        view.Loaded += (_, _) => CompositionTarget.Rendering += CloseAfterFrame;
        window.ShowOffScreen();

        void CloseAfterFrame(object? sender, object e)
        {
            CompositionTarget.Rendering -= CloseAfterFrame;
            if (!closed) window.Close();
        }
    }

    void OnFlyoutClosed()
    {
        flyoutDevices?.Dispose();
        flyoutDevices = null;
        flyout = null;
        flyoutClosedAt = Environment.TickCount64;
        if (trayHover is not null) trayHover.Suspended = false;
    }

    TaskbarIcon CreateTray()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("TrayOpen", OpenFlyout));
#if DEBUG
        menu.Items.Add(MenuItem("TrayDemoDevices", ShowDemoDevices));
        menu.Items.Add(MenuItem("TrayDemoLidPopup", () => (Headphones as FakeHeadphoneSource)?.RaiseLidOpened()));
        menu.Items.Add(MenuItem("TrayDemoEar", () => (Headphones as FakeHeadphoneSource)?.ToggleEar()));
#endif
        menu.Items.Add(MenuItem("TrayRestart", Restart));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("TrayExit", ExitApp));

        var icon = new TaskbarIcon
        {
            // Kept as the icon's accessible name; the standard text tip is off so it never covers the card.
            ToolTipText = Strings.Get("AppDisplayName"),
            IconSource = TrayIcon(trayLow),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(ToggleFlyout),
            NoLeftClickDelay = true,
        };
        icon.TrayIcon.UseStandardTooltip = false;
        icon.ForceCreate();
        trayHover = new TrayHover(icon, trayCard, ui!);
        return icon;
    }

    void DisposeTray()
    {
        flyout?.Close();
        lidPopup?.Close();
        trayHover?.Dispose();
        trayHover = null;
        tray?.Dispose();
        tray = null;
    }

    static BitmapImage TrayIcon(bool low) => new(new Uri(low ? "ms-appx:///Assets/TrayLow.ico" : "ms-appx:///Assets/Tray.ico"));

    void AttachHeadphones()
    {
        Headphones.Changed += OnHeadphonesChangedAnyThread;
        Headphones.LidOpened += OnLidOpenedAnyThread;
        Headphones.LidClosed += OnLidClosedAnyThread;
        Headphones.AutoConnectReady += OnAutoConnectReadyAnyThread;
    }

    void DetachHeadphones()
    {
        Headphones.Changed -= OnHeadphonesChangedAnyThread;
        Headphones.LidOpened -= OnLidOpenedAnyThread;
        Headphones.LidClosed -= OnLidClosedAnyThread;
        Headphones.AutoConnectReady -= OnAutoConnectReadyAnyThread;
    }

    void OnHeadphonesChangedAnyThread() => ui?.TryEnqueue(OnHeadphonesChanged);

    void OnAutoConnectReadyAnyThread(string key) => ui?.TryEnqueue(() => OnAutoConnectReady(key));

    /// <summary>A disconnected headset's pods were taken out of a case opened nearby: connect it, if its auto connect is on.</summary>
    void OnAutoConnectReady(string key)
    {
        if (!Settings.GetForDevice(SettingKeys.AutoConnect, key)) return;
        Log.Information("Auto connecting {Name}", Headphones.Current.FirstOrDefault(s => s.Key == key)?.Name ?? key);
        Headphones.Connect(key);
    }

    /// <summary>Battery or connection changed: refresh the tray card and the low dot.</summary>
    void OnHeadphonesChanged()
    {
        var connected = Headphones.Current.Where(s => s.State == DeviceState.Connected).ToList();
        foreach (var alert in lowBattery.Update(connected, ThresholdFor)) ShowLowBatteryNotification(alert);
        if (trayLow != lowBattery.AnyLow)
        {
            trayLow = lowBattery.AnyLow;
            if (tray is not null) tray.IconSource = TrayIcon(trayLow);
        }
        trayCard.Update(connected, ThresholdFor);
        media.Update(Headphones.Current, key => MediaSettings.For(Settings, key));
        TurntableView.Keep(connected.Select(s => Turntable.Resolve(s.ModelId, s.Colour, TurntableView.Available)));
        if (lidPopup is not null)
        {
            if (Headphones.Current.FirstOrDefault(s => s.Key == lidPopup.Key) is { } shown) lidPopup.Card.Update(shown, Headphones.IsAdapterAvailable);
            else lidPopup.Dismiss();
        }
    }

    void OnLidOpenedAnyThread(string key) => ui?.TryEnqueue(() => OnLidOpened(key));

    void OnLidClosedAnyThread(string key) => ui?.TryEnqueue(() =>
    {
        if (lidPopup?.Key == key) lidPopup.Dismiss();
    });

    /// <summary>A case opened near the PC: show its tile at the bottom of the screen, if the device wants that.</summary>
    void OnLidOpened(string key)
    {
        if (Headphones.Current.FirstOrDefault(s => s.Key == key) is not { } snapshot
            || !LidPopupSetting.IsOn(Settings, key, snapshot.ModelId)) return;
        if (lidPopup?.Key == key)
        {
            lidPopup.RestartTimer();
            return;
        }
        lidPopup?.Dismiss();
        var card = new DeviceCardViewModel(snapshot, Headphones, Settings, Headphones.IsAdapterAvailable, OnSettingsChanged);
        var popup = new LidPopupWindow(card, trayHover?.IconRect());
        popup.Dismissed += () =>
        {
            if (lidPopup == popup) lidPopup = null;
        };
        lidPopup = popup;
        Log.Information("Lid opened: {Name}", snapshot.Name);
        popup.Show();
    }

    /// <summary>Clicking a Pods notification opens the device list.</summary>
    void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.Register();
            notificationsRegistered = true;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Notifications unavailable");
        }
    }

    void UnregisterNotifications()
    {
        if (!notificationsRegistered) return;
        notificationsRegistered = false;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception e) { Log.Warning(e, "Unregistering notifications failed"); }
    }

    void ShowLowBatteryNotification(LowBatteryAlert alert)
    {
        var body = string.Format(Strings.Get("LowBattery" + (alert.Part == BatteryPartKind.Buds ? "Single" : alert.Part)), alert.Name, alert.Percent);
        Log.Information("Low battery: {Body}", body);
        ShowNotification(new AppNotificationBuilder()
            .AddText(Strings.Get("LowBatteryTitle"))
            .AddText(body)
            .AddArgument("action", "open")
            .BuildNotification());
    }

    void ShowNotification(AppNotification notification)
    {
        if (!notificationsRegistered) return;
        try
        {
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Showing a notification failed");
        }
    }

    /// <summary>A Pods notification or one of its buttons was clicked.</summary>
    void OnNotificationInvoked(Dictionary<string, string> arguments)
    {
        switch (arguments.GetValueOrDefault("action"))
        {
            case "update" when updates is not null && ReleaseParser.ParseVersion(arguments.GetValueOrDefault("version") ?? "") is { } version:
                _ = updates.InstallAsync(version);
                break;
            case "notes":
                updates?.OpenReleasePage();
                break;
            default:
                OpenFlyout();
                break;
        }
    }

    static MediaAutomation CreateMediaAutomation()
    {
        var automation = new MediaAutomation(new SmtcMediaControl());
        automation.Acted += (key, action) => Log.Information("Media: {Action} for {Device}", action, key);
        automation.Failed += ex => Log.Warning(ex, "Media action failed");
        return automation;
    }

    int ThresholdFor(string key) => Settings.GetForDevice(SettingKeys.LowBatteryPercent, key);

    static MenuFlyoutItem MenuItem(string key, Action action) =>
        new() { Text = Strings.Get(key), Command = new RelayCommand(action) };

    void Restart()
    {
        DisposeTray();
        UnregisterNotifications();
        Program.ReleaseInstanceKey();
        Log.CloseAndFlush();
        // Returns only if the restart could not be started.
        var failure = AppInstance.Restart("");
        LogSetup.Configure(DataFolder, Settings.Get(SettingKeys.LogLevel));
        Log.Error("Restart failed: {Reason}", failure);
        AppInstance.FindOrRegisterForKey("main");
        tray = CreateTray();
        RegisterNotifications();
    }

#if DEBUG
    /// <summary>Swaps the BLE source for demo devices until the next restart.</summary>
    void ShowDemoDevices()
    {
        if (Headphones is FakeHeadphoneSource) return;
        DetachHeadphones();
        Headphones.Stop();
        lidPopup?.Close();
        Headphones = new FakeHeadphoneSource(ui!);
        // The old source's headsets are gone, not disconnected: start afresh instead of pausing.
        media = CreateMediaAutomation();
        AttachHeadphones();
        Headphones.Start();
        OnHeadphonesChanged();
        // An open flyout still shows the old source's devices.
        if (flyout is null) return;
        flyout.Close();
        OpenFlyout();
    }
#endif

    void ExitApp()
    {
        PowerManager.SystemSuspendStatusChanged -= OnSuspendStatusChanged;
        updates?.Dispose();
        DetachHeadphones();
        Headphones.Stop();
        (Headphones as IDisposable)?.Dispose();
        DisposeTray();
        UnregisterNotifications();
        Log.CloseAndFlush();
        Exit();
    }
}
