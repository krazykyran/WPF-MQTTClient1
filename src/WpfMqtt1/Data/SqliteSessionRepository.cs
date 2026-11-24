using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Data;

public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly IAppPathsService _pathsService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteSessionRepository(IAppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _pathsService.HistoryDatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true
    }.ToString();

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized && File.Exists(_pathsService.HistoryDatabasePath))
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && File.Exists(_pathsService.HistoryDatabasePath))
            {
                return;
            }

            _pathsService.EnsureAppDataDirectoryExists();

            await using var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS Sessions (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name            TEXT    NOT NULL,
                    BrokerName      TEXT    NOT NULL,
                    BrokerAddress   TEXT    NOT NULL,
                    MqttVersion     TEXT    NOT NULL,
                    StartedAtUtc    TEXT    NOT NULL,
                    EndedAtUtc      TEXT    NULL,
                    MessageCount    INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Messages (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId       INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                    TimestampUtc    TEXT    NOT NULL,
                    Direction       INTEGER NOT NULL,
                    Topic           TEXT    NOT NULL,
                    Qos             INTEGER NOT NULL,
                    Retain          INTEGER NOT NULL,
                    PayloadBytes    BLOB    NOT NULL,
                    PayloadFormat   TEXT    NULL,
                    PayloadPreview  TEXT    NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Messages_Session_Time
                    ON Messages(SessionId, TimestampUtc);

                CREATE INDEX IF NOT EXISTS IX_Messages_Session_Topic
                    ON Messages(SessionId, Topic);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<RecordingSession> CreateSessionAsync(
        string name,
        string brokerName,
        string brokerAddress,
        string mqttVersion,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sessions (Name, BrokerName, BrokerAddress, MqttVersion, StartedAtUtc, EndedAtUtc, MessageCount)
            VALUES ($name, $brokerName, $brokerAddress, $mqttVersion, $startedAtUtc, NULL, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$brokerName", brokerName);
        cmd.Parameters.AddWithValue("$brokerAddress", brokerAddress);
        cmd.Parameters.AddWithValue("$mqttVersion", mqttVersion);
        cmd.Parameters.AddWithValue("$startedAtUtc", ToIsoUtc(startedAtUtc));

        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        return new RecordingSession
        {
            Id = id,
            Name = name,
            BrokerName = brokerName,
            BrokerAddress = brokerAddress,
            MqttVersion = mqttVersion,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = null,
            MessageCount = 0
        };
    }

    public async Task AppendMessagesBatchAsync(
        long sessionId,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO Messages
                    (SessionId, TimestampUtc, Direction, Topic, Qos, Retain, PayloadBytes, PayloadFormat, PayloadPreview)
                VALUES
                    ($sessionId, $timestampUtc, $direction, $topic, $qos, $retain, $payloadBytes, $payloadFormat, $payloadPreview);
                """;

            var pSessionId = insertCmd.Parameters.Add("$sessionId", SqliteType.Integer);
            var pTimestamp = insertCmd.Parameters.Add("$timestampUtc", SqliteType.Text);
            var pDirection = insertCmd.Parameters.Add("$direction", SqliteType.Integer);
            var pTopic = insertCmd.Parameters.Add("$topic", SqliteType.Text);
            var pQos = insertCmd.Parameters.Add("$qos", SqliteType.Integer);
            var pRetain = insertCmd.Parameters.Add("$retain", SqliteType.Integer);
            var pPayload = insertCmd.Parameters.Add("$payloadBytes", SqliteType.Blob);
            var pFormat = insertCmd.Parameters.Add("$payloadFormat", SqliteType.Text);
            var pPreview = insertCmd.Parameters.Add("$payloadPreview", SqliteType.Text);

            pSessionId.Value = sessionId;

            foreach (var msg in messages)
            {
                pTimestamp.Value = ToIsoUtc(msg.TimestampUtc);
                pDirection.Value = (int)msg.Direction;
                pTopic.Value = msg.Topic;
                pQos.Value = msg.Qos;
                pRetain.Value = msg.Retain ? 1 : 0;
                pPayload.Value = msg.Payload ?? [];
                pFormat.Value = (object?)msg.ContentType ?? DBNull.Value;
                pPreview.Value = msg.PayloadPreview ?? string.Empty;

                await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE Sessions
                SET MessageCount = MessageCount + $delta
                WHERE Id = $sessionId;
                """;
            updateCmd.Parameters.AddWithValue("$delta", messages.Count);
            updateCmd.Parameters.AddWithValue("$sessionId", sessionId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task EndSessionAsync(
        long sessionId,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Sessions
            SET EndedAtUtc = $endedAtUtc
            WHERE Id = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$endedAtUtc", ToIsoUtc(endedAtUtc));
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameSessionAsync(
        long sessionId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Sessions
            SET Name = $name
            WHERE Id = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$name", newName.Trim());
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_pathsService.HistoryDatabasePath))
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Sessions WHERE Id = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecordingSession>> GetSessionsAsync(
        string? searchQuery = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_pathsService.HistoryDatabasePath))
        {
            return [];
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = new StringBuilder("""
            SELECT Id, Name, BrokerName, BrokerAddress, MqttVersion, StartedAtUtc, EndedAtUtc, MessageCount
            FROM Sessions
            WHERE 1 = 1
            """);

        await using var cmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            sql.Append(" AND (Name LIKE $q OR BrokerName LIKE $q OR BrokerAddress LIKE $q)");
            cmd.Parameters.AddWithValue("$q", $"%{searchQuery.Trim()}%");
        }

        if (fromUtc.HasValue)
        {
            sql.Append(" AND StartedAtUtc >= $fromUtc");
            cmd.Parameters.AddWithValue("$fromUtc", ToIsoUtc(fromUtc.Value));
        }

        if (toUtc.HasValue)
        {
            sql.Append(" AND StartedAtUtc <= $toUtc");
            cmd.Parameters.AddWithValue("$toUtc", ToIsoUtc(toUtc.Value));
        }

        sql.Append(" ORDER BY StartedAtUtc DESC, Id DESC;");
        cmd.CommandText = sql.ToString();

        var results = new List<RecordingSession>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new RecordingSession
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                BrokerName = reader.GetString(2),
                BrokerAddress = reader.GetString(3),
                MqttVersion = reader.GetString(4),
                StartedAtUtc = ParseIsoUtc(reader.GetString(5)),
                EndedAtUtc = reader.IsDBNull(6) ? null : ParseIsoUtc(reader.GetString(6)),
                MessageCount = reader.GetInt64(7)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<MqttLogMessage>> GetSessionMessagesAsync(
        long sessionId,
        string? searchQuery = null,
        MessageDirectionFilter direction = MessageDirectionFilter.All,
        string? topicFilter = null,
        int limit = 10_000,
        int offset = 0,
        SessionMessageSortColumn sortBy = SessionMessageSortColumn.Timestamp,
        bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_pathsService.HistoryDatabasePath))
        {
            return [];
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = new StringBuilder("""
            SELECT Id, TimestampUtc, Direction, Topic, Qos, Retain, PayloadBytes, PayloadFormat, PayloadPreview
            FROM Messages
            WHERE SessionId = $sessionId
            """);

        await using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        if (direction == MessageDirectionFilter.Inbound)
        {
            sql.Append(" AND Direction = 0");
        }
        else if (direction == MessageDirectionFilter.Outbound)
        {
            sql.Append(" AND Direction = 1");
        }

        var useWildcardTopic = !string.IsNullOrWhiteSpace(topicFilter) &&
                               (topicFilter.Contains('#') || topicFilter.Contains('+'));

        if (!string.IsNullOrWhiteSpace(topicFilter) && !useWildcardTopic)
        {
            sql.Append(" AND Topic = $topic");
            cmd.Parameters.AddWithValue("$topic", topicFilter.Trim());
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            sql.Append(" AND (Topic LIKE $q OR PayloadPreview LIKE $q)");
            cmd.Parameters.AddWithValue("$q", $"%{searchQuery.Trim()}%");
        }

        var orderBy = (sortBy, sortDescending) switch
        {
            (SessionMessageSortColumn.Topic, false) => " ORDER BY Topic COLLATE NOCASE ASC, TimestampUtc ASC, Id ASC",
            (SessionMessageSortColumn.Topic, true) => " ORDER BY Topic COLLATE NOCASE DESC, TimestampUtc DESC, Id DESC",
            (SessionMessageSortColumn.Timestamp, true) => " ORDER BY TimestampUtc DESC, Id DESC",
            _ => " ORDER BY TimestampUtc ASC, Id ASC"
        };

        sql.Append(orderBy);
        sql.Append(" LIMIT $limit OFFSET $offset;");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100_000));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));

        var results = new List<MqttLogMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var topic = reader.GetString(3);
            if (useWildcardTopic && !MqttTopicMatcher.IsMatch(topicFilter!, topic))
            {
                continue;
            }

            var msgDirection = (MessageDirection)reader.GetInt32(2);
            var payload = reader.IsDBNull(6) ? [] : (byte[])reader.GetValue(6);

            results.Add(new MqttLogMessage
            {
                SequenceId = reader.GetInt64(0),
                TimestampUtc = ParseIsoUtc(reader.GetString(1)),
                Direction = msgDirection,
                Topic = topic,
                Qos = reader.GetInt32(4),
                Retain = reader.GetInt32(5) != 0,
                Payload = payload,
                ContentType = reader.IsDBNull(7) ? null : reader.GetString(7),
                PayloadPreview = reader.GetString(8),
                AccentColorHex = msgDirection == MessageDirection.Inbound ? "#22C55E" : "#3B82F6"
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetDistinctTopicsForSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_pathsService.HistoryDatabasePath))
        {
            return [];
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT Topic
            FROM Messages
            WHERE SessionId = $sessionId
            ORDER BY Topic ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        var topics = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            topics.Add(reader.GetString(0));
        }

        return topics;
    }

    public async Task CompactDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_pathsService.HistoryDatabasePath))
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ToIsoUtc(DateTimeOffset dto) =>
        dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseIsoUtc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
