using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using WpfMqtt1.Data;
using WpfMqtt1.Models;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Tests;

public sealed class Phase1ShellTests
{
    [Fact]
    public void ConfigureServices_ResolvesCoreServicesAndViewModels()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<INavigationViewPageProvider>());
        Assert.NotNull(provider.GetRequiredService<INavigationService>());
        Assert.NotNull(provider.GetRequiredService<ISnackbarService>());
        Assert.NotNull(provider.GetRequiredService<IContentDialogService>());

        Assert.NotNull(provider.GetRequiredService<IAppPathsService>());
        Assert.NotNull(provider.GetRequiredService<ISecretProtectorService>());
        Assert.NotNull(provider.GetRequiredService<IAppThemeService>());
        Assert.NotNull(provider.GetRequiredService<ISettingsService>());
        Assert.NotNull(provider.GetRequiredService<IBrokerProfileStore>());
        Assert.NotNull(provider.GetRequiredService<IPayloadCodecService>());
        Assert.NotNull(provider.GetRequiredService<ISessionRepository>());
        Assert.NotNull(provider.GetRequiredService<ISessionRecorderService>());
        Assert.NotNull(provider.GetRequiredService<IExportService>());
        Assert.NotNull(provider.GetRequiredService<IMqttClientService>());

        Assert.NotNull(provider.GetRequiredService<MainWindowViewModel>());
        Assert.NotNull(provider.GetRequiredService<ClientViewModel>());
        Assert.NotNull(provider.GetRequiredService<BrokersViewModel>());
        Assert.NotNull(provider.GetRequiredService<HistoryViewModel>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected, "Disconnected")]
    [InlineData(ConnectionState.Connecting, "Connecting...")]
    [InlineData(ConnectionState.Connected, "Connected")]
    [InlineData(ConnectionState.Reconnecting, "Reconnecting...")]
    public void MainWindowViewModel_ConnectionStateText_MatchesState(ConnectionState state, string expectedText)
    {
        var vm = new MainWindowViewModel { ConnectionState = state };

        Assert.Equal(expectedText, vm.ConnectionStateText);
    }

    [Fact]
    public async Task SettingsViewModel_UpdatesThemeAndBackdropThroughThemeService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WpfMqtt1_Tests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPathsService(tempDir);
            var fakeThemeService = new FakeThemeService();
            var settingsService = new SettingsService(paths, fakeThemeService);
            var snackbar = new SnackbarService();
            var vm = new SettingsViewModel(fakeThemeService, settingsService, paths, snackbar);

            Assert.Equal(AppThemePreference.System, vm.SelectedTheme);
            Assert.Equal(WindowBackdropPreference.Mica, vm.SelectedBackdrop);

            vm.SelectedTheme = AppThemePreference.Light;
            await vm.WaitForPendingSaveAsync();
            vm.SelectedBackdrop = WindowBackdropPreference.Acrylic;
            await vm.WaitForPendingSaveAsync();

            Assert.Equal(AppThemePreference.Light, fakeThemeService.CurrentTheme);
            Assert.Equal(WindowBackdropPreference.Acrylic, fakeThemeService.CurrentBackdrop);
            Assert.Equal(1, fakeThemeService.ApplyThemeCalls);
            Assert.Equal(1, fakeThemeService.ApplyBackdropCalls);
            Assert.True(File.Exists(paths.SettingsFilePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void AppThemeService_WithoutApplicationInstance_TracksSelectedPreferences()
    {
        var service = new AppThemeService();

        service.ApplyTheme(AppThemePreference.Dark);
        service.ApplyBackdrop(WindowBackdropPreference.None);

        Assert.Equal(AppThemePreference.Dark, service.CurrentTheme);
        Assert.Equal(WindowBackdropPreference.None, service.CurrentBackdrop);
    }

    [Fact]
    public void AllXamlSymbolIcons_AreValidSymbolRegularMembers()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WpfMqtt1.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var srcDir = Path.Combine(dir!.FullName, "src", "WpfMqtt1");
        var xamlFiles = Directory.GetFiles(srcDir, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(xamlFiles);

        var regex = new Regex(@"\{ui:SymbolIcon\s+([A-Za-z0-9]+)\}|Symbol=""([A-Za-z0-9]+)""", RegexOptions.Compiled);
        var invalid = new List<string>();

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in regex.Matches(content))
            {
                var symbolName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!Enum.TryParse<SymbolRegular>(symbolName, ignoreCase: false, out _))
                {
                    invalid.Add($"{Path.GetFileName(file)}: {symbolName}");
                }
            }
        }

        Assert.Empty(invalid);
    }

    private sealed class FakeThemeService : IAppThemeService
    {
        public AppThemePreference CurrentTheme { get; private set; } = AppThemePreference.System;
        public WindowBackdropPreference CurrentBackdrop { get; private set; } = WindowBackdropPreference.Mica;
        public int ApplyThemeCalls { get; private set; }
        public int ApplyBackdropCalls { get; private set; }

        public void AttachWindow(Window window)
        {
        }

        public void ApplyTheme(AppThemePreference theme)
        {
            CurrentTheme = theme;
            ApplyThemeCalls++;
        }

        public void ApplyBackdrop(WindowBackdropPreference backdrop)
        {
            CurrentBackdrop = backdrop;
            ApplyBackdropCalls++;
        }
    }
}
