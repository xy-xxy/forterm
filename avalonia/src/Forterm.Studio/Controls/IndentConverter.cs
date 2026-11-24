using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Forterm.Studio.Controls;

/// <summary>Tree level → left margin for Studio's compact tree rows.</summary>
public sealed class IndentConverter : IValueConverter
{
    public double Step { get; set; } = 14;
    public double Start { get; set; } = 4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(Start + (value is int level ? Math.Max(0, level) : 0) * Step, 0, 6, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
