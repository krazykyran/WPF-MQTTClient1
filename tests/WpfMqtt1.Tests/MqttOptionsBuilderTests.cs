using System.Security.Authentication;
using MQTTnet;
using MQTTnet.Formatter;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class MqttOptionsBuilderTests
{
    [Fact]
    public void Build_TcpProfile_ConfiguresEndpointAndProtocol()
    {
        var profile = new BrokerProfile
        {
            Name = "Local",
            Host = "127.0.0.1",
            Port = 1883,
            Transport = BrokerTransportType.Tcp,
            ProtocolVersion = MqttProtocolVersionOption.V311,
            ClientId = "test-client-1",
            CleanSession = false,
            KeepAliveSeconds = 45,
            ConnectionTimeoutSeconds = 15,
            Username = "alice"
        };

        var options = MqttOptionsBuilder.Build(profile, "secret123");

        Assert.Equal("test-client-1", options.ClientId);
        Assert.Equal(MqttProtocolVersion.V311, options.ProtocolVersion);
        Assert.False(options.CleanSession);
        Assert.Equal(TimeSpan.FromSeconds(45), options.KeepAlivePeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Timeout);

        var tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
        Assert.Equal("127.0.0.1", tcp.RemoteEndpoint is System.Net.DnsEndPoint dns ? dns.Host : null);
        Assert.Equal(1883, tcp.RemoteEndpoint is System.Net.DnsEndPoint dns2 ? dns2.Port : 0);
        Assert.False(tcp.TlsOptions.UseTls);
        Assert.Equal("alice", options.Credentials?.GetUserName(options));
    }

    [Fact]
    public void Build_TlsProfileWithSelfSignedBypass_ConfiguresTlsAndValidationHandler()
    {
        var profile = new BrokerProfile
        {
            Name = "Dev TLS",
            Host = "broker.local",
            Port = 8883,
            Transport = BrokerTransportType.Tls,
            ProtocolVersion = MqttProtocolVersionOption.V500,
            TlsVersion = TlsVersionOption.Tls13,
            AllowUntrustedCertificates = true
        };

        var options = MqttOptionsBuilder.Build(profile);

        Assert.Equal(MqttProtocolVersion.V500, options.ProtocolVersion);
        var tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
        Assert.True(tcp.TlsOptions.UseTls);
        Assert.Equal(SslProtocols.Tls13, tcp.TlsOptions.SslProtocol);
        Assert.True(tcp.TlsOptions.AllowUntrustedCertificates);
        Assert.True(tcp.TlsOptions.IgnoreCertificateChainErrors);
        Assert.NotNull(tcp.TlsOptions.CertificateValidationHandler);
    }

    [Theory]
    [InlineData(BrokerTransportType.WebSocket, "ws://broker.example.com:8083/mqtt", false)]
    [InlineData(BrokerTransportType.WebSocketSecure, "wss://broker.example.com:8083/mqtt", true)]
    public void Build_WebSocketProfiles_ConfiguresUriAndTls(
        BrokerTransportType transport,
        string expectedUri,
        bool expectedUseTls)
    {
        var profile = new BrokerProfile
        {
            Name = "WS Broker",
            Host = "broker.example.com",
            Port = 8083,
            WebSocketPath = "mqtt",
            Transport = transport,
            ProtocolVersion = MqttProtocolVersionOption.V310
        };

        var options = MqttOptionsBuilder.Build(profile);

        Assert.Equal(MqttProtocolVersion.V310, options.ProtocolVersion);
        var ws = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);
        Assert.Equal(expectedUri, ws.Uri);
        Assert.Equal(expectedUseTls, ws.TlsOptions.UseTls);
    }

    [Fact]
    public void Validate_InvalidProfile_ReturnsDescriptiveErrors()
    {
        var invalid = new BrokerProfile
        {
            Name = "   ",
            Host = "",
            Port = 70000,
            KeepAliveSeconds = -1,
            ConnectionTimeoutSeconds = 0
        };

        var errors = MqttOptionsBuilder.Validate(invalid);

        Assert.True(errors.Count >= 4);
        Assert.Throws<ArgumentException>(() => MqttOptionsBuilder.Build(invalid));
    }
}
