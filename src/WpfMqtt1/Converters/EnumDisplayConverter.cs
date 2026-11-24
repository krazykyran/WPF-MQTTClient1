using System.Globalization;
using System.Windows.Data;
using WpfMqtt1.Models;

namespace WpfMqtt1.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            BrokerTransportType.Tcp => "TCP (mqtt://)",
            BrokerTransportType.Tls => "TLS / SSL (mqtts://)",
            BrokerTransportType.WebSocket => "WebSocket (ws://)",
            BrokerTransportType.WebSocketSecure => "WebSocket Secure (wss://)",

            MqttProtocolVersionOption.V500 => "MQTT v5.0",
            MqttProtocolVersionOption.V311 => "MQTT v3.1.1",
            MqttProtocolVersionOption.V310 => "MQTT v3.1.0",

            TlsVersionOption.SystemDefault => "System Default (OS negotiated)",
            TlsVersionOption.Tls12 => "TLS 1.2",
            TlsVersionOption.Tls13 => "TLS 1.3",

            AppThemePreference.System => "Follow Windows System",
            AppThemePreference.Light => "Light",
            AppThemePreference.Dark => "Dark",

            WindowBackdropPreference.Mica => "Mica (Windows 11)",
            WindowBackdropPreference.Acrylic => "Acrylic",
            WindowBackdropPreference.None => "Solid (None)",

            PayloadFormat.Text => "Text (UTF-8)",
            PayloadFormat.Json => "JSON",
            PayloadFormat.Hex => "Hex",
            PayloadFormat.Base64 => "Base64",

            MessageDirectionFilter.All => "All Directions",
            MessageDirectionFilter.Inbound => "Inbound (↓)",
            MessageDirectionFilter.Outbound => "Outbound (↑)",

            int qos when parameter is string p && p.Equals("QoS", StringComparison.OrdinalIgnoreCase) => qos switch
            {
                0 => "QoS 0 — At most once",
                1 => "QoS 1 — At least once",
                2 => "QoS 2 — Exactly once",
                _ => $"QoS {qos}"
            },

            _ => value?.ToString() ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
