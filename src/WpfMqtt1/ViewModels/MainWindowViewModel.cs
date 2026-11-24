using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IMqttClientService? _mqttClientService;

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

    public MainWindowViewModel(IMqttClientService mqttClientService)
    {
        _mqttClientService = mqttClientService;
        _mqttClientService.StateChanged += OnMqttStateChanged;
        SyncFromMqttService(_mqttClientService.State);
    }

    [RelayCommand(CanExecute = nameof(IsConnectedOrConnecting))]
    private async Task DisconnectAsync()
    {
        if (_mqttClientService is null)
        {
            return;
        }

        await _mqttClientService.DisconnectAsync();
    }

    private void OnMqttStateChanged(object? sender, ConnectionState newState)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => SyncFromMqttService(newState));
        }
        else
        {
            SyncFromMqttService(newState);
        }
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
}
