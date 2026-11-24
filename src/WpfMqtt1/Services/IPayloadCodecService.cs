using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed record PayloadFormatResult(
    string FormattedText,
    bool IsValidFormat,
    string? WarningMessage = null);

public interface IPayloadCodecService
{
    PayloadFormatResult FormatPayload(byte[] payload, PayloadFormat format);
    string CreatePreview(byte[] payload, int maxChars = 160);
    bool TryEncodePayload(string? text, PayloadFormat format, out byte[] bytes, out string? errorMessage);
    bool TryFormatJson(string? text, out string formatted, out string? errorMessage);
}
