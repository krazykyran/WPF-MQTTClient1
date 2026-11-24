using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WpfMqtt1.Models;

namespace WpfMqtt1.Converters;

public sealed class DirectionToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InboundFg = Freeze(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush OutboundFg = Freeze(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush InboundBg = Freeze(Color.FromArgb(0x28, 0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush OutboundBg = Freeze(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBg = parameter is string p && p.Equals("Bg", StringComparison.OrdinalIgnoreCase);
        var isInbound = value is MessageDirection dir && dir == MessageDirection.Inbound;

        if (isBg)
        {
            return isInbound ? InboundBg : OutboundBg;
        }

        return isInbound ? InboundFg : OutboundFg;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
