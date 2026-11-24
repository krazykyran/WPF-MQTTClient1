using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public interface IMqttClientService : IAsyncDisposable, IDisposable
{
    ConnectionState State { get; }
    BrokerProfile? ActiveBroker { get; }

    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<MqttReceivedMessage>? MessageReceived;
    event EventHandler<string>? ConnectionError;

    Task ConnectAsync(BrokerProfile profile, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SubscribeAsync(string topicFilter, int qos, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default);
    Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        int qos,
        bool retain,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    Task<BrokerTestResult> TestConnectionAsync(
        BrokerProfile profile,
        string? plaintextPasswordOverride = null,
        CancellationToken cancellationToken = default);
}
