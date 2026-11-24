using WpfMqtt1.Models;

namespace WpfMqtt1.Data;

public interface ISessionRepository
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);

    Task<RecordingSession> CreateSessionAsync(
        string name,
        string brokerName,
        string brokerAddress,
        string mqttVersion,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task AppendMessagesBatchAsync(
        long sessionId,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default);

    Task EndSessionAsync(
        long sessionId,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default);

    Task RenameSessionAsync(
        long sessionId,
        string newName,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordingSession>> GetSessionsAsync(
        string? searchQuery = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MqttLogMessage>> GetSessionMessagesAsync(
        long sessionId,
        string? searchQuery = null,
        MessageDirectionFilter direction = MessageDirectionFilter.All,
        string? topicFilter = null,
        int limit = 10_000,
        int offset = 0,
        SessionMessageSortColumn sortBy = SessionMessageSortColumn.Timestamp,
        bool sortDescending = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDistinctTopicsForSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default);

    Task CompactDatabaseAsync(CancellationToken cancellationToken = default);
}
