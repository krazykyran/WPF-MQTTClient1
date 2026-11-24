using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1.ViewModels;

public sealed partial class BrokersViewModel : ObservableObject
{
    private readonly IBrokerProfileStore _profileStore;
    private readonly ISecretProtectorService _secretProtector;
    private readonly IMqttClientService _mqttClientService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _contentDialogService;

    private readonly List<BrokerProfile> _allBrokers = [];
    private bool _isLoadingEditor;
    private bool _isInitialized;

    public ObservableCollection<BrokerProfile> FilteredBrokers { get; } = [];
    public ObservableCollection<string> EditDefaultTopics { get; } = [];

    public IReadOnlyList<BrokerTransportType> AvailableTransports { get; } =
    [
        BrokerTransportType.Tcp,
        BrokerTransportType.Tls,
        BrokerTransportType.WebSocket,
        BrokerTransportType.WebSocketSecure
    ];

    public IReadOnlyList<MqttProtocolVersionOption> AvailableProtocolVersions { get; } =
    [
        MqttProtocolVersionOption.V500,
        MqttProtocolVersionOption.V311,
        MqttProtocolVersionOption.V310
    ];

    public IReadOnlyList<TlsVersionOption> AvailableTlsVersions { get; } =
    [
        TlsVersionOption.SystemDefault,
        TlsVersionOption.Tls12,
        TlsVersionOption.Tls13
    ];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBroker))]
    [NotifyPropertyChangedFor(nameof(IsSelectedBrokerActive))]
    [NotifyPropertyChangedFor(nameof(CanConnectSelectedBroker))]
    [NotifyPropertyChangedFor(nameof(ActiveBrokerStateText))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectBrokerCommand))]
    private BrokerProfile? _selectedBroker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedBrokerActive))]
    [NotifyPropertyChangedFor(nameof(CanConnectSelectedBroker))]
    [NotifyPropertyChangedFor(nameof(ActiveBrokerStateText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectBrokerCommand))]
    private ConnectionState _mqttConnectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedBrokerActive))]
    [NotifyPropertyChangedFor(nameof(CanConnectSelectedBroker))]
    [NotifyCanExecuteChangedFor(nameof(ConnectBrokerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectBrokerCommand))]
    private Guid? _activeBrokerId;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditSecure))]
    [NotifyPropertyChangedFor(nameof(IsEditWebSocket))]
    [NotifyPropertyChangedFor(nameof(EditPreviewUri))]
    private BrokerTransportType _editTransport = BrokerTransportType.Tcp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewUri))]
    private string _editHost = "localhost";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewUri))]
    private int _editPort = 1883;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewUri))]
    private string _editWebSocketPath = "/mqtt";

    [ObservableProperty]
    private MqttProtocolVersionOption _editProtocolVersion = MqttProtocolVersionOption.V500;

    [ObservableProperty]
    private string _editClientId = string.Empty;

    [ObservableProperty]
    private bool _editCleanSession = true;

    [ObservableProperty]
    private int _editKeepAliveSeconds = 60;

    [ObservableProperty]
    private int _editConnectionTimeoutSeconds = 10;

    [ObservableProperty]
    private bool _editAutoReconnect = true;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private TlsVersionOption _editTlsVersion = TlsVersionOption.SystemDefault;

    [ObservableProperty]
    private bool _editAllowUntrustedCertificates;

    [ObservableProperty]
    private string _newDefaultTopic = string.Empty;

    [ObservableProperty]
    private string? _validationErrorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectBrokerCommand))]
    private bool _isTestingConnection;

    [ObservableProperty]
    private bool _isTestResultOpen;

    [ObservableProperty]
    private string _testResultTitle = string.Empty;

    [ObservableProperty]
    private string _testResultMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _testResultSeverity = InfoBarSeverity.Informational;

    public bool HasSelectedBroker => SelectedBroker is not null;
    public bool IsEditSecure => EditTransport is BrokerTransportType.Tls or BrokerTransportType.WebSocketSecure;
    public bool IsEditWebSocket => EditTransport is BrokerTransportType.WebSocket or BrokerTransportType.WebSocketSecure;

    public bool IsSelectedBrokerActive =>
        SelectedBroker is not null &&
        ActiveBrokerId == SelectedBroker.Id &&
        MqttConnectionState is ConnectionState.Connected or ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool CanConnectSelectedBroker => HasSelectedBroker && !IsSelectedBrokerActive;

    public string ActiveBrokerStateText => MqttConnectionState switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Reconnecting => "Reconnecting...",
        _ => "Disconnected"
    };

    public string EditPreviewUri
    {
        get
        {
            var preview = BuildDraftProfile();
            return preview.DisplayUri;
        }
    }

    public BrokersViewModel(
        IBrokerProfileStore profileStore,
        ISecretProtectorService secretProtector,
        IMqttClientService mqttClientService,
        ISettingsService settingsService,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        _profileStore = profileStore;
        _secretProtector = secretProtector;
        _mqttClientService = mqttClientService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _snackbarService = snackbarService;
        _contentDialogService = contentDialogService;

        _mqttClientService.StateChanged += OnMqttStateChanged;
        SyncMqttState(_mqttClientService.State);
    }

    private void OnMqttStateChanged(object? sender, ConnectionState newState)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => SyncMqttState(newState));
        }
        else
        {
            SyncMqttState(newState);
        }
    }

    private void SyncMqttState(ConnectionState state)
    {
        MqttConnectionState = state;
        ActiveBrokerId = state == ConnectionState.Disconnected ? null : _mqttClientService.ActiveBroker?.Id;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await LoadBrokersAsync();
        _isInitialized = true;
    }

    public async Task LoadBrokersAsync(Guid? selectId = null)
    {
        var profiles = await _profileStore.GetAllAsync();
        _allBrokers.Clear();
        _allBrokers.AddRange(profiles);
        ApplyFilter(selectId ?? SelectedBroker?.Id);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter(SelectedBroker?.Id);
    }

    partial void OnSelectedBrokerChanged(BrokerProfile? value)
    {
        LoadEditorFromProfile(value);
    }

    partial void OnEditNameChanged(string value) => MarkDirty();
    partial void OnEditHostChanged(string value) => MarkDirty();
    partial void OnEditPortChanged(int value) => MarkDirty();
    partial void OnEditWebSocketPathChanged(string value) => MarkDirty();
    partial void OnEditProtocolVersionChanged(MqttProtocolVersionOption value) => MarkDirty();
    partial void OnEditClientIdChanged(string value) => MarkDirty();
    partial void OnEditCleanSessionChanged(bool value) => MarkDirty();
    partial void OnEditKeepAliveSecondsChanged(int value) => MarkDirty();
    partial void OnEditConnectionTimeoutSecondsChanged(int value) => MarkDirty();
    partial void OnEditAutoReconnectChanged(bool value) => MarkDirty();
    partial void OnEditUsernameChanged(string value) => MarkDirty();
    partial void OnEditPasswordChanged(string value) => MarkDirty();
    partial void OnEditTlsVersionChanged(TlsVersionOption value) => MarkDirty();
    partial void OnEditAllowUntrustedCertificatesChanged(bool value) => MarkDirty();

    partial void OnEditTransportChanged(BrokerTransportType value)
    {
        if (!_isLoadingEditor && BrokerProfile.IsStandardPort(EditPort))
        {
            EditPort = BrokerProfile.GetDefaultPort(value);
        }

        MarkDirty();
    }

    [RelayCommand]
    private async Task AddBrokerAsync()
    {
        var baseName = "Local Broker";
        var name = baseName;
        var suffix = 2;
        while (_allBrokers.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {suffix++}";
        }

        var profile = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            Name = name,
            Host = "localhost",
            Port = 1883,
            Transport = BrokerTransportType.Tcp,
            ProtocolVersion = MqttProtocolVersionOption.V500,
            ClientId = BrokerProfile.GenerateClientId()
        };

        await _profileStore.UpsertAsync(profile);
        await LoadBrokersAsync(profile.Id);
        ShowSnackbar("Broker added", $"Created profile \"{profile.Name}\".", ControlAppearance.Success);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBroker))]
    private async Task DuplicateBrokerAsync()
    {
        if (SelectedBroker is null) return;

        var copy = SelectedBroker.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{SelectedBroker.Name} (Copy)";
        copy.ClientId = BrokerProfile.GenerateClientId();

        await _profileStore.UpsertAsync(copy);
        await LoadBrokersAsync(copy.Id);
        ShowSnackbar("Broker duplicated", $"Created \"{copy.Name}\".", ControlAppearance.Info);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBroker))]
    private async Task DeleteBrokerAsync()
    {
        if (SelectedBroker is null) return;

        var target = SelectedBroker;

        var confirmed = await ConfirmDeleteAsync(target.Name);
        if (!confirmed)
        {
            return;
        }

        await _profileStore.DeleteAsync(target.Id);
        await LoadBrokersAsync();
        ShowSnackbar("Broker deleted", $"Removed \"{target.Name}\".", ControlAppearance.Caution);
    }

    [RelayCommand]
    private void RandomizeClientId()
    {
        EditClientId = BrokerProfile.GenerateClientId();
    }

    [RelayCommand]
    private void AddDefaultTopic()
    {
        var topic = NewDefaultTopic?.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        if (!EditDefaultTopics.Contains(topic, StringComparer.Ordinal))
        {
            EditDefaultTopics.Add(topic);
            MarkDirty();
        }

        NewDefaultTopic = string.Empty;
    }

    [RelayCommand]
    private void RemoveDefaultTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        if (EditDefaultTopics.Remove(topic))
        {
            MarkDirty();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBroker))]
    private async Task SaveBrokerAsync()
    {
        await SaveCurrentBrokerCoreAsync(showToast: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBroker))]
    private void RevertChanges()
    {
        LoadEditorFromProfile(SelectedBroker);
    }

    [RelayCommand(CanExecute = nameof(CanTestOrConnect))]
    private async Task TestConnectionAsync()
    {
        if (SelectedBroker is null) return;

        var draft = BuildDraftProfile();
        var errors = MqttOptionsBuilder.Validate(draft);
        if (errors.Count > 0)
        {
            ValidationErrorMessage = string.Join(" ", errors);
            return;
        }

        ValidationErrorMessage = null;
        IsTestingConnection = true;
        IsTestResultOpen = false;

        try
        {
            var result = await _mqttClientService.TestConnectionAsync(draft, EditPassword);
            TestResultTitle = result.Success ? "Connection Succeeded" : "Connection Failed";
            TestResultMessage = result.Message;
            TestResultSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            IsTestResultOpen = true;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectBrokerAsync()
    {
        if (SelectedBroker is null) return;

        var saved = await SaveCurrentBrokerCoreAsync(showToast: false);
        if (saved is null)
        {
            return;
        }

        _settingsService.Current.LastSelectedBrokerId = saved.Id;
        await _settingsService.SaveAsync();

        IsTestingConnection = true;
        try
        {
            await _mqttClientService.ConnectAsync(saved);
            ShowSnackbar("Connected", $"Connected to {saved.Name} ({saved.DisplayUri}).", ControlAppearance.Success);
            _navigationService.Navigate(typeof(ClientPage));
        }
        catch (Exception ex)
        {
            TestResultTitle = "Connection Failed";
            TestResultMessage = ex.GetBaseException().Message;
            TestResultSeverity = InfoBarSeverity.Error;
            IsTestResultOpen = true;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsSelectedBrokerActive))]
    private async Task DisconnectBrokerAsync()
    {
        var name = _mqttClientService.ActiveBroker?.Name ?? SelectedBroker?.Name ?? "broker";
        await _mqttClientService.DisconnectAsync();
        ShowSnackbar("Disconnected", $"Disconnected from {name}.", ControlAppearance.Info);
    }

    private bool CanTestOrConnect() => HasSelectedBroker && !IsTestingConnection;
    private bool CanConnect() => CanConnectSelectedBroker && !IsTestingConnection;

    private async Task<BrokerProfile?> SaveCurrentBrokerCoreAsync(bool showToast)
    {
        if (SelectedBroker is null)
        {
            return null;
        }

        var draft = BuildDraftProfile();
        var errors = MqttOptionsBuilder.Validate(draft);
        if (errors.Count > 0)
        {
            ValidationErrorMessage = string.Join(" ", errors);
            return null;
        }

        ValidationErrorMessage = null;
        draft.ProtectedPassword = _secretProtector.Protect(EditPassword);

        await _profileStore.UpsertAsync(draft);
        await LoadBrokersAsync(draft.Id);

        if (showToast)
        {
            ShowSnackbar("Broker saved", $"Saved \"{draft.Name}\".", ControlAppearance.Success);
        }

        return draft;
    }

    private BrokerProfile BuildDraftProfile()
    {
        return new BrokerProfile
        {
            Id = SelectedBroker?.Id ?? Guid.NewGuid(),
            Name = EditName.Trim(),
            Transport = EditTransport,
            Host = EditHost.Trim(),
            Port = EditPort,
            WebSocketPath = BrokerProfile.NormalizeWebSocketPath(EditWebSocketPath),
            ProtocolVersion = EditProtocolVersion,
            ClientId = string.IsNullOrWhiteSpace(EditClientId) ? BrokerProfile.GenerateClientId() : EditClientId.Trim(),
            CleanSession = EditCleanSession,
            KeepAliveSeconds = EditKeepAliveSeconds,
            ConnectionTimeoutSeconds = EditConnectionTimeoutSeconds,
            AutoReconnect = EditAutoReconnect,
            Username = EditUsername.Trim(),
            ProtectedPassword = SelectedBroker?.ProtectedPassword,
            TlsVersion = EditTlsVersion,
            AllowUntrustedCertificates = EditAllowUntrustedCertificates,
            DefaultTopics = [.. EditDefaultTopics]
        };
    }

    private void LoadEditorFromProfile(BrokerProfile? profile)
    {
        _isLoadingEditor = true;
        try
        {
            ValidationErrorMessage = null;
            IsTestResultOpen = false;
            EditDefaultTopics.Clear();
            NewDefaultTopic = string.Empty;

            if (profile is null)
            {
                EditName = string.Empty;
                EditTransport = BrokerTransportType.Tcp;
                EditHost = string.Empty;
                EditPort = 1883;
                EditWebSocketPath = "/mqtt";
                EditProtocolVersion = MqttProtocolVersionOption.V500;
                EditClientId = string.Empty;
                EditCleanSession = true;
                EditKeepAliveSeconds = 60;
                EditConnectionTimeoutSeconds = 10;
                EditAutoReconnect = true;
                EditUsername = string.Empty;
                EditPassword = string.Empty;
                EditTlsVersion = TlsVersionOption.SystemDefault;
                EditAllowUntrustedCertificates = false;
            }
            else
            {
                EditName = profile.Name;
                EditTransport = profile.Transport;
                EditHost = profile.Host;
                EditPort = profile.Port;
                EditWebSocketPath = BrokerProfile.NormalizeWebSocketPath(profile.WebSocketPath);
                EditProtocolVersion = profile.ProtocolVersion;
                EditClientId = profile.ClientId;
                EditCleanSession = profile.CleanSession;
                EditKeepAliveSeconds = profile.KeepAliveSeconds;
                EditConnectionTimeoutSeconds = profile.ConnectionTimeoutSeconds;
                EditAutoReconnect = profile.AutoReconnect;
                EditUsername = profile.Username;
                EditPassword = _secretProtector.Unprotect(profile.ProtectedPassword);
                EditTlsVersion = profile.TlsVersion;
                EditAllowUntrustedCertificates = profile.AllowUntrustedCertificates;

                foreach (var topic in profile.DefaultTopics)
                {
                    EditDefaultTopics.Add(topic);
                }
            }

            HasUnsavedChanges = false;
        }
        finally
        {
            _isLoadingEditor = false;
        }
    }

    private void ApplyFilter(Guid? preferredSelectionId)
    {
        var query = SearchText?.Trim() ?? string.Empty;
        var matches = string.IsNullOrEmpty(query)
            ? _allBrokers
            : _allBrokers.Where(b =>
                b.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.DisplayUri.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredBrokers.Clear();
        foreach (var broker in matches)
        {
            FilteredBrokers.Add(broker);
        }

        var nextSelection = preferredSelectionId.HasValue
            ? FilteredBrokers.FirstOrDefault(b => b.Id == preferredSelectionId.Value)
            : null;

        SelectedBroker = nextSelection ?? FilteredBrokers.FirstOrDefault();
    }

    private void MarkDirty()
    {
        if (!_isLoadingEditor && SelectedBroker is not null)
        {
            HasUnsavedChanges = true;
            ValidationErrorMessage = null;
        }
    }

    private async Task<bool> ConfirmDeleteAsync(string profileName)
    {
        try
        {
            if (_contentDialogService.GetDialogHostEx() is null)
            {
                return true;
            }

            var result = await _contentDialogService.ShowSimpleDialogAsync(
                new SimpleContentDialogCreateOptions
                {
                    Title = "Delete broker profile?",
                    Content = $"Are you sure you want to permanently remove \"{profileName}\"?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel"
                });

            return result == ContentDialogResult.Primary;
        }
        catch
        {
            return true;
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
}
