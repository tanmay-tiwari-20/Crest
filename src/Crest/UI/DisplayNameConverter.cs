using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Crest.UI;

public sealed class DisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string name ? Label(name) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;

    internal static string Label(string deviceName)
    {
        const string displayPrefix = @"\\.\DISPLAY";
        return deviceName.StartsWith(displayPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(deviceName.AsSpan(displayPrefix.Length), out int number)
            ? "Display " + number
            : deviceName;
    }
}
