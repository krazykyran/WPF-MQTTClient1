using System.IO;

namespace WpfMqtt1.Services;

public sealed class AppPathsService : IAppPathsService
{
    public AppPathsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfMqtt1"))
    {
    }

    public AppPathsService(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        AppDataDirectory = baseDirectory;
        SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");
        BrokersFilePath = Path.Combine(AppDataDirectory, "brokers.json");
        HistoryDatabasePath = Path.Combine(AppDataDirectory, "history.db");
        ErrorLogFilePath = Path.Combine(AppDataDirectory, "logs", "errors.log");
    }

    public string AppDataDirectory { get; }
    public string SettingsFilePath { get; }
    public string BrokersFilePath { get; }
    public string HistoryDatabasePath { get; }
    public string ErrorLogFilePath { get; }

    public void EnsureAppDataDirectoryExists()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }
}
