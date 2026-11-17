using Windows.Graphics;

namespace Pods.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.Get("MainWindowTitle");
        AppWindow.SetIcon("Assets/Pods.ico");
        AppWindow.Resize(new SizeInt32(480, 640));
    }
}
