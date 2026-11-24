using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1.ViewModels;

public sealed partial class ClientViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IMqttClientService _mqttClientService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectedOrConnecting))]
    [NotifyPropertyChangedFor(nameof(ConnectionStateText))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _activeBrokerDisplay = "Not connected to any broker";

    public bool IsConnectedOrConnecting =>
        ConnectionState is ConnectionState.Connected or ConnectionState.Connecting or ConnectionState.Reconnecting;

    public string ConnectionStateText => ConnectionState switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Reconnecting => "Reconnecting...",
        _ => "Disconnected"
    };

    public ClientViewModel(INavigationService navigationService, IMqttClientService mqttClientService)
    {
        _navigationService = navigationService;
        _mqttClientService = mqttClientService;
        _mqttClientService.StateChanged += OnMqttStateChanged;
        SyncConnectionState(_mqttClientService.State);
    }

    [RelayCommand(CanExecute = nameof(IsConnectedOrConnecting))]
    private async Task DisconnectAsync()
    {
        await _mqttClientService.DisconnectAsync();
    }

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

    private void OnMqttStateChanged(object? sender, ConnectionState newState)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => SyncConnectionState(newState));
        }
        else
        {
            SyncConnectionState(newState);
        }
    }

    private void SyncConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        var active = _mqttClientService.ActiveBroker;
        ActiveBrokerDisplay = active is not null && state != ConnectionState.Disconnected
            ? $"{active.Name} ({active.DisplayUri} • {active.ProtocolDisplayName})"
            : "Not connected to any broker";
    }
}
