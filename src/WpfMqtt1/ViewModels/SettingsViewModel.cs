using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IAppPathsService _pathsService;
    private readonly ISnackbarService _snackbarService;
    private readonly ISessionRepository? _sessionRepository;

    private bool _isSyncing;
    private Task _pendingSaveTask = Task.CompletedTask;

    internal Task WaitForPendingSaveAsync() => _pendingSaveTask;

    [ObservableProperty]
    private AppThemePreference _selectedTheme;

    [ObservableProperty]
    private WindowBackdropPreference _selectedBackdrop;

    [ObservableProperty]
    private bool _autoRecordOnConnect;

    [ObservableProperty]
    private int _maxInMemoryMessages = 5000;

    [ObservableProperty]
    private int _defaultPublishQos;

    [ObservableProperty]
    private PayloadFormat _defaultPayloadFormat = PayloadFormat.Text;

    [ObservableProperty]
    private string _databaseSizeDisplay = "Not created yet";

    public IReadOnlyList<AppThemePreference> AvailableThemes { get; } =
    [
        AppThemePreference.System,
        AppThemePreference.Light,
        AppThemePreference.Dark
    ];

    public IReadOnlyList<WindowBackdropPreference> AvailableBackdrops { get; } =
    [
        WindowBackdropPreference.Mica,
        WindowBackdropPreference.Acrylic,
        WindowBackdropPreference.None
    ];

    public IReadOnlyList<int> AvailableQosLevels { get; } = [0, 1, 2];

    public IReadOnlyList<PayloadFormat> AvailablePayloadFormats { get; } =
    [
        PayloadFormat.Text,
        PayloadFormat.Json,
        PayloadFormat.Hex,
        PayloadFormat.Base64
    ];

    public IReadOnlyList<int> AvailableBufferLimits { get; } = [1000, 2500, 5000, 10000, 25000, 50000];

    public string AppDataDirectory => _pathsService.AppDataDirectory;
    public string HistoryDatabasePath => _pathsService.HistoryDatabasePath;
    public string AppVersion { get; }

    public SettingsViewModel(
        IAppThemeService themeService,
        ISettingsService settingsService,
        IAppPathsService pathsService,
        ISnackbarService snackbarService,
        ISessionRepository? sessionRepository = null)
    {
        _themeService = themeService;
        _settingsService = settingsService;
        _pathsService = pathsService;
        _snackbarService = snackbarService;
        _sessionRepository = sessionRepository;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = version is not null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.1.0";

        _settingsService.SettingsChanged += (_, _) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(SyncFromSettings);
            }
            else
            {
                SyncFromSettings();
            }
        };
        SyncFromSettings();
        RefreshStorageInfo();
    }

    public void SyncFromSettings()
    {
        _isSyncing = true;
        try
        {
            var s = _settingsService.Current;
            SelectedTheme = s.Theme;
            SelectedBackdrop = s.Backdrop;
            AutoRecordOnConnect = s.AutoRecordOnConnect;
            MaxInMemoryMessages = s.MaxInMemoryMessages;
            DefaultPublishQos = s.DefaultPublishQos;
            DefaultPayloadFormat = s.DefaultPayloadFormat;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public void RefreshStorageInfo()
    {
        if (File.Exists(_pathsService.HistoryDatabasePath))
        {
            var bytes = new FileInfo(_pathsService.HistoryDatabasePath).Length;
            DatabaseSizeDisplay = FormatBytes(bytes);
        }
        else
        {
            DatabaseSizeDisplay = "Not created yet";
        }
    }

    partial void OnSelectedThemeChanged(AppThemePreference value)
    {
        if (_isSyncing) return;
        _settingsService.Current.Theme = value;
        _themeService.ApplyTheme(value);
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    partial void OnSelectedBackdropChanged(WindowBackdropPreference value)
    {
        if (_isSyncing) return;
        _settingsService.Current.Backdrop = value;
        _themeService.ApplyBackdrop(value);
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    partial void OnAutoRecordOnConnectChanged(bool value)
    {
        if (_isSyncing) return;
        _settingsService.Current.AutoRecordOnConnect = value;
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    partial void OnMaxInMemoryMessagesChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.MaxInMemoryMessages = value;
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    partial void OnDefaultPublishQosChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultPublishQos = value;
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    partial void OnDefaultPayloadFormatChanged(PayloadFormat value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultPayloadFormat = value;
        _pendingSaveTask = PersistSettingsSafeAsync();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            _pathsService.EnsureAppDataDirectoryExists();
            Process.Start(new ProcessStartInfo
            {
                FileName = _pathsService.AppDataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowSnackbar("Could not open folder", ex.Message, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    private async Task CompactDatabaseAsync()
    {
        if (_sessionRepository is null)
        {
            return;
        }

        try
        {
            await _sessionRepository.CompactDatabaseAsync();
            RefreshStorageInfo();
            ShowSnackbar("Database compacted", $"SQLite VACUUM complete ({DatabaseSizeDisplay}).", ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            ShowSnackbar("Compact failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
    }

    private async Task PersistSettingsSafeAsync()
    {
        try
        {
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            ShowSnackbar("Settings save failed", ex.Message, ControlAppearance.Danger);
        }
    }

    private void ShowSnackbar(string title, string message, ControlAppearance appearance)
    {
        try
        {
            _snackbarService.Show(
                title,
                message,
                appearance,
                new SymbolIcon(SymbolRegular.Info24),
                TimeSpan.FromSeconds(4));
        }
        catch
        {
            // Ignore when no snackbar presenter is attached (e.g., unit tests).
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
