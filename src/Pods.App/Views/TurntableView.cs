using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pods.Core.Apple;
using Windows.Foundation;

namespace Pods.App.Views;

/// <summary>
/// Plays a turntable sprite sheet (<see cref="Turntable"/>) in a loop. The frame stepping is a
/// composition animation, so it runs on the compositor without app work per frame. Hidden when
/// there is no sheet or it cannot be decoded.
/// </summary>
internal sealed partial class TurntableView : UserControl
{
    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Turntables");

    /// <summary>Sheet names shipped with the app (file names without extension).</summary>
    public static IReadOnlySet<string> Available { get; } =
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*.webp").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet()
            : [];

    public static readonly DependencyProperty SheetProperty = DependencyProperty.Register(
        nameof(Sheet), typeof(string), typeof(TurntableView), new PropertyMetadata(null, (d, _) => ((TurntableView)d).Load()));

    readonly Canvas viewport = new();
    readonly Image image = new() { Stretch = Stretch.None };

    public TurntableView()
    {
        viewport.Children.Add(image);
        Content = viewport;
        Visibility = Visibility.Collapsed;
        ElementCompositionPreview.SetIsTranslationEnabled(image, true);
        image.ImageOpened += (_, _) => Play();
        image.ImageFailed += (_, _) => Visibility = Visibility.Collapsed;
        Unloaded += (_, _) => ElementCompositionPreview.GetElementVisual(image).StopAnimation("Translation");
    }

    /// <summary>Sheet name from <see cref="Turntable.Resolve"/>, or null for none.</summary>
    public string? Sheet
    {
        get => (string?)GetValue(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    void Load()
    {
        if (Sheet is not { } name)
        {
            image.Source = null;
            Visibility = Visibility.Collapsed;
            return;
        }
        image.Source = new BitmapImage(new Uri(Path.Combine(Folder, name + ".webp")));
    }

    void Play()
    {
        if (image.Source is not BitmapImage { PixelWidth: > 0 } bitmap) return;
        float frameWidth = bitmap.PixelWidth / Turntable.Columns;
        float frameHeight = bitmap.PixelHeight / Turntable.Rows;
        viewport.Width = frameWidth;
        viewport.Height = frameHeight;
        viewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, frameWidth, frameHeight) };

        var visual = ElementCompositionPreview.GetElementVisual(image);
        var compositor = visual.Compositor;
        var step = compositor.CreateStepEasingFunction();
        var animation = compositor.CreateVector3KeyFrameAnimation();
        for (var i = 0; i < Turntable.FrameCount; i++)
        {
            var offset = new Vector3(-(i % Turntable.Columns) * frameWidth, -(i / Turntable.Columns) * frameHeight, 0);
            if (i == 0) animation.InsertKeyFrame(0f, offset);
            else animation.InsertKeyFrame((float)i / Turntable.FrameCount, offset, step);
        }
        animation.Duration = Turntable.TurnDuration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", animation);
        Visibility = Visibility.Visible;
    }
}
