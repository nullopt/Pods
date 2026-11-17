using Microsoft.UI.Xaml.Data;

namespace Pods.App.Views;

/// <summary>Shows a slider's value as a whole percentage ("25%") in its thumb tooltip.</summary>
internal sealed partial class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double d ? $"{Math.Round(d):0}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
