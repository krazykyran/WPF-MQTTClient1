using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WpfMqtt1.Models;

namespace WpfMqtt1.Converters;

public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DisconnectedBrush = CreateFrozenBrush(0x8A, 0x8A, 0x8A);
    private static readonly SolidColorBrush ConnectingBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush ConnectedBrush = CreateFrozenBrush(0x22, 0xC5, 0x5E);
    private static readonly SolidColorBrush ReconnectingBrush = CreateFrozenBrush(0xF9, 0x73, 0x16);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ConnectionState state
            ? state switch
            {
                ConnectionState.Connected => ConnectedBrush,
                ConnectionState.Connecting => ConnectingBrush,
                ConnectionState.Reconnecting => ReconnectingBrush,
                _ => DisconnectedBrush
            }
            : DisconnectedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
