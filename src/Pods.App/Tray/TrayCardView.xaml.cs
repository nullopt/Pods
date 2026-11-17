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
}
