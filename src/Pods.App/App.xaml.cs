using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Pods.App.Devices;
using Pods.App.Logging;
using Pods.Core.Devices;
using Pods.Core.Settings;
using Serilog;

namespace Pods.App;

public partial class App : Application
{
    TaskbarIcon? tray;
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
        Headphones = new AdvertHeadphoneSource(new BleAdvertSource(), TimeProvider.System);
        Headphones.Start();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        tray = CreateTray();
        ShowMainWindow();
    }

    /// <summary>A second launch was redirected here (single instance).</summary>
    internal void OnActivatedAgain() => ui?.TryEnqueue(ShowMainWindow);

    /// <summary>Creates the window if none exists, else brings it to the front. Closing destroys it.</summary>
    public void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            mainWindow = new MainWindow(Headphones);
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
            ToolTipText = Strings.Get("AppDisplayName"),
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Tray.ico")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(ShowMainWindow),
            NoLeftClickDelay = true,
        };
        icon.ForceCreate();
        return icon;
    }

    static MenuFlyoutItem MenuItem(string key, Action action) =>
        new() { Text = Strings.Get(key), Command = new RelayCommand(action) };

    void Restart()
    {
        tray?.Dispose();
        tray = null;
        Program.ReleaseInstanceKey();
        Log.CloseAndFlush();
        // Returns only if the restart could not be started.
        var failure = AppInstance.Restart("");
        LogSetup.Configure(DataFolder, Settings.Get(SettingKeys.LogLevel));
        Log.Error("Restart failed: {Reason}", failure);
        AppInstance.FindOrRegisterForKey("main");
        tray = CreateTray();
    }

#if DEBUG
    /// <summary>Swaps the BLE source for demo devices until the next restart.</summary>
    void ShowDemoDevices()
    {
        if (Headphones is FakeHeadphoneSource) return;
        Headphones.Stop();
        Headphones = new FakeHeadphoneSource(ui!);
        Headphones.Start();
        mainWindow?.Close();
        ShowMainWindow();
    }
#endif

    void ExitApp()
    {
        Headphones.Stop();
        tray?.Dispose();
        tray = null;
        mainWindow?.Close();
        Log.CloseAndFlush();
        Exit();
    }
}
