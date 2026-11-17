using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.System.Power;
using Pods.App.Devices;
using Pods.App.Logging;
using Pods.App.Tray;
using Pods.Core.Devices;
using Pods.Core.Settings;
using Serilog;

namespace Pods.App;

public partial class App : Application
{
    TaskbarIcon? tray;
    TrayHover? trayHover;
    readonly LowBatteryMonitor lowBattery = new();
    readonly TrayCardViewModel trayCard = new();
    bool trayLow;
    bool notificationsRegistered;
    MainWindow? mainWindow;
    DispatcherQueue? ui;

    public App() => InitializeComponent();

    /// <summary>The running app, or null before <see cref="Application.Start"/> has created it.</summary>
    public static App? Instance => Current as App;

    /// <summary>%LOCALAPPDATA%\Pods: settings and logs.</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pods");

    public SettingsStore Settings { get; private set; } = null!;

    /// <summary>Devices shown in the window; scanning runs while the app runs.</summary>
    internal IHeadphoneSource Headphones { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(DataFolder);
        Settings = SettingsStore.Load(DataFolder);
        LogSetup.Configure(DataFolder, Settings.Get(SettingKeys.LogLevel));
        foreach (var warning in Settings.Warnings) Log.Warning("Settings: {Warning}", warning);
        Log.Information("Pods {Version} started", typeof(App).Assembly.GetName().Version);
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");

        ui = DispatcherQueue.GetForCurrentThread();
        // Subscribed once, before registering, as the notification API requires.
        AppNotificationManager.Default.NotificationInvoked += (_, _) => ui?.TryEnqueue(ShowMainWindow);
        RegisterNotifications();
        var paired = new WindowsPairedDeviceSource();
        Headphones = new HeadphoneSource(paired, new BleAdvertSource(), new KsConnectionControl(paired), TimeProvider.System);
        Headphones.Changed += OnHeadphonesChangedAnyThread;
        Headphones.Start();
        PowerManager.SystemSuspendStatusChanged += OnSuspendStatusChanged;
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        tray = CreateTray();
        OnHeadphonesChanged();
        ShowMainWindow();
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
    internal void OnActivatedAgain() => ui?.TryEnqueue(ShowMainWindow);

    /// <summary>Creates the window if none exists, else brings it to the front. Closing destroys it.</summary>
    public void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            mainWindow = new MainWindow(Headphones, Settings);
            mainWindow.Closed += (_, _) => mainWindow = null;
        }
        mainWindow.Activate();
    }

    TaskbarIcon CreateTray()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("TrayOpen", ShowMainWindow));
#if DEBUG
        menu.Items.Add(MenuItem("TrayDemoDevices", ShowDemoDevices));
#endif
        menu.Items.Add(MenuItem("TrayRestart", Restart));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("TrayExit", ExitApp));

        var icon = new TaskbarIcon
        {
            IconSource = TrayIcon(trayLow),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(ShowMainWindow),
            NoLeftClickDelay = true,
        };
        icon.ForceCreate();
        trayHover = new TrayHover(icon, trayCard, ui!);
        return icon;
    }

    void DisposeTray()
    {
        trayHover?.Dispose();
        trayHover = null;
        tray?.Dispose();
        tray = null;
    }

    static BitmapImage TrayIcon(bool low) => new(new Uri(low ? "ms-appx:///Assets/TrayLow.ico" : "ms-appx:///Assets/Tray.ico"));

    void OnHeadphonesChangedAnyThread() => ui?.TryEnqueue(OnHeadphonesChanged);

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
        if (!notificationsRegistered) return;
        var body = string.Format(Strings.Get("LowBattery" + (alert.Part == BatteryPartKind.Buds ? "Single" : alert.Part)), alert.Name, alert.Percent);
        Log.Information("Low battery: {Body}", body);
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(Strings.Get("LowBatteryTitle"))
                .AddText(body)
                .AddArgument("action", "open")
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Showing a notification failed");
        }
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
        Headphones.Changed -= OnHeadphonesChangedAnyThread;
        Headphones.Stop();
        Headphones = new FakeHeadphoneSource(ui!);
        Headphones.Changed += OnHeadphonesChangedAnyThread;
        Headphones.Start();
        OnHeadphonesChanged();
        mainWindow?.Close();
        ShowMainWindow();
    }
#endif

    void ExitApp()
    {
        PowerManager.SystemSuspendStatusChanged -= OnSuspendStatusChanged;
        Headphones.Changed -= OnHeadphonesChangedAnyThread;
        Headphones.Stop();
        (Headphones as IDisposable)?.Dispose();
        DisposeTray();
        UnregisterNotifications();
        mainWindow?.Close();
        Log.CloseAndFlush();
        Exit();
    }
}
