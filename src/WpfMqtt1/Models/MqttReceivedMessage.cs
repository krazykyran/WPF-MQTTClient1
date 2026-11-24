namespace WpfMqtt1.Models;

public sealed record MqttReceivedMessage(
    DateTimeOffset TimestampUtc,
    string Topic,
    byte[] Payload,
    int Qos,
    bool Retain,
    string? ContentType);

public sealed record BrokerTestResult(
    bool Success,
    TimeSpan Elapsed,
    string Message);
