using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pods.App.Devices;

namespace Pods.App.Views;

/// <summary>One headset's tile, shared by the flyout and the lid-open popup.</summary>
internal sealed partial class DeviceTile : UserControl
{
    public DeviceTile() => InitializeComponent();

    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card), typeof(DeviceCardViewModel), typeof(DeviceTile), new PropertyMetadata(null, (d, _) => ((DeviceTile)d).Bindings.Update()));

    /// <summary>The headset shown; a recycled tile gets a new one.</summary>
    public DeviceCardViewModel? Card { get => (DeviceCardViewModel?)GetValue(CardProperty); set => SetValue(CardProperty, value); }

    public static readonly DependencyProperty CornerProperty = DependencyProperty.Register(
        nameof(Corner), typeof(object), typeof(DeviceTile), new PropertyMetadata(null));

    /// <summary>The button in the top right corner.</summary>
    public object? Corner { get => GetValue(CornerProperty); set => SetValue(CornerProperty, value); }
}
