using System.Security.Authentication;
using MQTTnet;
using MQTTnet.Formatter;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public static class MqttOptionsBuilder
{
    public static IReadOnlyList<string> Validate(BrokerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Profile name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            errors.Add("Hostname or IP address is required.");
        }
        else if (Uri.CheckHostName(profile.Host.Trim()) == UriHostNameType.Unknown)
        {
            errors.Add("Hostname contains invalid characters.");
        }

        if (profile.Port is < 1 or > 65535)
        {
            errors.Add("Port must be between 1 and 65535.");
        }

        if (profile.KeepAliveSeconds is < 0 or > 65535)
        {
            errors.Add("Keep-alive must be between 0 and 65535 seconds.");
        }

        if (profile.ConnectionTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Connection timeout must be between 1 and 300 seconds.");
        }

        return errors;
    }

    public static MqttClientOptions Build(BrokerProfile profile, string? plaintextPassword = null)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(profile));
        }

        var clientId = string.IsNullOrWhiteSpace(profile.ClientId)
            ? BrokerProfile.GenerateClientId()
            : profile.ClientId.Trim();

        var builder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithProtocolVersion(MapProtocolVersion(profile.ProtocolVersion))
            .WithCleanSession(profile.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(profile.KeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(profile.ConnectionTimeoutSeconds));

        var host = profile.Host.Trim();

        if (profile.IsWebSocket)
        {
            var path = BrokerProfile.NormalizeWebSocketPath(profile.WebSocketPath);
            var scheme = profile.IsSecure ? "wss" : "ws";
            var wsUri = $"{scheme}://{host}:{profile.Port}{path}";
            builder.WithWebSocketServer(ws => ws.WithUri(wsUri));
        }
        else
        {
            builder.WithTcpServer(host, profile.Port);
        }

        if (profile.IsSecure)
        {
            builder.WithTlsOptions(tls =>
            {
                tls.UseTls(true);
                tls.WithSslProtocols(MapSslProtocols(profile.TlsVersion));

                if (profile.AllowUntrustedCertificates)
                {
                    tls.WithAllowUntrustedCertificates(true);
                    tls.WithIgnoreCertificateChainErrors(true);
                    tls.WithIgnoreCertificateRevocationErrors(true);
                    tls.WithCertificateValidationHandler(_ => true);
                }
            });
        }
        else
        {
            builder.WithTlsOptions(tls => tls.UseTls(false));
        }

        if (!string.IsNullOrEmpty(profile.Username) || !string.IsNullOrEmpty(plaintextPassword))
        {
            builder.WithCredentials(profile.Username ?? string.Empty, plaintextPassword ?? string.Empty);
        }

        return builder.Build();
    }

    internal static MqttProtocolVersion MapProtocolVersion(MqttProtocolVersionOption version) =>
        version switch
        {
            MqttProtocolVersionOption.V500 => MqttProtocolVersion.V500,
            MqttProtocolVersionOption.V311 => MqttProtocolVersion.V311,
            MqttProtocolVersionOption.V310 => MqttProtocolVersion.V310,
            _ => MqttProtocolVersion.V500
        };

    internal static SslProtocols MapSslProtocols(TlsVersionOption tlsVersion) =>
        tlsVersion switch
        {
            TlsVersionOption.Tls12 => SslProtocols.Tls12,
            TlsVersionOption.Tls13 => SslProtocols.Tls13,
            _ => SslProtocols.None
        };
}
