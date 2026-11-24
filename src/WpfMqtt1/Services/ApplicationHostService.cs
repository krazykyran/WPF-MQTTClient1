using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using WpfMqtt1.Views;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1.Services;

public sealed class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAppThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IMqttClientService _mqttClientService;
    private readonly ISessionRecorderService _sessionRecorderService;
    private INavigationWindow? _navigationWindow;

    public ApplicationHostService(
        IServiceProvider serviceProvider,
        IAppThemeService themeService,
        ISettingsService settingsService,
        IMqttClientService mqttClientService,
        ISessionRecorderService sessionRecorderService)
    {
        _serviceProvider = serviceProvider;
        _themeService = themeService;
        _settingsService = settingsService;
        _mqttClientService = mqttClientService;
        _sessionRecorderService = sessionRecorderService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(cancellationToken);

        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            await app.Dispatcher.InvokeAsync(HandleActivation);
        }
        else
        {
            HandleActivation();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sessionRecorderService.StopRecordingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Swallow shutdown errors so the app exits cleanly.
        }

        try
        {
            await _mqttClientService.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Swallow shutdown errors so the app exits cleanly.
        }
    }

    private void HandleActivation()
    {
        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = _serviceProvider.GetRequiredService<INavigationWindow>();
            _navigationWindow.ShowWindow();

            if (_navigationWindow is Window window)
            {
                _themeService.AttachWindow(window);
            }

            _navigationWindow.Navigate(typeof(ClientPage));
        }
    }
}
