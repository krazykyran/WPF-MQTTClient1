namespace WpfMqtt1.Services;

public interface IAppPathsService
{
    string AppDataDirectory { get; }
    string SettingsFilePath { get; }
    string BrokersFilePath { get; }
    string HistoryDatabasePath { get; }
    string ErrorLogFilePath { get; }

    void EnsureAppDataDirectoryExists();
}
