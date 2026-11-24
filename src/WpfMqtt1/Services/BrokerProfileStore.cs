using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class BrokerProfileStore : IBrokerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAppPathsService _pathsService;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public event EventHandler? ProfilesChanged;

    public BrokerProfileStore(IAppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    public async Task<IReadOnlyList<BrokerProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(p => p.Clone()).ToList();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAllAsync(IEnumerable<BrokerProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = profiles.Select(p => p.Clone()).ToList();

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteInternalAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpsertAsync(BrokerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
            var index = list.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                list[index] = profile.Clone();
            }
            else
            {
                list.Add(profile.Clone());
            }

            await WriteInternalAsync(list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadInternalAsync(cancellationToken).ConfigureAwait(false);
            var removed = list.RemoveAll(p => p.Id == profileId);
            if (removed > 0)
            {
                await WriteInternalAsync(list, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _ioLock.Release();
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<List<BrokerProfile>> ReadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_pathsService.BrokersFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_pathsService.BrokersFilePath);
            var loaded = await JsonSerializer.DeserializeAsync<List<BrokerProfile>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return loaded ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteInternalAsync(List<BrokerProfile> profiles, CancellationToken cancellationToken)
    {
        _pathsService.EnsureAppDataDirectoryExists();
        var tempPath = _pathsService.BrokersFilePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, _pathsService.BrokersFilePath, overwrite: true);
    }
}
