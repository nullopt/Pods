namespace Pods.App.Tray;

/// <summary>The hover card: shown without taking focus while the pointer rests on the tray icon.</summary>
internal sealed partial class TrayCardWindow : TrayAnchoredWindow
{
    readonly TrayCardView view;

    public TrayCardWindow(TrayCardViewModel card) : this(new TrayCardView(card)) { }

    TrayCardWindow(TrayCardView view) : base(view) => this.view = view;

    public void ShowAbove(int left, int top, int right, int bottom) => ShowAt(left, top, right, bottom, activate: false);

    protected override void OnClosed() => view.Detach();
}
