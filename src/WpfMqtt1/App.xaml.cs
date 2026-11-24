using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;
using WpfMqtt1.Views;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1;

public partial class App : Application
{
    private static readonly IHost HostInstance = Host
        .CreateDefaultBuilder()
        .ConfigureServices((_, services) => ConfigureServices(services))
        .Build();

    public static IServiceProvider Services => HostInstance.Services;

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddNavigationViewPageProvider();

        services.AddHostedService<ApplicationHostService>();

        // WPF-UI framework services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // Core application services
        services.AddSingleton<IAppPathsService, AppPathsService>();
        services.AddSingleton<ISecretProtectorService, SecretProtectorService>();
        services.AddSingleton<IAppThemeService, AppThemeService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IBrokerProfileStore, BrokerProfileStore>();
        services.AddSingleton<IMqttClientService, MqttClientService>();

        // Shell
        services.AddSingleton<INavigationWindow, MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // Pages & ViewModels
        services.AddSingleton<ClientPage>();
        services.AddSingleton<ClientViewModel>();

        services.AddSingleton<BrokersPage>();
        services.AddSingleton<BrokersViewModel>();

        services.AddSingleton<HistoryPage>();
        services.AddSingleton<HistoryViewModel>();

        services.AddSingleton<SettingsPage>();
        services.AddSingleton<SettingsViewModel>();
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        await HostInstance.StartAsync();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        await HostInstance.StopAsync();
        HostInstance.Dispose();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Placeholder hook for structured logging / user notification in later phases.
    }
}
