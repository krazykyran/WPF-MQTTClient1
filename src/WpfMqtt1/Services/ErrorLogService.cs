using System.IO;
using System.Text;

namespace WpfMqtt1.Services;

public interface IErrorLogService
{
    void LogException(string source, Exception exception);
}

public sealed class ErrorLogService(IAppPathsService pathsService) : IErrorLogService
{
    private readonly Lock _fileLock = new();

    public void LogException(string source, Exception exception)
    {
        try
        {
            var dir = Path.GetDirectoryName(pathsService.ErrorLogFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var entry = new StringBuilder();
            entry.AppendLine("============================================================");
            entry.AppendLine($"UTC:    {DateTimeOffset.UtcNow:O}");
            entry.AppendLine($"Source: {source}");
            entry.AppendLine($"Type:   {exception.GetType().FullName}");
            entry.AppendLine($"Error:  {exception.Message}");
            entry.AppendLine(exception.ToString());
            entry.AppendLine();

            lock (_fileLock)
            {
                File.AppendAllText(pathsService.ErrorLogFilePath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Error logging must never throw and cascade during exception handling.
        }
    }
}
