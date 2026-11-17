using Microsoft.UI.Xaml.Controls;
using Pods.App.Devices;

namespace Pods.App.Views;

internal sealed partial class DevicesView : UserControl
{
    public DevicesView(DevicesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public DevicesViewModel ViewModel { get; }
}
