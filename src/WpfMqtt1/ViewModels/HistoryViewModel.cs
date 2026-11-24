using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    public const string AllTopicsLabel = "All Topics";

    private readonly ISessionRepository _repository;
    private readonly ISessionRecorderService _recorder;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settingsService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _contentDialogService;

    private bool _isLoadingMessages;
    private bool _disposed;

    public ObservableCollection<RecordingSession> Sessions { get; } = [];
    public ObservableCollection<MqttLogMessage> SessionMessages { get; } = [];
    public ObservableCollection<string> TopicFilterOptions { get; } = [AllTopicsLabel];

    public IReadOnlyList<SessionDateFilter> AvailableDateFilters { get; } =
    [
        SessionDateFilter.AllDates,
        SessionDateFilter.Today,
        SessionDateFilter.Last7Days,
        SessionDateFilter.Last30Days
    ];

    public IReadOnlyList<MessageDirectionFilter> AvailableDirectionFilters { get; } =
    [
        MessageDirectionFilter.All,
        MessageDirectionFilter.Inbound,
        MessageDirectionFilter.Outbound
    ];

    [ObservableProperty]
    private string _sessionSearchText = string.Empty;

    [ObservableProperty]
    private SessionDateFilter _selectedDateFilter = SessionDateFilter.AllDates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSession))]
    [NotifyCanExecuteChangedFor(nameof(RenameSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportJsonCommand))]
    private RecordingSession? _selectedSession;

    [ObservableProperty]
    private string _messageSearchText = string.Empty;

    [ObservableProperty]
    private MessageDirectionFilter _messageDirectionFilter = MessageDirectionFilter.All;

    [ObservableProperty]
    private string _selectedTopicFilter = AllTopicsLabel;

    [ObservableProperty]
    private MqttLogMessage? _selectedMessage;

    [ObservableProperty]
    private PayloadFormat _inspectorFormat = PayloadFormat.Text;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _messageCountSummary = string.Empty;

    [ObservableProperty]
    private SessionMessageSortColumn _sortColumn = SessionMessageSortColumn.Timestamp;

    [ObservableProperty]
    private bool _sortDescending;

    public bool HasSelectedSession => SelectedSession is not null;

    public HistoryViewModel(
        ISessionRepository repository,
        ISessionRecorderService recorder,
        IExportService exportService,
        ISettingsService settingsService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        _repository = repository;
        _recorder = recorder;
        _exportService = exportService;
        _settingsService = settingsService;
        _snackbarService = snackbarService;
        _contentDialogService = contentDialogService;

        InspectorFormat = _settingsService.Current.DefaultPayloadFormat;
        _recorder.SessionsUpdated += OnSessionsUpdated;
    }

    public async Task RefreshSessionsAsync(long? selectSessionId = null)
    {
        IsBusy = true;
        try
        {
            var (fromUtc, toUtc) = ComputeDateRange(SelectedDateFilter);
            var sessions = await _repository.GetSessionsAsync(SessionSearchText, fromUtc, toUtc);

            var preferredId = selectSessionId ?? SelectedSession?.Id;

            RunOnUiThread(() =>
            {
                Sessions.Clear();
                foreach (var session in sessions)
                {
                    Sessions.Add(session);
                }

                SelectedSession = (preferredId.HasValue
                    ? Sessions.FirstOrDefault(s => s.Id == preferredId.Value)
                    : null) ?? Sessions.FirstOrDefault();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadSelectedSessionMessagesAsync()
    {
        var session = SelectedSession;
        if (session is null)
        {
            SessionMessages.Clear();
            TopicFilterOptions.Clear();
            TopicFilterOptions.Add(AllTopicsLabel);
            SelectedTopicFilter = AllTopicsLabel;
            SelectedMessage = null;
            MessageCountSummary = string.Empty;
            return;
        }

        _isLoadingMessages = true;
        try
        {
            var topics = await _repository.GetDistinctTopicsForSessionAsync(session.Id);
            var currentTopic = SelectedTopicFilter;

            RunOnUiThread(() =>
            {
                TopicFilterOptions.Clear();
                TopicFilterOptions.Add(AllTopicsLabel);
                foreach (var t in topics)
                {
                    TopicFilterOptions.Add(t);
                }

                SelectedTopicFilter = TopicFilterOptions.Contains(currentTopic) ? currentTopic : AllTopicsLabel;
            });

            var topicParam = string.Equals(SelectedTopicFilter, AllTopicsLabel, StringComparison.Ordinal)
                ? null
                : SelectedTopicFilter;

            var messages = await _repository.GetSessionMessagesAsync(
                session.Id,
                MessageSearchText,
                MessageDirectionFilter,
                topicParam,
                sortBy: SortColumn,
                sortDescending: SortDescending);

            RunOnUiThread(() =>
            {
                var previousSeq = SelectedMessage?.SequenceId;
                SessionMessages.Clear();
                foreach (var msg in messages)
                {
                    SessionMessages.Add(msg);
                }

                SelectedMessage = previousSeq.HasValue
                    ? SessionMessages.FirstOrDefault(m => m.SequenceId == previousSeq.Value)
                    : SessionMessages.FirstOrDefault();

                MessageCountSummary = messages.Count == session.MessageCount
                    ? $"{messages.Count:N0} messages"
                    : $"Showing {messages.Count:N0} of {session.MessageCount:N0} messages";
            });
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    partial void OnSessionSearchTextChanged(string value) => _ = RefreshSessionsAsync();
    partial void OnSelectedDateFilterChanged(SessionDateFilter value) => _ = RefreshSessionsAsync();

    partial void OnSelectedSessionChanged(RecordingSession? value)
    {
        MessageSearchText = string.Empty;
        MessageDirectionFilter = MessageDirectionFilter.All;
        SelectedTopicFilter = AllTopicsLabel;
        _ = LoadSelectedSessionMessagesAsync();
    }

    partial void OnMessageSearchTextChanged(string value)
    {
        if (!_isLoadingMessages)
        {
            _ = LoadSelectedSessionMessagesAsync();
        }
    }

    partial void OnMessageDirectionFilterChanged(MessageDirectionFilter value)
    {
        if (!_isLoadingMessages)
        {
            _ = LoadSelectedSessionMessagesAsync();
        }
    }

    partial void OnSelectedTopicFilterChanged(string value)
    {
        if (!_isLoadingMessages)
        {
            _ = LoadSelectedSessionMessagesAsync();
        }
    }

    public async Task SetSortAsync(SessionMessageSortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        await LoadSelectedSessionMessagesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshSessionsAsync();
        await LoadSelectedSessionMessagesAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task RenameSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var session = SelectedSession;
        var newName = await PromptForSessionNameAsync(session.Name);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName.Trim(), session.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RenameSessionCoreAsync(session.Id, newName.Trim());
    }

    public async Task RenameSessionCoreAsync(long sessionId, string newName)
    {
        await _repository.RenameSessionAsync(sessionId, newName);
        await RefreshSessionsAsync(sessionId);
        ShowSnackbar("Session renamed", $"Renamed session to \"{newName}\".", ControlAppearance.Success);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task DeleteSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var session = SelectedSession;
        if (!await ConfirmDeleteAsync(session.Name))
        {
            return;
        }

        await _repository.DeleteSessionAsync(session.Id);
        await RefreshSessionsAsync();
        ShowSnackbar("Session deleted", $"Removed \"{session.Name}\".", ControlAppearance.Info);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task ExportCsvAsync()
    {
        if (SelectedSession is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Session to CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = BuildSafeExportFileName(SelectedSession, ".csv")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ExportSessionToFileAsync(SelectedSession, dialog.FileName, isJson: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task ExportJsonAsync()
    {
        if (SelectedSession is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Session to JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = BuildSafeExportFileName(SelectedSession, ".json")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ExportSessionToFileAsync(SelectedSession, dialog.FileName, isJson: true);
    }

    public async Task ExportSessionToFileAsync(RecordingSession session, string filePath, bool isJson)
    {
        try
        {
            var messages = await _repository.GetSessionMessagesAsync(
                session.Id,
                limit: 100_000,
                sortBy: SortColumn,
                sortDescending: SortDescending);
            if (isJson)
            {
                await _exportService.ExportToJsonAsync(filePath, session, messages);
            }
            else
            {
                await _exportService.ExportToCsvAsync(filePath, session, messages);
            }

            ShowSnackbar(
                "Export complete",
                $"Exported {messages.Count:N0} messages to {Path.GetFileName(filePath)}.",
                ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            ShowSnackbar("Export failed", ex.GetBaseException().Message, ControlAppearance.Danger);
        }
    }

    private void OnSessionsUpdated(object? sender, EventArgs e)
    {
        _ = RefreshSessionsAsync();
    }

    private async Task<string?> PromptForSessionNameAsync(string currentName)
    {
        try
        {
            if (_contentDialogService.GetDialogHostEx() is null)
            {
                return null;
            }

            var inputBox = new Wpf.Ui.Controls.TextBox
            {
                Text = currentName,
                PlaceholderText = "Enter session name..."
            };

            var dialog = new ContentDialog
            {
                Title = "Rename session",
                Content = inputBox,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
            return result == ContentDialogResult.Primary ? inputBox.Text : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ConfirmDeleteAsync(string sessionName)
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
                    Title = "Delete recorded session?",
                    Content = $"Permanently delete \"{sessionName}\" and all of its logged messages from SQLite?",
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

    private static (DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) ComputeDateRange(SessionDateFilter filter)
    {
        var nowLocal = DateTimeOffset.Now;
        return filter switch
        {
            SessionDateFilter.Today => (new DateTimeOffset(nowLocal.Date, nowLocal.Offset).ToUniversalTime(), null),
            SessionDateFilter.Last7Days => (nowLocal.AddDays(-7).ToUniversalTime(), null),
            SessionDateFilter.Last30Days => (nowLocal.AddDays(-30).ToUniversalTime(), null),
            _ => (null, null)
        };
    }

    private static string BuildSafeExportFileName(RecordingSession session, string extension)
    {
        var raw = $"{session.Name}_{session.StartedAtUtc.ToLocalTime():yyyyMMdd_HHmm}";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return cleaned + extension;
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
        if (_disposed) return;
        _disposed = true;
        _recorder.SessionsUpdated -= OnSessionsUpdated;
    }
}
