using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using WpfMqtt1.Data;
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
        services.AddSingleton<IErrorLogService, ErrorLogService>();
        services.AddSingleton<ISecretProtectorService, SecretProtectorService>();
        services.AddSingleton<IAppThemeService, AppThemeService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IBrokerProfileStore, BrokerProfileStore>();
        services.AddSingleton<IPayloadCodecService, PayloadCodecService>();
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<ISessionRecorderService, SessionRecorderService>();
        services.AddSingleton<IExportService, ExportService>();
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
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        await HostInstance.StartAsync();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        await HostInstance.StopAsync();
        HostInstance.Dispose();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);

        try
        {
            var snackbar = Services.GetService<ISnackbarService>();
            if (snackbar?.GetSnackbarPresenter() is not null)
            {
                snackbar.Show(
                    "Unexpected Error",
                    e.Exception.GetBaseException().Message,
                    Wpf.Ui.Controls.ControlAppearance.Danger,
                    new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(6));
            }
        }
        catch
        {
            // Ignore secondary snackbar errors during exception handling.
        }

        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError("AppDomainUnhandledException", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogError(string source, Exception exception)
    {
        try
        {
            Services.GetService<IErrorLogService>()?.LogException(source, exception);
        }
        catch
        {
            // Never throw while logging an unhandled exception.
        }
    }
}
