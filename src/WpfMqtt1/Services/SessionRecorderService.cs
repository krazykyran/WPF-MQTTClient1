using System.Threading.Channels;
using WpfMqtt1.Data;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class SessionRecorderService : ISessionRecorderService
{
    private const int MaxBatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly ISessionRepository _repository;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private Channel<MqttLogMessage>? _channel;
    private Task? _writerTask;
    private RecordingSession? _activeSession;
    private long _recordedCount;
    private int _disposed;

    public bool IsRecording => _activeSession is not null;
    public RecordingSession? ActiveSession => _activeSession;
    public long RecordedMessageCount => Interlocked.Read(ref _recordedCount);

    public event EventHandler? RecordingStateChanged;
    public event EventHandler<long>? RecordedCountChanged;
    public event EventHandler? SessionsUpdated;

    public SessionRecorderService(ISessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<RecordingSession> StartRecordingAsync(
        BrokerProfile broker,
        string? sessionName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession is not null)
            {
                await StopRecordingLockedAsync(cancellationToken).ConfigureAwait(false);
            }

            var startedAt = DateTimeOffset.UtcNow;
            var name = string.IsNullOrWhiteSpace(sessionName)
                ? $"{broker.Name} — {startedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
                : sessionName.Trim();

            var session = await _repository.CreateSessionAsync(
                name,
                broker.Name,
                broker.DisplayUri,
                broker.ProtocolDisplayName,
                startedAt,
                cancellationToken).ConfigureAwait(false);

            var channel = Channel.CreateUnbounded<MqttLogMessage>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

            _channel = channel;
            _activeSession = session;
            Interlocked.Exchange(ref _recordedCount, 0);
            _writerTask = Task.Run(() => RunWriterLoopAsync(session.Id, channel));
        }
        finally
        {
            _lifecycleLock.Release();
        }

        RecordingStateChanged?.Invoke(this, EventArgs.Empty);
        RecordedCountChanged?.Invoke(this, 0);
        SessionsUpdated?.Invoke(this, EventArgs.Empty);

        return _activeSession!;
    }

    public void EnqueueMessage(MqttLogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _channel?.Writer.TryWrite(message);
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopRecordingLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StopRecordingLockedAsync(CancellationToken cancellationToken)
    {
        var session = _activeSession;
        var channel = _channel;
        var writerTask = _writerTask;

        if (session is null)
        {
            return;
        }

        _activeSession = null;
        _channel = null;
        _writerTask = null;

        channel?.Writer.TryComplete();

        if (writerTask is not null)
        {
            try
            {
                await writerTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort flush on stop/shutdown.
            }
        }

        var endedAt = DateTimeOffset.UtcNow;
        session.EndedAtUtc = endedAt;
        await _repository.EndSessionAsync(session.Id, endedAt, cancellationToken).ConfigureAwait(false);

        RecordingStateChanged?.Invoke(this, EventArgs.Empty);
        SessionsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunWriterLoopAsync(long sessionId, Channel<MqttLogMessage> channel)
    {
        var batch = new List<MqttLogMessage>(MaxBatchSize);
        var reader = channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                var deadline = DateTime.UtcNow + FlushInterval;

                while (batch.Count < MaxBatchSize)
                {
                    if (reader.TryRead(out var msg))
                    {
                        batch.Add(msg);
                        continue;
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || reader.Completion.IsCompleted)
                    {
                        break;
                    }

                    using var delayCts = new CancellationTokenSource(remaining);
                    try
                    {
                        if (!await reader.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    await _repository.AppendMessagesBatchAsync(sessionId, batch).ConfigureAwait(false);
                    var total = Interlocked.Add(ref _recordedCount, batch.Count);
                    if (_activeSession is { } current && current.Id == sessionId)
                    {
                        current.MessageCount = total;
                    }

                    RecordedCountChanged?.Invoke(this, total);
                }
            }
        }
        catch
        {
            // Keep background writer resilient against transient disk errors.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Swallow disposal errors.
        }

        _lifecycleLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopRecordingAsync().ConfigureAwait(false);
        }
        catch
        {
            // Swallow disposal errors.
        }

        _lifecycleLock.Dispose();
    }
}
