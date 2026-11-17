using Microsoft.UI.Dispatching;
using Pods.App.Devices;
using Pods.App.Views;
using Pods.Core.Devices;
using Windows.Graphics;

namespace Pods.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    readonly DevicesViewModel devices;

    internal MainWindow(IHeadphoneSource headphones)
    {
        InitializeComponent();
        Title = Strings.Get("MainWindowTitle");
        AppWindow.SetIcon("Assets/Pods.ico");
        AppWindow.Resize(new SizeInt32(480, 640));
        ExtendsContentIntoTitleBar = true;

        devices = new DevicesViewModel(headphones, DispatcherQueue.GetForCurrentThread());
        Root.Children.Add(new DevicesView(devices));
        Closed += (_, _) => devices.Dispose();
    }
}
