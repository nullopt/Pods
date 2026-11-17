using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;

namespace Pods.App;

public partial class App : Application
{
    TaskbarIcon? tray;
    MainWindow? mainWindow;
    DispatcherQueue? ui;

    public App() => InitializeComponent();

    /// <summary>The running app, or null before <see cref="Application.Start"/> has created it.</summary>
    public static App? Instance => Current as App;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ui = DispatcherQueue.GetForCurrentThread();
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
            mainWindow = new MainWindow();
            mainWindow.Closed += (_, _) => mainWindow = null;
        }
        mainWindow.Activate();
    }

    TaskbarIcon CreateTray()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("TrayOpen", ShowMainWindow));
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
        // Returns only if the restart could not be started.
        _ = AppInstance.Restart("");
        tray = CreateTray();
    }

    void ExitApp()
    {
        tray?.Dispose();
        tray = null;
        mainWindow?.Close();
        Exit();
    }
}
