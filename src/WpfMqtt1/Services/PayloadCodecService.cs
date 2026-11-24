using System.Globalization;
using System.Text;
using System.Text.Json;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class PayloadCodecService : IPayloadCodecService
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonWriterOptions PrettyJsonOptions = new() { Indented = true };

    public PayloadFormatResult FormatPayload(byte[] payload, PayloadFormat format)
    {
        payload ??= [];

        return format switch
        {
            PayloadFormat.Text => FormatText(payload),
            PayloadFormat.Json => FormatJson(payload),
            PayloadFormat.Hex => new PayloadFormatResult(FormatHexDump(payload), true),
            PayloadFormat.Base64 => new PayloadFormatResult(FormatBase64(payload), true),
            _ => FormatText(payload)
        };
    }

    public string CreatePreview(byte[] payload, int maxChars = 160)
    {
        if (payload is null || payload.Length == 0)
        {
            return "(empty)";
        }

        if (IsLikelyUtf8Text(payload, out var text))
        {
            var sb = new StringBuilder(Math.Min(text.Length, maxChars + 4));
            var lastWasSpace = false;

            foreach (var ch in text)
            {
                if (sb.Length >= maxChars)
                {
                    sb.Append('…');
                    break;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    lastWasSpace = false;
                }
            }

            return sb.Length == 0 ? "(whitespace)" : sb.ToString().TrimEnd();
        }

        var take = Math.Min(payload.Length, 24);
        var hex = new StringBuilder($"[Binary {payload.Length:N0} B] ");
        for (var i = 0; i < take; i++)
        {
            if (i > 0) hex.Append(' ');
            hex.Append(payload[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        if (payload.Length > take)
        {
            hex.Append(" …");
        }

        return hex.ToString();
    }

    public bool TryEncodePayload(string? text, PayloadFormat format, out byte[] bytes, out string? errorMessage)
    {
        text ??= string.Empty;

        switch (format)
        {
            case PayloadFormat.Text:
                bytes = Encoding.UTF8.GetBytes(text);
                errorMessage = null;
                return true;

            case PayloadFormat.Json:
                if (string.IsNullOrWhiteSpace(text))
                {
                    bytes = [];
                    errorMessage = "JSON payload cannot be empty.";
                    return false;
                }

                try
                {
                    using var _ = JsonDocument.Parse(text);
                    bytes = Encoding.UTF8.GetBytes(text);
                    errorMessage = null;
                    return true;
                }
                catch (JsonException ex)
                {
                    bytes = [];
                    errorMessage = $"Invalid JSON: {ex.Message}";
                    return false;
                }

            case PayloadFormat.Hex:
                return TryParseHex(text, out bytes, out errorMessage);

            case PayloadFormat.Base64:
                return TryParseBase64(text, out bytes, out errorMessage);

            default:
                bytes = Encoding.UTF8.GetBytes(text);
                errorMessage = null;
                return true;
        }
    }

    public bool TryFormatJson(string? text, out string formatted, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            formatted = string.Empty;
            errorMessage = "Enter JSON text to format.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, PrettyJsonOptions))
            {
                doc.WriteTo(writer);
            }

            formatted = Encoding.UTF8.GetString(stream.ToArray());
            errorMessage = null;
            return true;
        }
        catch (JsonException ex)
        {
            formatted = text;
            errorMessage = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static PayloadFormatResult FormatText(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return new PayloadFormatResult(string.Empty, true);
        }

        if (IsLikelyUtf8Text(payload, out var text))
        {
            return new PayloadFormatResult(text, true);
        }

        var lossy = Encoding.UTF8.GetString(payload);
        return new PayloadFormatResult(
            lossy,
            false,
            "Payload contains non-UTF-8 or control bytes. Switch to Hex or Base64 for exact inspection.");
    }

    private static PayloadFormatResult FormatJson(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return new PayloadFormatResult(string.Empty, false, "Payload is empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, PrettyJsonOptions))
            {
                doc.WriteTo(writer);
            }

            return new PayloadFormatResult(Encoding.UTF8.GetString(stream.ToArray()), true);
        }
        catch (JsonException)
        {
            var raw = Encoding.UTF8.GetString(payload);
            return new PayloadFormatResult(
                raw,
                false,
                "Payload is not valid JSON — showing raw UTF-8 text.");
        }
    }

    private static string FormatHexDump(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return "(empty — 0 bytes)";
        }

        const int bytesPerRow = 16;
        var sb = new StringBuilder(payload.Length * 4);

        for (var offset = 0; offset < payload.Length; offset += bytesPerRow)
        {
            if (offset > 0)
            {
                sb.AppendLine();
            }

            sb.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            sb.Append("  ");

            for (var i = 0; i < bytesPerRow; i++)
            {
                if (i == 8)
                {
                    sb.Append(' ');
                }

                if (offset + i < payload.Length)
                {
                    sb.Append(payload[offset + i].ToString("X2", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(" |");
            for (var i = 0; i < bytesPerRow && offset + i < payload.Length; i++)
            {
                var b = payload[offset + i];
                sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            }

            sb.Append('|');
        }

        return sb.ToString();
    }

    private static string FormatBase64(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(payload, Base64FormattingOptions.InsertLineBreaks);
    }

    private static bool TryParseHex(string text, out byte[] bytes, out string? errorMessage)
    {
        var cleaned = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch) || ch is '-' or ':' or ',')
            {
                i++;
                continue;
            }

            if (ch == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
            {
                i += 2;
                continue;
            }

            if (!Uri.IsHexDigit(ch))
            {
                bytes = [];
                errorMessage = $"Invalid hex character '{ch}' at position {i + 1}.";
                return false;
            }

            cleaned.Append(ch);
            i++;
        }

        if (cleaned.Length == 0)
        {
            bytes = [];
            errorMessage = null;
            return true;
        }

        if (cleaned.Length % 2 != 0)
        {
            bytes = [];
            errorMessage = "Hex payload must contain an even number of hex digits.";
            return false;
        }

        var result = new byte[cleaned.Length / 2];
        for (var idx = 0; idx < result.Length; idx++)
        {
            result[idx] = byte.Parse(cleaned.ToString(idx * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        bytes = result;
        errorMessage = null;
        return true;
    }

    private static bool TryParseBase64(string text, out byte[] bytes, out string? errorMessage)
    {
        var compact = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                compact.Append(ch);
            }
        }

        if (compact.Length == 0)
        {
            bytes = [];
            errorMessage = null;
            return true;
        }

        try
        {
            bytes = Convert.FromBase64String(compact.ToString());
            errorMessage = null;
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            errorMessage = "Invalid Base64 string.";
            return false;
        }
    }

    private static bool IsLikelyUtf8Text(byte[] payload, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }

        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
            {
                return false;
            }
        }

        return true;
    }
}
