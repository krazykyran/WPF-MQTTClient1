using System.Globalization;
using System.Windows.Data;

namespace WpfMqtt1.Converters;

public sealed class DoubleToIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (double?)i : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            return (int)Math.Round(d);
        }

        if (parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fallback))
        {
            return fallback;
        }

        return 0;
    }
}
