namespace WpfMqtt1.Models;

public sealed class RecordingSession
{
    public long Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string BrokerName { get; init; } = string.Empty;
    public string BrokerAddress { get; init; } = string.Empty;
    public string MqttVersion { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public long MessageCount { get; set; }

    public string StartedAtDisplay => StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string MessageCountDisplay => MessageCount == 1 ? "1 msg" : $"{MessageCount:N0} msgs";

    public string DurationDisplay
    {
        get
        {
            if (EndedAtUtc is null)
            {
                return "Recording…";
            }

            var elapsed = EndedAtUtc.Value - StartedAtUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalHours >= 1)
            {
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
            }

            if (elapsed.TotalMinutes >= 1)
            {
                return $"{elapsed.Minutes}m {elapsed.Seconds}s";
            }

            return $"{Math.Max(1, (int)Math.Round(elapsed.TotalSeconds))}s";
        }
    }
}
