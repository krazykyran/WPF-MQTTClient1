using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class SessionRecorderAndExportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;
    private readonly SqliteSessionRepository _repo;
    private readonly ExportService _exportService = new();

    public SessionRecorderAndExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_RecExp_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
        _repo = new SqliteSessionRepository(_paths);
    }

    [Fact]
    public async Task SessionRecorder_BatchesAndFlushesAllMessagesOnStop()
    {
        await using var recorder = new SessionRecorderService(_repo);
        var broker = new BrokerProfile { Name = "Broker A", Host = "127.0.0.1", Port = 1883 };

        var session = await recorder.StartRecordingAsync(broker, "Burst Capture");
        Assert.True(recorder.IsRecording);

        for (var i = 0; i < 75; i++)
        {
            recorder.EnqueueMessage(new MqttLogMessage
            {
                SequenceId = i + 1,
                TimestampUtc = DateTimeOffset.UtcNow,
                Direction = i % 2 == 0 ? MessageDirection.Inbound : MessageDirection.Outbound,
                Topic = $"burst/item/{i}",
                Qos = i % 3,
                Retain = false,
                Payload = Encoding.UTF8.GetBytes($"value-{i}"),
                PayloadPreview = $"value-{i}"
            });
        }

        await recorder.StopRecordingAsync();
        Assert.False(recorder.IsRecording);
        Assert.Equal(75, recorder.RecordedMessageCount);

        var stored = await _repo.GetSessionMessagesAsync(session.Id);
        Assert.Equal(75, stored.Count);
        Assert.Equal("burst/item/0", stored[0].Topic);
        Assert.Equal("burst/item/74", stored[74].Topic);
    }

    [Fact]
    public async Task ExportService_WritesValidCsvAndJsonWithLosslessPayloads()
    {
        Directory.CreateDirectory(_tempDir);

        var session = new RecordingSession
        {
            Id = 42,
            Name = "Export Session",
            BrokerName = "Test Broker",
            BrokerAddress = "mqtt://localhost:1883",
            MqttVersion = "v5.0",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndedAtUtc = DateTimeOffset.UtcNow,
            MessageCount = 2
        };

        var binary = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var messages = new List<MqttLogMessage>
        {
            new()
            {
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
                Direction = MessageDirection.Inbound,
                Topic = "home/sensor",
                Qos = 1,
                Retain = true,
                Payload = Encoding.UTF8.GetBytes("""{"note":"hello, \"world\""}"""),
                ContentType = "application/json",
                PayloadPreview = """{"note":"hello, \"world\""}"""
            },
            new()
            {
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                Direction = MessageDirection.Outbound,
                Topic = "home/raw",
                Qos = 0,
                Retain = false,
                Payload = binary,
                PayloadPreview = "[Binary 4 B] DE AD BE EF"
            }
        };

        var csvPath = Path.Combine(_tempDir, "session.csv");
        await _exportService.ExportToCsvAsync(csvPath, session, messages);
        var csv = await File.ReadAllTextAsync(csvPath);
        Assert.Contains("TimestampUtc,TimestampLocal,Direction,Topic,QoS,Retain,ByteCount,ContentType,PayloadUtf8,PayloadBase64", csv);
        Assert.Contains("home/sensor", csv);
        Assert.Contains(Convert.ToBase64String(binary), csv);

        var jsonPath = Path.Combine(_tempDir, "session.json");
        await _exportService.ExportToJsonAsync(jsonPath, session, messages);
        var jsonText = await File.ReadAllTextAsync(jsonPath);

        using var doc = JsonDocument.Parse(jsonText);
        Assert.Equal("Export Session", doc.RootElement.GetProperty("session").GetProperty("name").GetString());
        var msgArray = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, msgArray.GetArrayLength());
        Assert.Equal("hello, \"world\"", msgArray[0].GetProperty("payloadJson").GetProperty("note").GetString());
        Assert.Equal(Convert.ToBase64String(binary), msgArray[1].GetProperty("payloadBase64").GetString());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
