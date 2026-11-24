using System.IO;
using System.Text;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Tests;

public sealed class ClientViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;
    private readonly BrokerProfileStore _store;
    private readonly SettingsService _settings;
    private readonly FakeMqttClientService _mqtt;
    private readonly MainWindowViewModel _mainVm;
    private readonly ClientViewModel _vm;

    public ClientViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_ClientVm_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
        _store = new BrokerProfileStore(_paths);
        _settings = new SettingsService(_paths, new AppThemeService());
        _mqtt = new FakeMqttClientService();
        _mainVm = new MainWindowViewModel(_mqtt);

        _vm = new ClientViewModel(
            new NavigationService(new NullPageProvider()),
            _mqtt,
            _store,
            _settings,
            new PayloadCodecService(),
            new SnackbarService(),
            _mainVm);
    }

    [Fact]
    public async Task Connect_AutoSubscribesDefaultTopics_AndDisconnectClearsSubscriptions()
    {
        var broker = new BrokerProfile
        {
            Name = "Dev",
            Host = "localhost",
            DefaultTopics = ["sensors/#", "alerts/critical"]
        };
        await _store.UpsertAsync(broker);
        await _vm.EnsureLoadedAsync();

        Assert.Equal(broker.Id, _vm.SelectedBroker?.Id);

        await _vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(_vm.IsConnected);
        Assert.Equal(2, _vm.Subscriptions.Count);
        Assert.Contains(_mqtt.SubscribedTopics, t => t == "sensors/#");
        Assert.Contains(_mqtt.SubscribedTopics, t => t == "alerts/critical");

        await _vm.DisconnectCommand.ExecuteAsync(null);
        Assert.True(_vm.IsDisconnected);
        Assert.Empty(_vm.Subscriptions);
    }

    [Fact]
    public async Task Publish_EncodesHexAndJson_AndLogsOutboundMessage()
    {
        var broker = new BrokerProfile { Name = "Dev", Host = "localhost" };
        await _store.UpsertAsync(broker);
        await _vm.EnsureLoadedAsync();
        await _vm.ConnectCommand.ExecuteAsync(null);

        // Publish Hex payload
        _vm.PublishTopic = "devices/cmd";
        _vm.PublishFormat = PayloadFormat.Hex;
        _vm.PublishPayload = "CA FE BA BE";
        _vm.PublishQos = 1;
        _vm.PublishRetain = true;

        await _vm.PublishCommand.ExecuteAsync(null);

        Assert.Null(_vm.PublishValidationError);
        var sent = Assert.Single(_mqtt.PublishedMessages);
        Assert.Equal("devices/cmd", sent.Topic);
        Assert.Equal([0xCA, 0xFE, 0xBA, 0xBE], sent.Payload);
        Assert.Equal(1, sent.Qos);
        Assert.True(sent.Retain);

        var logged = Assert.Single(_vm.VisibleMessages);
        Assert.Equal(MessageDirection.Outbound, logged.Direction);
        Assert.Equal(1, _mainVm.MessageCount);

        // Reject wildcard in publish topic
        _vm.PublishTopic = "devices/#";
        await _vm.PublishCommand.ExecuteAsync(null);
        Assert.NotNull(_vm.PublishValidationError);
        Assert.Single(_mqtt.PublishedMessages);
    }

    [Fact]
    public async Task IncomingBatch_UpdatesCounts_SupportsFiltersMuteAndPause()
    {
        var broker = new BrokerProfile { Name = "Dev", Host = "localhost" };
        await _store.UpsertAsync(broker);
        await _vm.EnsureLoadedAsync();
        await _vm.ConnectCommand.ExecuteAsync(null);

        _vm.NewSubscribeTopic = "sensors/#";
        await _vm.SubscribeCommand.ExecuteAsync(null);
        var sub = Assert.Single(_vm.Subscriptions);

        _vm.ProcessIncomingBatch([
            new MqttReceivedMessage(DateTimeOffset.UtcNow, "sensors/room1", Encoding.UTF8.GetBytes("{\"t\":21}"), 0, false, "application/json"),
            new MqttReceivedMessage(DateTimeOffset.UtcNow, "sensors/room2", Encoding.UTF8.GetBytes("{\"t\":25}"), 1, false, "application/json")
        ]);

        Assert.Equal(2, sub.MessageCount);
        Assert.Equal(2, _vm.VisibleMessages.Count);

        // Search filter
        _vm.FeedSearchText = "room2";
        var filtered = Assert.Single(_vm.VisibleMessages);
        Assert.Equal("sensors/room2", filtered.Topic);
        _vm.FeedSearchText = string.Empty;
        Assert.Equal(2, _vm.VisibleMessages.Count);

        // Mute subscription hides its incoming messages
        _vm.ToggleMuteCommand.Execute(sub);
        Assert.True(sub.IsMuted);
        Assert.Empty(_vm.VisibleMessages);
        _vm.ToggleMuteCommand.Execute(sub);
        Assert.Equal(2, _vm.VisibleMessages.Count);

        // Pause buffers incoming messages until resumed
        _vm.TogglePauseCommand.Execute(null);
        _vm.ProcessIncomingBatch([
            new MqttReceivedMessage(DateTimeOffset.UtcNow, "sensors/room3", Encoding.UTF8.GetBytes("29"), 0, false, null)
        ]);
        Assert.Equal(1, _vm.PausedBufferedCount);
        Assert.Equal(2, _vm.VisibleMessages.Count);

        _vm.TogglePauseCommand.Execute(null);
        Assert.Equal(0, _vm.PausedBufferedCount);
        Assert.Equal(3, _vm.VisibleMessages.Count);
    }

    [Fact]
    public void IncomingBatch_EnforcesInMemoryRingBufferCap()
    {
        _settings.Current.MaxInMemoryMessages = 100;

        var batch = Enumerable.Range(1, 130)
            .Select(i => new MqttReceivedMessage(
                DateTimeOffset.UtcNow,
                $"topic/{i}",
                Encoding.UTF8.GetBytes($"msg-{i}"),
                0,
                false,
                null))
            .ToList();

        _vm.ProcessIncomingBatch(batch);

        Assert.Equal(100, _vm.VisibleMessages.Count);
        Assert.Equal("topic/31", _vm.VisibleMessages[0].Topic);
        Assert.Equal("topic/130", _vm.VisibleMessages[^1].Topic);
        Assert.Equal(100, _mainVm.MessageCount);
    }

    public void Dispose()
    {
        _vm.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed record PublishedRecord(string Topic, byte[] Payload, int Qos, bool Retain, string? ContentType);

    private sealed class FakeMqttClientService : IMqttClientService
    {
        private ConnectionState _state = ConnectionState.Disconnected;
        private BrokerProfile? _activeBroker;

        public ConnectionState State => _state;
        public BrokerProfile? ActiveBroker => _activeBroker;
        public List<string> SubscribedTopics { get; } = [];
        public List<PublishedRecord> PublishedMessages { get; } = [];

        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<MqttReceivedMessage>? MessageReceived;
        public event EventHandler<string>? ConnectionError;

        public Task ConnectAsync(BrokerProfile profile, CancellationToken cancellationToken = default)
        {
            _activeBroker = profile.Clone();
            _state = ConnectionState.Connected;
            StateChanged?.Invoke(this, _state);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _state = ConnectionState.Disconnected;
            StateChanged?.Invoke(this, _state);
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(string topicFilter, int qos, CancellationToken cancellationToken = default)
        {
            SubscribedTopics.Add(topicFilter);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
        {
            SubscribedTopics.Remove(topicFilter);
            return Task.CompletedTask;
        }

        public Task PublishAsync(
            string topic,
            ReadOnlyMemory<byte> payload,
            int qos,
            bool retain,
            string? contentType = null,
            CancellationToken cancellationToken = default)
        {
            PublishedMessages.Add(new PublishedRecord(topic, payload.ToArray(), qos, retain, contentType));
            return Task.CompletedTask;
        }

        public Task<BrokerTestResult> TestConnectionAsync(BrokerProfile profile, string? plaintextPasswordOverride = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrokerTestResult(true, TimeSpan.FromMilliseconds(5), "OK"));

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void SuppressWarnings()
        {
            MessageReceived?.Invoke(this, new MqttReceivedMessage(DateTimeOffset.UtcNow, "", [], 0, false, null));
            ConnectionError?.Invoke(this, "");
        }
    }

    private sealed class NullPageProvider : INavigationViewPageProvider
    {
        public object? GetPage(Type pageType) => null;
    }
}
