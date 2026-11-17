using Microsoft.UI.Xaml.Controls;

namespace Pods.App.Tray;

/// <summary>The tray hover card. Its rings exist only while the card is shown.</summary>
internal sealed partial class TrayCardView : UserControl
{
    public TrayCardView(TrayCardViewModel card)
    {
        Card = card;
        InitializeComponent();
    }

    public TrayCardViewModel Card { get; }

    /// <summary>Lets go of the long-lived card view model once the window closes, so closed cards are not kept alive.</summary>
    public void Detach()
    {
        Bindings.StopTracking();
        RingList.ItemsSource = null;
    }
}
