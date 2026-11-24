using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Wpf.Ui;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Tests;

public sealed class HistoryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;
    private readonly SqliteSessionRepository _repo;
    private readonly SessionRecorderService _recorder;
    private readonly ExportService _exportService;
    private readonly SettingsService _settings;
    private readonly HistoryViewModel _vm;

    public HistoryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_HistoryVm_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
        _repo = new SqliteSessionRepository(_paths);
        _recorder = new SessionRecorderService(_repo);
        _exportService = new ExportService();
        _settings = new SettingsService(_paths, new AppThemeService());

        _vm = new HistoryViewModel(
            _repo,
            _recorder,
            _exportService,
            _settings,
            new SnackbarService(),
            new ContentDialogService());
    }

    [Fact]
    public async Task OfflineSessionBrowsing_LoadsFiltersRenamesExportsAndDeletes()
    {
        var session = await _repo.CreateSessionAsync(
            "Field Capture",
            "Factory Broker",
            "mqtts://factory.local:8883",
            "v5.0",
            DateTimeOffset.UtcNow);

        await _repo.AppendMessagesBatchAsync(session.Id, [
            new MqttLogMessage
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Direction = MessageDirection.Inbound,
                Topic = "line1/motor/rpm",
                Qos = 0,
                Payload = Encoding.UTF8.GetBytes("1450"),
                PayloadPreview = "1450"
            },
            new MqttLogMessage
            {
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(1),
                Direction = MessageDirection.Outbound,
                Topic = "line1/motor/cmd",
                Qos = 1,
                Payload = Encoding.UTF8.GetBytes("STOP"),
                PayloadPreview = "STOP"
            }
        ]);
        await _repo.EndSessionAsync(session.Id, DateTimeOffset.UtcNow.AddSeconds(10));

        await _vm.RefreshSessionsAsync();
        Assert.Single(_vm.Sessions);
        Assert.Equal(session.Id, _vm.SelectedSession?.Id);

        await _vm.LoadSelectedSessionMessagesAsync();
        Assert.Equal(2, _vm.SessionMessages.Count);
        Assert.Equal(SessionMessageSortColumn.Timestamp, _vm.SortColumn);
        Assert.False(_vm.SortDescending);
        Assert.Equal(["line1/motor/rpm", "line1/motor/cmd"], _vm.SessionMessages.Select(m => m.Topic));
        Assert.Contains("line1/motor/rpm", _vm.TopicFilterOptions);
        Assert.Contains("line1/motor/cmd", _vm.TopicFilterOptions);

        // Sort by Timestamp DESC
        await _vm.SetSortAsync(SessionMessageSortColumn.Timestamp, descending: true);
        Assert.Equal(["line1/motor/cmd", "line1/motor/rpm"], _vm.SessionMessages.Select(m => m.Topic));

        // Sort by Topic ASC / DESC
        await _vm.SetSortAsync(SessionMessageSortColumn.Topic, descending: false);
        Assert.Equal(["line1/motor/cmd", "line1/motor/rpm"], _vm.SessionMessages.Select(m => m.Topic));

        await _vm.SetSortAsync(SessionMessageSortColumn.Topic, descending: true);
        Assert.Equal(["line1/motor/rpm", "line1/motor/cmd"], _vm.SessionMessages.Select(m => m.Topic));

        // Rename session
        await _vm.RenameSessionCoreAsync(session.Id, "Motor Line 1 Audit");
        Assert.Equal("Motor Line 1 Audit", _vm.SelectedSession?.Name);

        // Export JSON offline
        var jsonPath = Path.Combine(_tempDir, "audit.json");
        await _vm.ExportSessionToFileAsync(_vm.SelectedSession!, jsonPath, isJson: true);
        Assert.True(File.Exists(jsonPath));
        Assert.Contains("Motor Line 1 Audit", await File.ReadAllTextAsync(jsonPath));

        // Delete session
        await _vm.DeleteSessionCommand.ExecuteAsync(null);
        Assert.Empty(_vm.Sessions);
        Assert.Null(_vm.SelectedSession);
    }

    public void Dispose()
    {
        _vm.Dispose();
        _recorder.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
