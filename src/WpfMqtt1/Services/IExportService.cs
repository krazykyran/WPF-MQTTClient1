using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public interface IExportService
{
    Task ExportToCsvAsync(
        string filePath,
        RecordingSession session,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default);

    Task ExportToJsonAsync(
        string filePath,
        RecordingSession session,
        IReadOnlyList<MqttLogMessage> messages,
        CancellationToken cancellationToken = default);
}
