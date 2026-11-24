using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class ExportService : IExportService
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonWriterOptions JsonOptions = new() { Indented = true };

    public async Task ExportToCsvAsync(
        string filePath,
        RecordingSession session,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);

        var sb = new StringBuilder(messages.Count * 128 + 128);
        sb.AppendLine("TimestampUtc,TimestampLocal,Direction,Topic,QoS,Retain,ByteCount,ContentType,PayloadUtf8,PayloadBase64");

        foreach (var msg in messages)
        {
            var payload = msg.Payload ?? [];
            var utf8Text = TryDecodeUtf8(payload, out var text) ? text : string.Empty;
            var base64 = payload.Length > 0 ? Convert.ToBase64String(payload) : string.Empty;

            sb.Append(EscapeCsv(msg.TimestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(msg.TimestampFullDisplay)).Append(',');
            sb.Append(EscapeCsv(msg.DirectionLabel)).Append(',');
            sb.Append(EscapeCsv(msg.Topic)).Append(',');
            sb.Append(msg.Qos.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(msg.Retain ? "true" : "false").Append(',');
            sb.Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsv(msg.ContentType ?? string.Empty)).Append(',');
            sb.Append(EscapeCsv(utf8Text)).Append(',');
            sb.AppendLine(EscapeCsv(base64));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ExportToJsonAsync(
        string filePath,
        RecordingSession session,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);

        await using var stream = File.Create(filePath);
        await using var writer = new Utf8JsonWriter(stream, JsonOptions);

        writer.WriteStartObject();

        writer.WritePropertyName("session");
        writer.WriteStartObject();
        writer.WriteNumber("id", session.Id);
        writer.WriteString("name", session.Name);
        writer.WriteString("brokerName", session.BrokerName);
        writer.WriteString("brokerAddress", session.BrokerAddress);
        writer.WriteString("mqttVersion", session.MqttVersion);
        writer.WriteString("startedAtUtc", session.StartedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        if (session.EndedAtUtc.HasValue)
        {
            writer.WriteString("endedAtUtc", session.EndedAtUtc.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("endedAtUtc");
        }
        writer.WriteNumber("messageCount", messages.Count);
        writer.WriteEndObject();

        writer.WritePropertyName("messages");
        writer.WriteStartArray();

        foreach (var msg in messages)
        {
            var payload = msg.Payload ?? [];
            writer.WriteStartObject();
            writer.WriteString("timestampUtc", msg.TimestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("direction", msg.DirectionLabel);
            writer.WriteString("topic", msg.Topic);
            writer.WriteNumber("qos", msg.Qos);
            writer.WriteBoolean("retain", msg.Retain);
            writer.WriteNumber("byteCount", payload.Length);

            if (!string.IsNullOrEmpty(msg.ContentType))
            {
                writer.WriteString("contentType", msg.ContentType);
            }

            if (TryDecodeUtf8(payload, out var utf8Text))
            {
                writer.WriteString("payloadText", utf8Text);
                if (TryWriteInlineJson(writer, payload))
                {
                    // Written as "payloadJson".
                }
            }

            writer.WriteString("payloadBase64", Convert.ToBase64String(payload));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        if (bytes.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        try
        {
            text = StrictUtf8.GetString(bytes);
            foreach (var ch in text)
            {
                if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
                {
                    text = string.Empty;
                    return false;
                }
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool TryWriteInlineJson(Utf8JsonWriter writer, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            writer.WritePropertyName("payloadJson");
            doc.RootElement.WriteTo(writer);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
