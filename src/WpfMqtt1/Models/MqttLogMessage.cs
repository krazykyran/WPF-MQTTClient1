namespace WpfMqtt1.Models;

public sealed class MqttLogMessage
{
    public long SequenceId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public MessageDirection Direction { get; init; }
    public string Topic { get; init; } = string.Empty;
    public int Qos { get; init; }
    public bool Retain { get; init; }
    public byte[] Payload { get; init; } = [];
    public string? ContentType { get; init; }
    public string PayloadPreview { get; init; } = string.Empty;
    public string AccentColorHex { get; init; } = "#60A5FA";

    public int ByteCount => Payload.Length;

    public string TimestampDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    public string TimestampFullDisplay => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string DirectionLabel => Direction == MessageDirection.Inbound ? "IN" : "OUT";
    public string DirectionArrow => Direction == MessageDirection.Inbound ? "↓ IN" : "↑ OUT";
    public string QosLabel => $"QoS {Qos}";

    public string SizeDisplay => FormatByteSize(ByteCount);

    public static string FormatByteSize(int byteCount)
    {
        if (byteCount < 1024)
        {
            return $"{byteCount} B";
        }

        double kb = byteCount / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        return $"{kb / 1024.0:0.##} MB";
    }
}
