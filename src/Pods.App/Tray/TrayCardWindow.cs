namespace Pods.App.Tray;

/// <summary>The hover card: shown without taking focus while the pointer rests on the tray icon.</summary>
internal sealed partial class TrayCardWindow : TrayAnchoredWindow
{
    public TrayCardWindow(TrayCardViewModel card) : this(new TrayCardView(card)) { }

    TrayCardWindow(TrayCardView view) : base(view) => Closed += (_, _) => view.Detach();

    public void ShowAbove(int left, int top, int right, int bottom) => ShowAt(left, top, right, bottom, activate: false);
}
