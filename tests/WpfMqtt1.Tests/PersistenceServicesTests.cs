using System.IO;
using WpfMqtt1.Models;
using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class PersistenceServicesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPathsService _paths;

    public PersistenceServicesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_Persist_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPathsService(_tempDir);
    }

    [Fact]
    public async Task SettingsService_SaveAndReload_PersistsAllFields()
    {
        var themeService = new AppThemeService();
        var writer = new SettingsService(_paths, themeService);
        writer.Current.Theme = AppThemePreference.Light;
        writer.Current.Backdrop = WindowBackdropPreference.Acrylic;
        writer.Current.AutoRecordOnConnect = true;
        writer.Current.MaxInMemoryMessages = 10_000;
        writer.Current.DefaultPublishQos = 2;
        writer.Current.DefaultPayloadFormat = PayloadFormat.Json;
        var brokerId = Guid.NewGuid();
        writer.Current.LastSelectedBrokerId = brokerId;

        await writer.SaveAsync();

        var readerTheme = new AppThemeService();
        var reader = new SettingsService(_paths, readerTheme);
        await reader.LoadAsync();

        Assert.Equal(AppThemePreference.Light, reader.Current.Theme);
        Assert.Equal(WindowBackdropPreference.Acrylic, reader.Current.Backdrop);
        Assert.True(reader.Current.AutoRecordOnConnect);
        Assert.Equal(10_000, reader.Current.MaxInMemoryMessages);
        Assert.Equal(2, reader.Current.DefaultPublishQos);
        Assert.Equal(PayloadFormat.Json, reader.Current.DefaultPayloadFormat);
        Assert.Equal(brokerId, reader.Current.LastSelectedBrokerId);

        Assert.Equal(AppThemePreference.Light, readerTheme.CurrentTheme);
        Assert.Equal(WindowBackdropPreference.Acrylic, readerTheme.CurrentBackdrop);
    }

    [Fact]
    public async Task BrokerProfileStore_UpsertReloadDelete_PersistsEncryptedCredentialsOnly()
    {
        var protector = new SecretProtectorService();
        var store = new BrokerProfileStore(_paths);
        var changedCount = 0;
        store.ProfilesChanged += (_, _) => changedCount++;

        const string plaintextPassword = "SuperSecretMqttPassword99!";
        var profile = new BrokerProfile
        {
            Name = "Production EU",
            Host = "mqtt.example.org",
            Port = 8883,
            Transport = BrokerTransportType.Tls,
            ProtocolVersion = MqttProtocolVersionOption.V500,
            Username = "iot-user",
            ProtectedPassword = protector.Protect(plaintextPassword),
            AllowUntrustedCertificates = false,
            DefaultTopics = ["devices/+/telemetry", "alerts/#"]
        };

        await store.UpsertAsync(profile);

        var rawJson = await File.ReadAllTextAsync(_paths.BrokersFilePath);
        Assert.DoesNotContain(plaintextPassword, rawJson, StringComparison.Ordinal);

        var reloaded = await store.GetAllAsync();
        var item = Assert.Single(reloaded);
        Assert.Equal("Production EU", item.Name);
        Assert.Equal("mqtts://mqtt.example.org:8883", item.DisplayUri);
        Assert.Equal(plaintextPassword, protector.Unprotect(item.ProtectedPassword));
        Assert.Equal(["devices/+/telemetry", "alerts/#"], item.DefaultTopics);

        await store.DeleteAsync(profile.Id);
        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(2, changedCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
