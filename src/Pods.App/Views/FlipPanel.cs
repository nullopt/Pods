using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Pods.App.Views;

/// <summary>
/// Two faces in one place; setting <see cref="IsFlipped"/> turns the panel about its vertical axis
/// to show the back (a short 3D turn). Both faces stay laid out, so the panel keeps the size of the
/// larger one and does not jump when it turns; the hidden face is transparent and ignores input.
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

    bool showingBack;
    int turn;   // bumped by every change, so a stale half-turn's completion does nothing

    public FlipPanel()
    {
        // A loaded panel turns; one that is not loaded yet (or is reused for another item) just shows the right face.
        Loaded += (_, _) => Snap();
    }

    public UIElement? Front { get => (UIElement?)GetValue(FrontProperty); set => SetValue(FrontProperty, value); }
    public UIElement? Back { get => (UIElement?)GetValue(BackProperty); set => SetValue(BackProperty, value); }
    public bool IsFlipped { get => (bool)GetValue(IsFlippedProperty); set => SetValue(IsFlippedProperty, value); }

    void SetFaces()
    {
        Children.Clear();
        if (Front is { } front) Children.Add(front);
        if (Back is { } back) Children.Add(back);
        Snap();
    }

    /// <summary>Shows the face for <see cref="IsFlipped"/> at once, cancelling any turn.</summary>
    void Snap()
    {
        turn++;
        showingBack = IsFlipped;
        foreach (var face in Faces()) Visual(face).StopAnimation(nameof(Microsoft.UI.Composition.Visual.RotationAngleInDegrees));
        foreach (var face in Faces()) Visual(face).RotationAngleInDegrees = 0;
        Show(showingBack);
    }

    void Turn()
    {
        if (!IsLoaded || Front is null || Back is null || ActualWidth == 0)
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
        var outgoing = from ? Back : Front;
        var incoming = from ? Front : Back;
        SetPerspective();
        Rotate(outgoing, 0, 90, SpeedUp, () =>
        {
            if (current != turn) return;
            showingBack = !from;
            Show(showingBack);
            Visual(outgoing).RotationAngleInDegrees = 0;
            Rotate(incoming, -90, 0, SlowDown, () => { });
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
        if (Front is { } front)
        {
            front.Opacity = back ? 0 : 1;
            front.IsHitTestVisible = !back;
        }
        if (Back is { } b)
        {
            b.Opacity = back ? 1 : 0;
            b.IsHitTestVisible = back;
        }
    }

    IEnumerable<UIElement> Faces()
    {
        if (Front is { } front) yield return front;
        if (Back is { } back) yield return back;
    }

    static Visual Visual(UIElement element) => ElementCompositionPreview.GetElementVisual(element);
}
