using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public interface ISessionRecorderService : IAsyncDisposable, IDisposable
{
    bool IsRecording { get; }
    RecordingSession? ActiveSession { get; }
    long RecordedMessageCount { get; }

    event EventHandler? RecordingStateChanged;
    event EventHandler<long>? RecordedCountChanged;
    event EventHandler? SessionsUpdated;

    Task<RecordingSession> StartRecordingAsync(
        BrokerProfile broker,
        string? sessionName = null,
        CancellationToken cancellationToken = default);

    void EnqueueMessage(MqttLogMessage message);

    Task StopRecordingAsync(CancellationToken cancellationToken = default);
}
