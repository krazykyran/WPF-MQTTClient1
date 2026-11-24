using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAppPathsService _pathsService;
    private readonly IAppThemeService _themeService;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(IAppPathsService pathsService, IAppThemeService themeService)
    {
        _pathsService = pathsService;
        _themeService = themeService;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_pathsService.SettingsFilePath))
            {
                await using var stream = File.OpenRead(_pathsService.SettingsFilePath);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
                if (loaded is not null)
                {
                    Normalize(loaded);
                    Current = loaded;
                }
            }
        }
        catch (JsonException)
        {
            Current = new AppSettings();
        }
        finally
        {
            _ioLock.Release();
        }

        _themeService.ApplyBackdrop(Current.Backdrop);
        _themeService.ApplyTheme(Current.Theme);
        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize(Current);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pathsService.EnsureAppDataDirectoryExists();
            var tempPath = _pathsService.SettingsFilePath + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _pathsService.SettingsFilePath, overwrite: true);
        }
        finally
        {
            _ioLock.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.MaxInMemoryMessages = Math.Clamp(settings.MaxInMemoryMessages, 100, 100_000);
        settings.DefaultPublishQos = Math.Clamp(settings.DefaultPublishQos, 0, 2);
    }
}
