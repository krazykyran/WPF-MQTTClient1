using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class SqliteSessionRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;
    private readonly SqliteSessionRepository _repo;

    public SqliteSessionRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_Sqlite_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
        _repo = new SqliteSessionRepository(_paths);
    }

    [Fact]
    public async Task CreateAppendFilterRenameAndDelete_PersistsLosslessly()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        var session = await _repo.CreateSessionAsync(
            "Lab Run 1",
            "Local Mosquitto",
            "mqtt://127.0.0.1:1883",
            "v5.0",
            started);

        var binaryPayload = new byte[] { 0x00, 0xFF, 0x10, 0x7A };
        var jsonPayload = Encoding.UTF8.GetBytes("""{"temp":23.4}""");

        await _repo.AppendMessagesBatchAsync(session.Id, [
            new MqttLogMessage
            {
                TimestampUtc = started.AddSeconds(1),
                Direction = MessageDirection.Inbound,
                Topic = "sensors/kitchen/temp",
                Qos = 1,
                Retain = true,
                Payload = jsonPayload,
                ContentType = "application/json",
                PayloadPreview = """{"temp":23.4}"""
            },
            new MqttLogMessage
            {
                TimestampUtc = started.AddSeconds(2),
                Direction = MessageDirection.Outbound,
                Topic = "devices/relay/raw",
                Qos = 0,
                Retain = false,
                Payload = binaryPayload,
                ContentType = null,
                PayloadPreview = "[Binary 4 B] 00 FF 10 7A"
            }
        ]);

        await _repo.EndSessionAsync(session.Id, started.AddMinutes(1));
        await _repo.RenameSessionAsync(session.Id, "Kitchen Bench Test");

        var sessions = await _repo.GetSessionsAsync("Kitchen");
        var loaded = Assert.Single(sessions);
        Assert.Equal("Kitchen Bench Test", loaded.Name);
        Assert.Equal(2, loaded.MessageCount);
        Assert.NotNull(loaded.EndedAtUtc);

        var topics = await _repo.GetDistinctTopicsForSessionAsync(session.Id);
        Assert.Equal(["devices/relay/raw", "sensors/kitchen/temp"], topics);

        // Direction filter
        var inboundOnly = await _repo.GetSessionMessagesAsync(session.Id, direction: MessageDirectionFilter.Inbound);
        var inbound = Assert.Single(inboundOnly);
        Assert.Equal(jsonPayload, inbound.Payload);
        Assert.True(inbound.Retain);
        Assert.Equal("application/json", inbound.ContentType);

        // Exact binary round-trip via outbound filter
        var outboundOnly = await _repo.GetSessionMessagesAsync(session.Id, direction: MessageDirectionFilter.Outbound);
        var outbound = Assert.Single(outboundOnly);
        Assert.Equal(binaryPayload, outbound.Payload);

        // Search filter
        var searched = await _repo.GetSessionMessagesAsync(session.Id, searchQuery: "23.4");
        Assert.Single(searched);

        // Wildcard topic filter
        var wildcard = await _repo.GetSessionMessagesAsync(session.Id, topicFilter: "sensors/+/temp");
        Assert.Single(wildcard);

        // VACUUM succeeds cleanly
        await _repo.CompactDatabaseAsync();
        Assert.True(File.Exists(_paths.HistoryDatabasePath));

        // Cascade delete removes session and its messages
        await _repo.DeleteSessionAsync(session.Id);
        Assert.Empty(await _repo.GetSessionsAsync());
        Assert.Empty(await _repo.GetSessionMessagesAsync(session.Id));
    }

    [Fact]
    public async Task GetSessionMessagesAsync_SortsByTimestampAndTopicAscAndDesc()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var session = await _repo.CreateSessionAsync("Sort Test", "Broker", "mqtt://localhost:1883", "v5.0", t0);

        // Intentionally insert out-of-timestamp order to prove ORDER BY TimestampUtc (not Id)
        await _repo.AppendMessagesBatchAsync(session.Id, [
            new MqttLogMessage
            {
                TimestampUtc = t0.AddSeconds(20),
                Direction = MessageDirection.Inbound,
                Topic = "zeta/sensor",
                Payload = Encoding.UTF8.GetBytes("2")
            },
            new MqttLogMessage
            {
                TimestampUtc = t0.AddSeconds(10),
                Direction = MessageDirection.Inbound,
                Topic = "Alpha/sensor",
                Payload = Encoding.UTF8.GetBytes("1")
            },
            new MqttLogMessage
            {
                TimestampUtc = t0.AddSeconds(30),
                Direction = MessageDirection.Inbound,
                Topic = "middle/sensor",
                Payload = Encoding.UTF8.GetBytes("3")
            }
        ]);

        var timeAsc = await _repo.GetSessionMessagesAsync(session.Id, sortBy: SessionMessageSortColumn.Timestamp, sortDescending: false);
        Assert.Equal(["Alpha/sensor", "zeta/sensor", "middle/sensor"], timeAsc.Select(m => m.Topic));

        var timeDesc = await _repo.GetSessionMessagesAsync(session.Id, sortBy: SessionMessageSortColumn.Timestamp, sortDescending: true);
        Assert.Equal(["middle/sensor", "zeta/sensor", "Alpha/sensor"], timeDesc.Select(m => m.Topic));

        var topicAsc = await _repo.GetSessionMessagesAsync(session.Id, sortBy: SessionMessageSortColumn.Topic, sortDescending: false);
        Assert.Equal(["Alpha/sensor", "middle/sensor", "zeta/sensor"], topicAsc.Select(m => m.Topic));

        var topicDesc = await _repo.GetSessionMessagesAsync(session.Id, sortBy: SessionMessageSortColumn.Topic, sortDescending: true);
        Assert.Equal(["zeta/sensor", "middle/sensor", "Alpha/sensor"], topicDesc.Select(m => m.Topic));
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
