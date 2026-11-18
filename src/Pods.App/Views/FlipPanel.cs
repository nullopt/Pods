using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Pods.App.Views;

/// <summary>
/// Two faces in one place; setting <see cref="IsFlipped"/> turns the panel about its vertical axis
/// to show the back (a short 3D turn, or at once while animations are off). Both faces stay laid out and the panel takes the front's
/// height (the wider face's width), so it does not jump when it turns; a taller back gets only that
/// height and should scroll. Each face sits in a host that, while hidden, is
/// transparent, disabled (out of the Tab order) and hidden from screen readers.
/// </summary>
internal sealed partial class FlipPanel : Grid
{
    static readonly TimeSpan HalfTurn = TimeSpan.FromMilliseconds(180);
    // One curve over the whole turn: the first half speeds up, the second slows down, so it never pauses edge-on.
    static readonly (Vector2, Vector2) SpeedUp = (new(0.4f, 0), new(1, 1));
    static readonly (Vector2, Vector2) SlowDown = (new(0, 0), new(0.2f, 1));
    const float Perspective = 600;

    public static readonly DependencyProperty FrontProperty = DependencyProperty.Register(
        nameof(Front), typeof(UIElement), typeof(FlipPanel), new PropertyMetadata(null, (d, _) => ((FlipPanel)d).SetFaces()));

    public static readonly DependencyProperty BackProperty = DependencyProperty.Register(
        nameof(Back), typeof(UIElement), typeof(FlipPanel), new PropertyMetadata(null, (d, _) => ((FlipPanel)d).SetFaces()));

    public static readonly DependencyProperty IsFlippedProperty = DependencyProperty.Register(
        nameof(IsFlipped), typeof(bool), typeof(FlipPanel), new PropertyMetadata(false, (d, _) => ((FlipPanel)d).Turn()));

    readonly ContentControl frontHost = Host(), backHost = Host();
    bool showingBack;
    int turn;   // bumped by every change, so a stale half-turn's completion does nothing

    public FlipPanel()
    {
        // A loaded panel turns; one that is not loaded yet (or is reused for another item) just shows the right face.
        Loaded += (_, _) => Snap();
        Children.Add(frontHost);
        Children.Add(backHost);
    }

    static ContentControl Host() => new()
    {
        IsTabStop = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        frontHost.Measure(availableSize);
        var front = frontHost.DesiredSize;
        backHost.Measure(new Windows.Foundation.Size(availableSize.Width, front.Height));
        return new Windows.Foundation.Size(Math.Max(front.Width, backHost.DesiredSize.Width), front.Height);
    }

    public UIElement? Front { get => (UIElement?)GetValue(FrontProperty); set => SetValue(FrontProperty, value); }
    public UIElement? Back { get => (UIElement?)GetValue(BackProperty); set => SetValue(BackProperty, value); }
    public bool IsFlipped { get => (bool)GetValue(IsFlippedProperty); set => SetValue(IsFlippedProperty, value); }

    void SetFaces()
    {
        frontHost.Content = Front;
        backHost.Content = Back;
        Snap();
    }

    /// <summary>Shows the face for <see cref="IsFlipped"/> at once, cancelling any turn.</summary>
    void Snap()
    {
        turn++;
        showingBack = IsFlipped;
        foreach (var host in (UIElement[])[frontHost, backHost])
        {
            Visual(host).StopAnimation(nameof(Microsoft.UI.Composition.Visual.RotationAngleInDegrees));
            Visual(host).RotationAngleInDegrees = 0;
        }
        // Perspective only while turning: left on, it would also distort turns of panels nested inside.
        Visual(this).TransformMatrix = Matrix4x4.Identity;
        Show(showingBack);
    }

    void Turn()
    {
        if (!IsLoaded || Front is null || Back is null || ActualWidth == 0 || !App.AnimationsOn)
        {
            Snap();
            return;
        }
        // A change mid-turn starts again from the face currently up, so the end always matches IsFlipped.
        var from = showingBack;
        Snap();
        showingBack = from;
        Show(from);
        if (from == IsFlipped) return;

        var current = turn;
        var outgoing = from ? backHost : frontHost;
        var incoming = from ? frontHost : backHost;
        SetPerspective();
        Rotate(outgoing, 0, 90, SpeedUp, () =>
        {
            if (current != turn) return;
            var hadFocus = ContainsFocus(outgoing);
            showingBack = !from;
            Show(showingBack);
            Visual(outgoing).RotationAngleInDegrees = 0;
            // Focus on the face that turned away would vanish: follow it to the new face.
            if (hadFocus && FocusManager.FindFirstFocusableElement(incoming) is Control first) first.Focus(FocusState.Programmatic);
            Rotate(incoming, -90, 0, SlowDown, () =>
            {
                if (current == turn) Visual(this).TransformMatrix = Matrix4x4.Identity;
            });
        });
    }

    void Rotate(UIElement face, float from, float to, (Vector2 A, Vector2 B) easing, Action completed)
    {
        var visual = Visual(face);
        var size = face.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
        visual.RotationAxis = Vector3.UnitY;
        visual.RotationAngleInDegrees = from;
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, to, compositor.CreateCubicBezierEasingFunction(easing.A, easing.B));
        animation.Duration = HalfTurn;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Microsoft.UI.Composition.Visual.RotationAngleInDegrees), animation);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() => completed());
    }

    /// <summary>Perspective about the panel's centre, so the near edge grows as it turns.</summary>
    void SetPerspective()
    {
        var centre = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        var perspective = Matrix4x4.Identity;
        perspective.M34 = -1 / Perspective;
        Visual(this).TransformMatrix = Matrix4x4.CreateTranslation(-centre) * perspective * Matrix4x4.CreateTranslation(centre);
    }

    void Show(bool back)
    {
        SetShown(frontHost, !back);
        SetShown(backHost, back);
    }

    static void SetShown(ContentControl host, bool shown)
    {
        host.Opacity = shown ? 1 : 0;
        host.IsHitTestVisible = shown;
        host.IsEnabled = shown;
        AutomationProperties.SetAccessibilityView(host, shown ? AccessibilityView.Content : AccessibilityView.Raw);
    }

    bool ContainsFocus(UIElement host)
    {
        if (XamlRoot is null) return false;
        for (var e = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; e is not null; e = VisualTreeHelper.GetParent(e))
            if (e == host) return true;
        return false;
    }

    static Visual Visual(UIElement element) => ElementCompositionPreview.GetElementVisual(element);
}
