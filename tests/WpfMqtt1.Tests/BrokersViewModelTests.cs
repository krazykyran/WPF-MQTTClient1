using System.IO;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Tests;

public sealed class BrokersViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;
    private readonly BrokerProfileStore _store;
    private readonly SecretProtectorService _protector;
    private readonly SettingsService _settings;
    private readonly FakeMqttClientService _mqtt;
    private readonly BrokersViewModel _vm;

    public BrokersViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_BrokersVm_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
        _store = new BrokerProfileStore(_paths);
        _protector = new SecretProtectorService();
        _settings = new SettingsService(_paths, new AppThemeService());
        _mqtt = new FakeMqttClientService();

        _vm = new BrokersViewModel(
            _store,
            _protector,
            _mqtt,
            _settings,
            new NavigationService(new NullPageProvider()),
            new SnackbarService(),
            new ContentDialogService());
    }

    [Fact]
    public async Task AddEditSave_AutoUpdatesPortAndEncryptsPassword()
    {
        await _vm.AddBrokerCommand.ExecuteAsync(null);
        Assert.Single(_vm.FilteredBrokers);
        Assert.NotNull(_vm.SelectedBroker);
        Assert.Equal(1883, _vm.EditPort);

        // Switching from TCP (1883) to TLS should auto-suggest port 8883
        _vm.EditTransport = BrokerTransportType.Tls;
        Assert.Equal(8883, _vm.EditPort);
        Assert.True(_vm.IsEditSecure);
        Assert.True(_vm.HasUnsavedChanges);

        _vm.EditName = "Cloud Cluster";
        _vm.EditHost = "broker.hivemq.com";
        _vm.EditUsername = "demo";
        _vm.EditPassword = "my-password";
        _vm.NewDefaultTopic = "sensors/#";
        _vm.AddDefaultTopicCommand.Execute(null);

        await _vm.SaveBrokerCommand.ExecuteAsync(null);

        Assert.False(_vm.HasUnsavedChanges);
        Assert.Null(_vm.ValidationErrorMessage);

        var saved = Assert.Single(await _store.GetAllAsync());
        Assert.Equal("Cloud Cluster", saved.Name);
        Assert.Equal("broker.hivemq.com", saved.Host);
        Assert.Equal(8883, saved.Port);
        Assert.Equal("my-password", _protector.Unprotect(saved.ProtectedPassword));
        Assert.Equal(["sensors/#"], saved.DefaultTopics);
    }

    [Fact]
    public async Task TestConnectionCommand_PopulatesSuccessBanner()
    {
        await _vm.AddBrokerCommand.ExecuteAsync(null);
        _mqtt.NextTestResult = new BrokerTestResult(true, TimeSpan.FromMilliseconds(24), "Connected in 24 ms.");

        await _vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(_vm.IsTestResultOpen);
        Assert.Equal("Connection Succeeded", _vm.TestResultTitle);
        Assert.Equal("Connected in 24 ms.", _vm.TestResultMessage);
    }

    [Fact]
    public async Task DuplicateAndDelete_UpdatesStoredBrokers()
    {
        await _vm.AddBrokerCommand.ExecuteAsync(null);
        var originalId = _vm.SelectedBroker!.Id;

        await _vm.DuplicateBrokerCommand.ExecuteAsync(null);
        Assert.Equal(2, _vm.FilteredBrokers.Count);
        Assert.NotEqual(originalId, _vm.SelectedBroker!.Id);
        Assert.EndsWith("(Copy)", _vm.SelectedBroker!.Name);

        await _vm.DeleteBrokerCommand.ExecuteAsync(null);
        Assert.Single(_vm.FilteredBrokers);
    }

    [Fact]
    public async Task ConnectAndDisconnect_TogglesActiveBrokerState()
    {
        await _vm.AddBrokerCommand.ExecuteAsync(null);
        Assert.False(_vm.IsSelectedBrokerActive);
        Assert.True(_vm.CanConnectSelectedBroker);

        await _vm.ConnectBrokerCommand.ExecuteAsync(null);
        Assert.True(_vm.IsSelectedBrokerActive);
        Assert.False(_vm.CanConnectSelectedBroker);
        Assert.Equal("Connected", _vm.ActiveBrokerStateText);

        await _vm.DisconnectBrokerCommand.ExecuteAsync(null);
        Assert.False(_vm.IsSelectedBrokerActive);
        Assert.True(_vm.CanConnectSelectedBroker);
        Assert.Equal("Disconnected", _vm.ActiveBrokerStateText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeMqttClientService : IMqttClientService
    {
        private ConnectionState _state = ConnectionState.Disconnected;
        private BrokerProfile? _activeBroker;

        public ConnectionState State => _state;
        public BrokerProfile? ActiveBroker => _activeBroker;
        public BrokerTestResult NextTestResult { get; set; } = new(true, TimeSpan.FromMilliseconds(10), "OK");

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
        public Task SubscribeAsync(string topicFilter, int qos, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, int qos, bool retain, string? contentType = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BrokerTestResult> TestConnectionAsync(BrokerProfile profile, string? plaintextPasswordOverride = null, CancellationToken cancellationToken = default) => Task.FromResult(NextTestResult);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void SuppressUnusedEventWarnings()
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
