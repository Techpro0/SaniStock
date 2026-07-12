using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SaniStock.App.Infrastructure;

/// <summary>Paints a numeric value red when it is negative (shortfall highlighting).</summary>
public sealed class NegativeToRedConverter : IValueConverter
{
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush Normal = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value switch
        {
            decimal m => m,
            double db => (decimal)db,
            int i => i,
            _ => 0m
        };
        return d < 0 ? Red : Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows "Active"/"Inactive" (or with a parameter, custom pair) from a bool.</summary>
public sealed class BoolToActiveTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Active" : "Inactive";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Collapses an element when the bound string is null/empty.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
