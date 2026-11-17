using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Pods.Core.Apple;

namespace Pods.App.Views;

/// <summary>Which part of a turntable frame to show: all of it, the earbuds (left) or the case (right).</summary>
internal enum TurntablePart { Whole, Left, Right }

/// <summary>
/// Shows device artwork from a turntable sprite sheet (<see cref="Turntable"/>): the whole frame or
/// one part, spinning or still on the front view, at any height. Each sheet is decoded once and
/// shared by every view of it (a decoded sheet is about 17 MB). Spinning is a composition animation,
/// so it needs no app work per frame. Hidden when there is nothing to show.
/// </summary>
internal sealed partial class TurntableView : UserControl
{
    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Turntables");
    static readonly Dictionary<string, WeakReference<LoadedImageSurface>> Surfaces = [];

    /// <summary>Sheet names shipped with the app (file names without extension).</summary>
    public static IReadOnlySet<string> Available { get; } =
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*.webp").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet()
            : [];

    public static readonly DependencyProperty SheetProperty = Register(nameof(Sheet), typeof(string), null);
    public static readonly DependencyProperty PartProperty = Register(nameof(Part), typeof(TurntablePart), TurntablePart.Whole);
    public static readonly DependencyProperty IsSpinningProperty = Register(nameof(IsSpinning), typeof(bool), false);
    public static readonly DependencyProperty FrameHeightProperty = Register(nameof(FrameHeight), typeof(double), double.NaN);

    SpriteVisual? sprite;
    CompositionSurfaceBrush? brush;
    LoadedImageSurface? surface;
    bool spinning;

    public TurntableView()
    {
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => StopSpinning();
    }

    /// <summary>Sheet name from <see cref="Turntable.Resolve"/>, or null for none.</summary>
    public string? Sheet { get => (string?)GetValue(SheetProperty); set => SetValue(SheetProperty, value); }

    public TurntablePart Part { get => (TurntablePart)GetValue(PartProperty); set => SetValue(PartProperty, value); }

    /// <summary>Spin the turntable; otherwise show the still front view (frame 0).</summary>
    public bool IsSpinning { get => (bool)GetValue(IsSpinningProperty); set => SetValue(IsSpinningProperty, value); }

    /// <summary>Displayed frame height in DIPs; NaN shows the sheet at its own size. The width follows the part.</summary>
    public double FrameHeight { get => (double)GetValue(FrameHeightProperty); set => SetValue(FrameHeightProperty, value); }

    static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(TurntableView),
            new PropertyMetadata(defaultValue, (d, _) => ((TurntableView)d).Update()));

    void Update()
    {
        if (!IsLoaded) return;
        var sheetSurface = SurfaceFor(Sheet);
        if (!ReferenceEquals(sheetSurface, surface))
        {
            if (surface is not null) surface.LoadCompleted -= OnLoadCompleted;
            surface = sheetSurface;
            if (surface is not null) surface.LoadCompleted += OnLoadCompleted;
            StopSpinning();
        }
        if (surface is null || surface.NaturalSize.Width <= 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var frameWidth = (float)surface.NaturalSize.Width / Turntable.Columns;
        var frameHeight = (float)surface.NaturalSize.Height / Turntable.Rows;
        var cut = Turntable.Cut(Sheet!);
        if (Part != TurntablePart.Whole && cut is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        var partX = Part == TurntablePart.Right ? cut!.Value : 0f;
        var partWidth = Part switch
        {
            TurntablePart.Left => cut!.Value,
            TurntablePart.Right => frameWidth - cut!.Value,
            _ => frameWidth,
        };
        var scale = double.IsNaN(FrameHeight) ? 1f : (float)(FrameHeight / frameHeight);

        EnsureVisual();
        brush!.Surface = surface;
        brush.Scale = new Vector2(scale);
        sprite!.Size = new Vector2(partWidth * scale, frameHeight * scale);
        Width = sprite.Size.X;
        Height = sprite.Size.Y;

        Vector2 Offset(int frame) => new(
            -((frame % Turntable.Columns) * frameWidth + partX) * scale,
            -(frame / Turntable.Columns) * frameHeight * scale);

        StopSpinning();
        brush.Offset = Offset(0);
        if (IsSpinning)
        {
            var compositor = brush.Compositor;
            var step = compositor.CreateStepEasingFunction();
            var animation = compositor.CreateVector2KeyFrameAnimation();
            animation.InsertKeyFrame(0f, Offset(0));
            for (var i = 1; i < Turntable.FrameCount; i++)
                animation.InsertKeyFrame((float)i / Turntable.FrameCount, Offset(i), step);
            animation.Duration = Turntable.TurnDuration;
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            brush.StartAnimation(nameof(CompositionSurfaceBrush.Offset), animation);
            spinning = true;
        }
        Visibility = Visibility.Visible;
    }

    void OnLoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
    {
        if (args.Status == LoadedImageSourceLoadStatus.Success) Update();
        else Visibility = Visibility.Collapsed;
    }

    void EnsureVisual()
    {
        if (sprite is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        brush = compositor.CreateSurfaceBrush();
        brush.Stretch = CompositionStretch.None;
        brush.HorizontalAlignmentRatio = 0;
        brush.VerticalAlignmentRatio = 0;
        sprite = compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        ElementCompositionPreview.SetElementChildVisual(this, sprite);
    }

    void StopSpinning()
    {
        if (!spinning || brush is null) return;
        brush.StopAnimation(nameof(CompositionSurfaceBrush.Offset));
        spinning = false;
    }

    /// <summary>The shared decoded surface for a sheet; starts decoding on first use.</summary>
    static LoadedImageSurface? SurfaceFor(string? sheet)
    {
        if (sheet is null || !Available.Contains(sheet)) return null;
        if (Surfaces.TryGetValue(sheet, out var weak) && weak.TryGetTarget(out var cached)) return cached;
        var created = LoadedImageSurface.StartLoadFromUri(new Uri(Path.Combine(Folder, sheet + ".webp")));
        Surfaces[sheet] = new WeakReference<LoadedImageSurface>(created);
        return created;
    }
}
