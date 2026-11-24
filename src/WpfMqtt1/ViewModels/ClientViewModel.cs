using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1.ViewModels;

public sealed partial class ClientViewModel : ObservableObject, IDisposable
{
    public const string AllTopicsFilterLabel = "All Topics";

    private static readonly string[] SubscriptionPalette =
    [
        "#3B82F6", // Blue
        "#22C55E", // Green
        "#F59E0B", // Amber
        "#A855F7", // Purple
        "#EC4899", // Pink
        "#14B8A6", // Teal
        "#F97316", // Orange
        "#6366F1"  // Indigo
    ];

    private readonly INavigationService _navigationService;
    private readonly IMqttClientService _mqttClientService;
    private readonly IBrokerProfileStore _brokerProfileStore;
    private readonly ISettingsService _settingsService;
    private readonly IPayloadCodecService _payloadCodec;
    private readonly ISnackbarService _snackbarService;
    private readonly MainWindowViewModel _mainWindowViewModel;

    private readonly Channel<MqttReceivedMessage> _incomingChannel = Channel.CreateUnbounded<MqttReceivedMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly List<MqttLogMessage> _allMessages = [];
    private readonly List<MqttLogMessage> _pausedBuffer = [];

    private long _nextSequenceId;
    private int _colorCursor;
    private bool _isInitialized;
    private Guid? _autoSubscribedBrokerId;
    private bool _disposed;

    public ObservableCollection<BrokerProfile> AvailableBrokers { get; } = [];
    public ObservableCollection<TopicSubscription> Subscriptions { get; } = [];
    public ObservableCollection<MqttLogMessage> VisibleMessages { get; } = [];
    public ObservableCollection<string> TopicFilterOptions { get; } = [AllTopicsFilterLabel];
    public ObservableCollection<string> RecentPublishTopics { get; } = [];

    public IReadOnlyList<int> AvailableQosLevels { get; } = [0, 1, 2];

    public IReadOnlyList<MessageDirectionFilter> AvailableDirectionFilters { get; } =
    [
        MessageDirectionFilter.All,
        MessageDirectionFilter.Inbound,
        MessageDirectionFilter.Outbound
    ];

    public IReadOnlyList<PayloadFormat> AvailablePayloadFormats { get; } =
    [
        PayloadFormat.Text,
        PayloadFormat.Json,
        PayloadFormat.Hex,
        PayloadFormat.Base64
    ];

    public event EventHandler? MessagesAppended;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private BrokerProfile? _selectedBroker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(IsConnectedOrConnecting))]
    [NotifyPropertyChangedFor(nameof(ConnectionStateText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubscribeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private string _activeBrokerDisplay = "Not connected";

    // Subscriptions
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubscribeCommand))]
    private string _newSubscribeTopic = string.Empty;

    [ObservableProperty]
    private int _newSubscribeQos;

    // Live Feed Filters
    [ObservableProperty]
    private string _feedSearchText = string.Empty;

    [ObservableProperty]
    private MessageDirectionFilter _directionFilter = MessageDirectionFilter.All;

    [ObservableProperty]
    private string _selectedTopicFilter = AllTopicsFilterLabel;

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool _isFeedPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private int _pausedBufferedCount;

    [ObservableProperty]
    private MqttLogMessage? _selectedMessage;

    [ObservableProperty]
    private PayloadFormat _inspectorFormat = PayloadFormat.Text;

    // Publisher
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _publishTopic = string.Empty;

    [ObservableProperty]
    private int _publishQos;

    [ObservableProperty]
    private bool _publishRetain;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJsonPublishFormat))]
    private PayloadFormat _publishFormat = PayloadFormat.Text;

    [ObservableProperty]
    private string _publishPayload = string.Empty;

    [ObservableProperty]
    private string? _publishValidationError;

    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool IsDisconnected => ConnectionState == ConnectionState.Disconnected;
    public bool IsConnectedOrConnecting =>
        ConnectionState is ConnectionState.Connected or ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool IsJsonPublishFormat => PublishFormat == PayloadFormat.Json;

    public string ConnectionStateText => ConnectionState switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Reconnecting => "Reconnecting...",
        _ => "Disconnected"
    };

    public string PauseButtonText => IsFeedPaused
        ? PausedBufferedCount > 0 ? $"Resume (+{PausedBufferedCount})" : "Resume"
        : "Pause";

    public ClientViewModel(
        INavigationService navigationService,
        IMqttClientService mqttClientService,
        IBrokerProfileStore brokerProfileStore,
        ISettingsService settingsService,
        IPayloadCodecService payloadCodec,
        ISnackbarService snackbarService,
        MainWindowViewModel mainWindowViewModel)
    {
        _navigationService = navigationService;
        _mqttClientService = mqttClientService;
        _brokerProfileStore = brokerProfileStore;
        _settingsService = settingsService;
        _payloadCodec = payloadCodec;
        _snackbarService = snackbarService;
        _mainWindowViewModel = mainWindowViewModel;

        ApplySettingsDefaults();

        _mqttClientService.StateChanged += OnMqttStateChanged;
        _mqttClientService.MessageReceived += OnMqttMessageReceived;
        _brokerProfileStore.ProfilesChanged += OnBrokerProfilesChanged;

        SyncConnectionState(_mqttClientService.State);
        _ = Task.Run(() => RunIncomingDrainLoopAsync(_pumpCts.Token));
    }

    public async Task EnsureLoadedAsync()
    {
        if (_isInitialized)
        {
            await RefreshBrokersAsync();
            return;
        }

        ApplySettingsDefaults();
        await RefreshBrokersAsync();
        _isInitialized = true;

        if (_mqttClientService.State == ConnectionState.Connected && _mqttClientService.ActiveBroker is not null)
        {
            await AutoSubscribeDefaultTopicsAsync(_mqttClientService.ActiveBroker);
        }
    }

    public async Task RefreshBrokersAsync()
    {
        var profiles = await _brokerProfileStore.GetAllAsync();
        RunOnUiThread(() =>
        {
            var preferredId = _mqttClientService.ActiveBroker?.Id
                ?? SelectedBroker?.Id
                ?? _settingsService.Current.LastSelectedBrokerId;

            AvailableBrokers.Clear();
            foreach (var profile in profiles)
            {
                AvailableBrokers.Add(profile);
            }

            SelectedBroker = (preferredId.HasValue
                ? AvailableBrokers.FirstOrDefault(b => b.Id == preferredId.Value)
                : null) ?? AvailableBrokers.FirstOrDefault();
        });
    }

    partial void OnFeedSearchTextChanged(string value) => RebuildVisibleMessages();
    partial void OnDirectionFilterChanged(MessageDirectionFilter value) => RebuildVisibleMessages();
    partial void OnSelectedTopicFilterChanged(string value) => RebuildVisibleMessages();

    partial void OnPublishFormatChanged(PayloadFormat value) => ValidatePublishPayload();
    partial void OnPublishPayloadChanged(string value) => ValidatePublishPayload();

    partial void OnIsFeedPausedChanged(bool value)
    {
        if (!value && _pausedBuffer.Count > 0)
        {
            var flushed = _pausedBuffer.ToList();
            _pausedBuffer.Clear();
            PausedBufferedCount = 0;
            AppendLogMessagesOnUiThread(flushed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedBroker is null)
        {
            return;
        }

        IsConnecting = true;
        try
        {
            _settingsService.Current.LastSelectedBrokerId = SelectedBroker.Id;
            await _settingsService.SaveAsync();

            _autoSubscribedBrokerId = null;
            await _mqttClientService.ConnectAsync(SelectedBroker);
            await AutoSubscribeDefaultTopicsAsync(SelectedBroker);

            ShowSnackbar(
                "Connected",
                $"Connected to {SelectedBroker.Name} ({SelectedBroker.DisplayUri}).",
                ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            ShowSnackbar("Connection Failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() => SelectedBroker is not null && IsDisconnected && !IsConnecting;

    [RelayCommand(CanExecute = nameof(IsConnectedOrConnecting))]
    private async Task DisconnectAsync()
    {
        var name = _mqttClientService.ActiveBroker?.Name ?? "broker";
        await _mqttClientService.DisconnectAsync();
        Subscriptions.Clear();
        RebuildTopicFilterOptions();
        _autoSubscribedBrokerId = null;
        ShowSnackbar("Disconnected", $"Disconnected from {name}.", ControlAppearance.Info);
    }

    [RelayCommand(CanExecute = nameof(CanSubscribe))]
    private async Task SubscribeAsync()
    {
        var filter = NewSubscribeTopic?.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        if (Subscriptions.Any(s => string.Equals(s.TopicFilter, filter, StringComparison.Ordinal)))
        {
            ShowSnackbar("Already subscribed", $"Topic \"{filter}\" is already subscribed.", ControlAppearance.Caution);
            return;
        }

        try
        {
            await _mqttClientService.SubscribeAsync(filter, NewSubscribeQos);
            AddSubscriptionRow(filter, NewSubscribeQos);
            NewSubscribeTopic = string.Empty;
        }
        catch (Exception ex)
        {
            ShowSnackbar("Subscribe Failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
    }

    private bool CanSubscribe() => IsConnected && !string.IsNullOrWhiteSpace(NewSubscribeTopic);

    [RelayCommand]
    private async Task UnsubscribeAsync(TopicSubscription? subscription)
    {
        if (subscription is null)
        {
            return;
        }

        try
        {
            if (IsConnected)
            {
                await _mqttClientService.UnsubscribeAsync(subscription.TopicFilter);
            }

            Subscriptions.Remove(subscription);
            RebuildTopicFilterOptions();
        }
        catch (Exception ex)
        {
            ShowSnackbar("Unsubscribe Failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    private void ToggleMute(TopicSubscription? subscription)
    {
        if (subscription is null)
        {
            return;
        }

        subscription.IsMuted = !subscription.IsMuted;
        RebuildVisibleMessages();
    }

    [RelayCommand]
    private void UseTopicInPublisher(TopicSubscription? subscription)
    {
        if (subscription is null)
        {
            return;
        }

        PublishTopic = subscription.TopicFilter;
        RememberPublishTopic(subscription.TopicFilter);
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsFeedPaused = !IsFeedPaused;
    }

    [RelayCommand]
    private void ClearMessages()
    {
        _allMessages.Clear();
        _pausedBuffer.Clear();
        PausedBufferedCount = 0;
        VisibleMessages.Clear();
        SelectedMessage = null;

        foreach (var sub in Subscriptions)
        {
            sub.MessageCount = 0;
        }

        _mainWindowViewModel.MessageCount = 0;
    }

    [RelayCommand]
    private void FormatPublishJson()
    {
        if (_payloadCodec.TryFormatJson(PublishPayload, out var formatted, out var error))
        {
            PublishPayload = formatted;
            PublishValidationError = null;
        }
        else
        {
            PublishValidationError = error;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        var topic = PublishTopic?.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        if (topic.Contains('#') || topic.Contains('+'))
        {
            PublishValidationError = "Publish topics cannot contain MQTT wildcards ('+' or '#').";
            return;
        }

        if (!_payloadCodec.TryEncodePayload(PublishPayload, PublishFormat, out var payloadBytes, out var error))
        {
            PublishValidationError = error;
            return;
        }

        PublishValidationError = null;
        var contentType = PublishFormat switch
        {
            PayloadFormat.Json => "application/json",
            PayloadFormat.Text => "text/plain",
            _ => null
        };

        try
        {
            await _mqttClientService.PublishAsync(topic, payloadBytes, PublishQos, PublishRetain, contentType);
            RememberPublishTopic(topic);

            var outbound = new MqttLogMessage
            {
                SequenceId = Interlocked.Increment(ref _nextSequenceId),
                TimestampUtc = DateTimeOffset.UtcNow,
                Direction = MessageDirection.Outbound,
                Topic = topic,
                Qos = PublishQos,
                Retain = PublishRetain,
                Payload = payloadBytes,
                ContentType = contentType,
                PayloadPreview = _payloadCodec.CreatePreview(payloadBytes),
                AccentColorHex = "#3B82F6"
            };

            AppendLogMessagesOnUiThread([outbound]);
        }
        catch (Exception ex)
        {
            ShowSnackbar("Publish Failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
    }

    private bool CanPublish() => IsConnected && !string.IsNullOrWhiteSpace(PublishTopic);

    [RelayCommand]
    private void OpenBrokers()
    {
        _navigationService.Navigate(typeof(BrokersPage));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _navigationService.Navigate(typeof(SettingsPage));
    }

    internal void ProcessIncomingBatch(IReadOnlyList<MqttReceivedMessage> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var logItems = new List<MqttLogMessage>(batch.Count);

        foreach (var msg in batch)
        {
            var matchingSubs = Subscriptions.Where(s => MqttTopicMatcher.IsMatch(s.TopicFilter, msg.Topic)).ToList();
            foreach (var sub in matchingSubs)
            {
                sub.MessageCount++;
            }

            var accent = matchingSubs.FirstOrDefault()?.ColorHex ?? "#22C55E";

            var item = new MqttLogMessage
            {
                SequenceId = Interlocked.Increment(ref _nextSequenceId),
                TimestampUtc = msg.TimestampUtc,
                Direction = MessageDirection.Inbound,
                Topic = msg.Topic,
                Qos = msg.Qos,
                Retain = msg.Retain,
                Payload = msg.Payload,
                ContentType = msg.ContentType,
                PayloadPreview = _payloadCodec.CreatePreview(msg.Payload),
                AccentColorHex = accent
            };

            logItems.Add(item);
        }

        if (IsFeedPaused)
        {
            _pausedBuffer.AddRange(logItems);
            PausedBufferedCount = _pausedBuffer.Count;
            return;
        }

        AppendLogMessagesOnUiThread(logItems);
    }

    private void AppendLogMessagesOnUiThread(IReadOnlyList<MqttLogMessage> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var cap = Math.Clamp(_settingsService.Current.MaxInMemoryMessages, 100, 100_000);
        var addedToVisible = false;

        foreach (var item in items)
        {
            _allMessages.Add(item);
            if (MatchesFilters(item))
            {
                VisibleMessages.Add(item);
                addedToVisible = true;
            }
        }

        if (_allMessages.Count > cap)
        {
            var excess = _allMessages.Count - cap;
            var evicted = new HashSet<long>();
            for (var i = 0; i < excess; i++)
            {
                evicted.Add(_allMessages[i].SequenceId);
            }

            _allMessages.RemoveRange(0, excess);

            while (VisibleMessages.Count > 0 && evicted.Contains(VisibleMessages[0].SequenceId))
            {
                if (ReferenceEquals(SelectedMessage, VisibleMessages[0]))
                {
                    SelectedMessage = null;
                }

                VisibleMessages.RemoveAt(0);
            }
        }

        _mainWindowViewModel.MessageCount = _allMessages.Count;

        if (addedToVisible && IsAutoScrollEnabled)
        {
            MessagesAppended?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildVisibleMessages()
    {
        VisibleMessages.Clear();
        foreach (var item in _allMessages)
        {
            if (MatchesFilters(item))
            {
                VisibleMessages.Add(item);
            }
        }
    }

    private bool MatchesFilters(MqttLogMessage msg)
    {
        if (DirectionFilter == MessageDirectionFilter.Inbound && msg.Direction != MessageDirection.Inbound)
        {
            return false;
        }

        if (DirectionFilter == MessageDirectionFilter.Outbound && msg.Direction != MessageDirection.Outbound)
        {
            return false;
        }

        if (msg.Direction == MessageDirection.Inbound && Subscriptions.Count > 0)
        {
            var matchingSubs = Subscriptions.Where(s => MqttTopicMatcher.IsMatch(s.TopicFilter, msg.Topic)).ToList();
            if (matchingSubs.Count > 0 && matchingSubs.All(s => s.IsMuted))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(SelectedTopicFilter) &&
            !string.Equals(SelectedTopicFilter, AllTopicsFilterLabel, StringComparison.Ordinal))
        {
            if (!MqttTopicMatcher.IsMatch(SelectedTopicFilter, msg.Topic) &&
                !string.Equals(SelectedTopicFilter, msg.Topic, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var search = FeedSearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var matchTopic = msg.Topic.Contains(search, StringComparison.OrdinalIgnoreCase);
            var matchPreview = msg.PayloadPreview.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!matchTopic && !matchPreview)
            {
                var utf8 = msg.Payload.Length > 0 ? Encoding.UTF8.GetString(msg.Payload) : string.Empty;
                if (!utf8.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private async Task AutoSubscribeDefaultTopicsAsync(BrokerProfile broker)
    {
        if (_autoSubscribedBrokerId == broker.Id || broker.DefaultTopics.Count == 0)
        {
            return;
        }

        _autoSubscribedBrokerId = broker.Id;

        foreach (var raw in broker.DefaultTopics)
        {
            var topic = raw?.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                continue;
            }

            if (Subscriptions.Any(s => string.Equals(s.TopicFilter, topic, StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                await _mqttClientService.SubscribeAsync(topic, 0);
                RunOnUiThread(() => AddSubscriptionRow(topic, 0));
            }
            catch
            {
                // Continue subscribing remaining defaults even if one fails.
            }
        }
    }

    private void AddSubscriptionRow(string topicFilter, int qos)
    {
        var color = SubscriptionPalette[_colorCursor++ % SubscriptionPalette.Length];
        Subscriptions.Add(new TopicSubscription(topicFilter, qos, color));
        RebuildTopicFilterOptions();

        if (!topicFilter.Contains('#') && !topicFilter.Contains('+'))
        {
            RememberPublishTopic(topicFilter);
            if (string.IsNullOrWhiteSpace(PublishTopic))
            {
                PublishTopic = topicFilter;
            }
        }
    }

    private void RebuildTopicFilterOptions()
    {
        var current = SelectedTopicFilter;
        TopicFilterOptions.Clear();
        TopicFilterOptions.Add(AllTopicsFilterLabel);
        foreach (var sub in Subscriptions)
        {
            TopicFilterOptions.Add(sub.TopicFilter);
        }

        SelectedTopicFilter = TopicFilterOptions.Contains(current) ? current : AllTopicsFilterLabel;
    }

    private void RememberPublishTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var existingIndex = RecentPublishTopics.IndexOf(topic);
        if (existingIndex == 0)
        {
            return;
        }

        if (existingIndex > 0)
        {
            RecentPublishTopics.Move(existingIndex, 0);
        }
        else
        {
            RecentPublishTopics.Insert(0, topic);
            while (RecentPublishTopics.Count > 20)
            {
                RecentPublishTopics.RemoveAt(RecentPublishTopics.Count - 1);
            }
        }
    }

    private void ValidatePublishPayload()
    {
        if (PublishFormat == PayloadFormat.Text || string.IsNullOrWhiteSpace(PublishPayload))
        {
            PublishValidationError = null;
            return;
        }

        _payloadCodec.TryEncodePayload(PublishPayload, PublishFormat, out _, out var error);
        PublishValidationError = error;
    }

    private void ApplySettingsDefaults()
    {
        PublishQos = Math.Clamp(_settingsService.Current.DefaultPublishQos, 0, 2);
        PublishFormat = _settingsService.Current.DefaultPayloadFormat;
        InspectorFormat = _settingsService.Current.DefaultPayloadFormat;
    }

    private void OnMqttMessageReceived(object? sender, MqttReceivedMessage message)
    {
        _incomingChannel.Writer.TryWrite(message);
    }

    private async Task RunIncomingDrainLoopAsync(CancellationToken ct)
    {
        var batch = new List<MqttReceivedMessage>(128);
        try
        {
            while (await _incomingChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < 200 && _incomingChannel.Reader.TryRead(out var msg))
                {
                    batch.Add(msg);
                }

                if (batch.Count > 0)
                {
                    var snapshot = batch.ToArray();
                    RunOnUiThread(() => ProcessIncomingBatch(snapshot));
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void OnBrokerProfilesChanged(object? sender, EventArgs e)
    {
        _ = RefreshBrokersAsync();
    }

    private void OnMqttStateChanged(object? sender, ConnectionState newState)
    {
        RunOnUiThread(() =>
        {
            SyncConnectionState(newState);

            if (newState == ConnectionState.Disconnected)
            {
                Subscriptions.Clear();
                RebuildTopicFilterOptions();
                _autoSubscribedBrokerId = null;
            }
            else if (newState == ConnectionState.Connected && _mqttClientService.ActiveBroker is not null)
            {
                var active = _mqttClientService.ActiveBroker;
                var match = AvailableBrokers.FirstOrDefault(b => b.Id == active.Id);
                if (match is not null)
                {
                    SelectedBroker = match;
                }

                _ = AutoSubscribeDefaultTopicsAsync(active);
            }
        });
    }

    private void SyncConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        var active = _mqttClientService.ActiveBroker;
        ActiveBrokerDisplay = active is not null && state != ConnectionState.Disconnected
            ? $"{active.Name}  •  {active.DisplayUri} ({active.ProtocolDisplayName})"
            : "Not connected";
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void ShowSnackbar(string title, string message, ControlAppearance appearance)
    {
        try
        {
            var icon = appearance switch
            {
                ControlAppearance.Success => SymbolRegular.CheckmarkCircle24,
                ControlAppearance.Danger => SymbolRegular.ErrorCircle24,
                ControlAppearance.Caution => SymbolRegular.Warning24,
                _ => SymbolRegular.Info24
            };

            _snackbarService.Show(
                title,
                message,
                appearance,
                new SymbolIcon(icon),
                TimeSpan.FromSeconds(4));
        }
        catch
        {
            // Ignore when no snackbar presenter is attached (e.g., unit tests).
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mqttClientService.StateChanged -= OnMqttStateChanged;
        _mqttClientService.MessageReceived -= OnMqttMessageReceived;
        _brokerProfileStore.ProfilesChanged -= OnBrokerProfilesChanged;

        _incomingChannel.Writer.TryComplete();
        _pumpCts.Cancel();
        _pumpCts.Dispose();
    }
}
