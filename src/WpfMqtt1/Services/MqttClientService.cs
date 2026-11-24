using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using MQTTnet;
using MQTTnet.Protocol;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class MqttClientService : IMqttClientService
{
    private readonly ISecretProtectorService _secretProtector;
    private readonly MqttClientFactory _factory = new();
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _activeSubscriptions = new(StringComparer.Ordinal);

    private CancellationTokenSource? _reconnectCts;
    private volatile bool _userRequestedDisconnect;
    private int _disposed;
    private ConnectionState _state = ConnectionState.Disconnected;

    public ConnectionState State => _state;
    public BrokerProfile? ActiveBroker { get; private set; }

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<MqttReceivedMessage>? MessageReceived;
    public event EventHandler<string>? ConnectionError;

    public MqttClientService(ISecretProtectorService secretProtector)
    {
        _secretProtector = secretProtector;
        _client = _factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public async Task ConnectAsync(BrokerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelReconnectLoop();
            _userRequestedDisconnect = false;

            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder()
                        .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                        .Build(),
                    cancellationToken).ConfigureAwait(false);
            }

            _activeSubscriptions.Clear();
            ActiveBroker = profile.Clone();
            SetState(ConnectionState.Connecting);

            var password = _secretProtector.Unprotect(profile.ProtectedPassword);
            var options = MqttOptionsBuilder.Build(profile, password);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.ConnectionTimeoutSeconds, 1, 300)));

            var result = await _client.ConnectAsync(options, timeoutCts.Token).ConfigureAwait(false);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                SetState(ConnectionState.Disconnected);
                var message = string.IsNullOrWhiteSpace(result.ReasonString)
                    ? $"Broker rejected connection ({result.ResultCode})."
                    : $"Broker rejected connection ({result.ResultCode}): {result.ReasonString}";
                ConnectionError?.Invoke(this, message);
                throw new InvalidOperationException(message);
            }

            SetState(ConnectionState.Connected);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetState(ConnectionState.Disconnected);
            ConnectionError?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _userRequestedDisconnect = true;
            CancelReconnectLoop();

            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder()
                        .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                        .Build(),
                    cancellationToken).ConfigureAwait(false);
            }

            _activeSubscriptions.Clear();
            SetState(ConnectionState.Disconnected);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task SubscribeAsync(string topicFilter, int qos, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicFilter);
        EnsureConnected();

        var trimmed = topicFilter.Trim();
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(trimmed)
                .WithQualityOfServiceLevel(MapQos(qos)))
            .Build();

        await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
        _activeSubscriptions[trimmed] = Math.Clamp(qos, 0, 2);
    }

    public async Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicFilter);
        EnsureConnected();

        var trimmed = topicFilter.Trim();
        var options = new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(trimmed)
            .Build();

        await _client.UnsubscribeAsync(options, cancellationToken).ConfigureAwait(false);
        _activeSubscriptions.TryRemove(trimmed, out _);
    }

    public async Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        int qos,
        bool retain,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        EnsureConnected();

        var builder = new MqttApplicationMessageBuilder()
            .WithTopic(topic.Trim())
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MapQos(qos))
            .WithRetainFlag(retain);

        if (!string.IsNullOrWhiteSpace(contentType) &&
            ActiveBroker?.ProtocolVersion == MqttProtocolVersionOption.V500)
        {
            builder.WithContentType(contentType);
        }

        await _client.PublishAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrokerTestResult> TestConnectionAsync(
        BrokerProfile profile,
        string? plaintextPasswordOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var validationErrors = MqttOptionsBuilder.Validate(profile);
        if (validationErrors.Count > 0)
        {
            return new BrokerTestResult(false, TimeSpan.Zero, string.Join(" ", validationErrors));
        }

        var password = plaintextPasswordOverride ?? _secretProtector.Unprotect(profile.ProtectedPassword);
        var options = MqttOptionsBuilder.Build(profile, password);

        using var testClient = _factory.CreateMqttClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.ConnectionTimeoutSeconds, 1, 300)));

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await testClient.ConnectAsync(options, timeoutCts.Token).ConfigureAwait(false);
            sw.Stop();

            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                try
                {
                    await testClient.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disconnect errors after a successful handshake test.
                }

                return new BrokerTestResult(
                    true,
                    sw.Elapsed,
                    $"Connected to {profile.DisplayUri} ({profile.ProtocolDisplayName}) in {sw.Elapsed.TotalMilliseconds:F0} ms.");
            }

            var reason = string.IsNullOrWhiteSpace(result.ReasonString)
                ? result.ResultCode.ToString()
                : $"{result.ResultCode}: {result.ReasonString}";
            return new BrokerTestResult(false, sw.Elapsed, $"Broker rejected connection ({reason}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new BrokerTestResult(
                false,
                sw.Elapsed,
                $"Connection timed out after {profile.ConnectionTimeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            var root = ex.GetBaseException();
            return new BrokerTestResult(false, sw.Elapsed, root.Message);
        }
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var msg = args.ApplicationMessage;
        var payloadBytes = msg.Payload.IsEmpty ? [] : msg.Payload.ToArray();

        var received = new MqttReceivedMessage(
            DateTimeOffset.UtcNow,
            msg.Topic ?? string.Empty,
            payloadBytes,
            (int)msg.QualityOfServiceLevel,
            msg.Retain,
            msg.ContentType);

        MessageReceived?.Invoke(this, received);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_userRequestedDisconnect || ActiveBroker is null || !ActiveBroker.AutoReconnect)
        {
            SetState(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        SetState(ConnectionState.Reconnecting);
        StartReconnectLoop(ActiveBroker.Clone());
        return Task.CompletedTask;
    }

    private void StartReconnectLoop(BrokerProfile profile)
    {
        CancelReconnectLoop();
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;

        _ = Task.Run(async () =>
        {
            var delaySeconds = 2;
            while (!cts.IsCancellationRequested && !_userRequestedDisconnect)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token).ConfigureAwait(false);
                    if (_userRequestedDisconnect || cts.IsCancellationRequested)
                    {
                        break;
                    }

                    await _connectionLock.WaitAsync(cts.Token).ConfigureAwait(false);
                    try
                    {
                        if (_client.IsConnected || _userRequestedDisconnect)
                        {
                            break;
                        }

                        var password = _secretProtector.Unprotect(profile.ProtectedPassword);
                        var options = MqttOptionsBuilder.Build(profile, password);
                        var result = await _client.ConnectAsync(options, cts.Token).ConfigureAwait(false);

                        if (result.ResultCode == MqttClientConnectResultCode.Success)
                        {
                            if (profile.CleanSession && !_activeSubscriptions.IsEmpty)
                            {
                                var subBuilder = new MqttClientSubscribeOptionsBuilder();
                                foreach (var (topic, qos) in _activeSubscriptions)
                                {
                                    subBuilder.WithTopicFilter(f => f
                                        .WithTopic(topic)
                                        .WithQualityOfServiceLevel(MapQos(qos)));
                                }

                                await _client.SubscribeAsync(subBuilder.Build(), cts.Token).ConfigureAwait(false);
                            }

                            SetState(ConnectionState.Connected);
                            break;
                        }
                    }
                    finally
                    {
                        _connectionLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    delaySeconds = Math.Min(delaySeconds * 2, 30);
                }
            }
        }, cts.Token);
    }

    private void CancelReconnectLoop()
    {
        var previous = Interlocked.Exchange(ref _reconnectCts, null);
        if (previous is not null)
        {
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }
    }

    private void EnsureConnected()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Not connected to an MQTT broker.");
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    private static MqttQualityOfServiceLevel MapQos(int qos) =>
        qos switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _userRequestedDisconnect = true;
        CancelReconnectLoop();

        _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        _client.Dispose();
        _connectionLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _userRequestedDisconnect = true;
        CancelReconnectLoop();

        _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder()
                        .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                        .Build(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during shutdown.
            }
        }

        _client.Dispose();
        _connectionLock.Dispose();
    }
}
