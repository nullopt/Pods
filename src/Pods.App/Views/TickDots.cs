using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Pods.App.Views;

/// <summary>
/// A row of small dots under a slider, one per step, spread between <see cref="Inset"/> from each
/// end so they line up with where the thumb's centre stops.
/// </summary>
internal sealed partial class TickDots : Canvas
{
    const double Dot = 3;

    public TickDots()
    {
        Height = Dot;
        SizeChanged += (_, _) => Layout();
    }

    public int Count { get; set; } = 2;

    /// <summary>Distance from each end to the first and last dot's centre: half the thumb's width.</summary>
    public double Inset { get; set; }

    /// <summary>A dependency property so XAML can give it a theme brush.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(TickDots), new PropertyMetadata(null, (d, _) => ((TickDots)d).Recolour()));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    void Recolour()
    {
        foreach (var dot in Children.OfType<Ellipse>()) dot.Fill = Fill;
    }

    void Layout()
    {
        if (Children.Count != Count)
        {
            Children.Clear();
            for (var i = 0; i < Count; i++) Children.Add(new Ellipse { Width = Dot, Height = Dot, Fill = Fill });
        }
        var span = ActualWidth - 2 * Inset;
        for (var i = 0; i < Count; i++)
            SetLeft(Children[i], Inset + span * i / Math.Max(1, Count - 1) - Dot / 2);
    }
}
