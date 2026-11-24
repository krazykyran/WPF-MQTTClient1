using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IMqttClientService? _mqttClientService;
    private readonly ISessionRecorderService? _sessionRecorderService;

    [ObservableProperty]
    private string _applicationTitle = "WpfMqtt1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStateText))]
    [NotifyPropertyChangedFor(nameof(IsConnectedOrConnecting))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _activeBrokerUri = "No broker selected";

    [ObservableProperty]
    private int _messageCount;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _recordingStatusText = "Not recording";

    public bool IsConnectedOrConnecting =>
        ConnectionState is ConnectionState.Connected or ConnectionState.Connecting or ConnectionState.Reconnecting;

    public string ConnectionStateText => ConnectionState switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Reconnecting => "Reconnecting...",
        _ => "Disconnected"
    };

    public MainWindowViewModel()
    {
    }

    public MainWindowViewModel(
        IMqttClientService mqttClientService,
        ISessionRecorderService? sessionRecorderService = null)
    {
        _mqttClientService = mqttClientService;
        _sessionRecorderService = sessionRecorderService;

        _mqttClientService.StateChanged += OnMqttStateChanged;
        SyncFromMqttService(_mqttClientService.State);

        if (_sessionRecorderService is not null)
        {
            _sessionRecorderService.RecordingStateChanged += OnRecordingChanged;
            _sessionRecorderService.RecordedCountChanged += OnRecordedCountChanged;
            SyncFromRecorder();
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnectedOrConnecting))]
    private async Task DisconnectAsync()
    {
        if (_mqttClientService is null)
        {
            return;
        }

        if (_sessionRecorderService is { IsRecording: true })
        {
            await _sessionRecorderService.StopRecordingAsync();
        }

        await _mqttClientService.DisconnectAsync();
    }

    private void OnMqttStateChanged(object? sender, ConnectionState newState)
    {
        DispatchToUi(() => SyncFromMqttService(newState));
    }

    private void OnRecordingChanged(object? sender, EventArgs e)
    {
        DispatchToUi(SyncFromRecorder);
    }

    private void OnRecordedCountChanged(object? sender, long count)
    {
        DispatchToUi(SyncFromRecorder);
    }

    private void SyncFromMqttService(ConnectionState state)
    {
        ConnectionState = state;
        var active = _mqttClientService?.ActiveBroker;
        ActiveBrokerUri = active is not null && state != ConnectionState.Disconnected
            ? $"{active.Name}  •  {active.DisplayUri}"
            : active is not null
                ? $"{active.Name} ({active.DisplayUri})"
                : "No broker selected";
    }

    private void SyncFromRecorder()
    {
        if (_sessionRecorderService is null)
        {
            IsRecording = false;
            RecordingStatusText = "Not recording";
            return;
        }

        IsRecording = _sessionRecorderService.IsRecording;
        RecordingStatusText = IsRecording
            ? $"REC • {_sessionRecorderService.RecordedMessageCount:N0} logged"
            : "Not recording";
    }

    private static void DispatchToUi(Action action)
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
}
