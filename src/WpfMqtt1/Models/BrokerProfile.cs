using System.Text.Json.Serialization;

namespace WpfMqtt1.Models;

public sealed class BrokerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Broker";
    public BrokerTransportType Transport { get; set; } = BrokerTransportType.Tcp;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string WebSocketPath { get; set; } = "/mqtt";

    public MqttProtocolVersionOption ProtocolVersion { get; set; } = MqttProtocolVersionOption.V500;
    public string ClientId { get; set; } = GenerateClientId();
    public bool CleanSession { get; set; } = true;
    public int KeepAliveSeconds { get; set; } = 60;
    public int ConnectionTimeoutSeconds { get; set; } = 10;
    public bool AutoReconnect { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted password (Base64). Plaintext passwords are never serialized to disk.
    /// </summary>
    public string? ProtectedPassword { get; set; }

    public TlsVersionOption TlsVersion { get; set; } = TlsVersionOption.SystemDefault;
    public bool AllowUntrustedCertificates { get; set; }

    public List<string> DefaultTopics { get; set; } = [];

    [JsonIgnore]
    public bool IsSecure => Transport is BrokerTransportType.Tls or BrokerTransportType.WebSocketSecure;

    [JsonIgnore]
    public bool IsWebSocket => Transport is BrokerTransportType.WebSocket or BrokerTransportType.WebSocketSecure;

    [JsonIgnore]
    public string Scheme => Transport switch
    {
        BrokerTransportType.Tcp => "mqtt",
        BrokerTransportType.Tls => "mqtts",
        BrokerTransportType.WebSocket => "ws",
        BrokerTransportType.WebSocketSecure => "wss",
        _ => "mqtt"
    };

    [JsonIgnore]
    public string DisplayUri
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(Host) ? "<host>" : Host.Trim();
            if (IsWebSocket)
            {
                var path = NormalizeWebSocketPath(WebSocketPath);
                return $"{Scheme}://{host}:{Port}{path}";
            }

            return $"{Scheme}://{host}:{Port}";
        }
    }

    [JsonIgnore]
    public string ProtocolDisplayName => ProtocolVersion switch
    {
        MqttProtocolVersionOption.V500 => "v5.0",
        MqttProtocolVersionOption.V311 => "v3.1.1",
        MqttProtocolVersionOption.V310 => "v3.1.0",
        _ => "v5.0"
    };

    public static int GetDefaultPort(BrokerTransportType transport) => transport switch
    {
        BrokerTransportType.Tcp => 1883,
        BrokerTransportType.Tls => 8883,
        BrokerTransportType.WebSocket => 80,
        BrokerTransportType.WebSocketSecure => 443,
        _ => 1883
    };

    public static bool IsStandardPort(int port) => port is 1883 or 8883 or 80 or 443;

    public static string NormalizeWebSocketPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/mqtt";
        }

        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    public static string GenerateClientId() => $"wpfmqtt-{Guid.NewGuid().ToString("N")[..10]}";

    public BrokerProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Transport = Transport,
        Host = Host,
        Port = Port,
        WebSocketPath = WebSocketPath,
        ProtocolVersion = ProtocolVersion,
        ClientId = ClientId,
        CleanSession = CleanSession,
        KeepAliveSeconds = KeepAliveSeconds,
        ConnectionTimeoutSeconds = ConnectionTimeoutSeconds,
        AutoReconnect = AutoReconnect,
        Username = Username,
        ProtectedPassword = ProtectedPassword,
        TlsVersion = TlsVersion,
        AllowUntrustedCertificates = AllowUntrustedCertificates,
        DefaultTopics = [.. DefaultTopics]
    };
}
