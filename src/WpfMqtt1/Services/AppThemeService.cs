using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public sealed class AppThemeService : IAppThemeService
{
    private Window? _window;
    private bool _isWatchingSystemTheme;

    public AppThemePreference CurrentTheme { get; private set; } = AppThemePreference.System;
    public WindowBackdropPreference CurrentBackdrop { get; private set; } = WindowBackdropPreference.Mica;

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        ApplyBackdropAndTheme();
    }

    public void ApplyTheme(AppThemePreference theme)
    {
        CurrentTheme = theme;
        ApplyBackdropAndTheme();
    }

    public void ApplyBackdrop(WindowBackdropPreference backdrop)
    {
        CurrentBackdrop = backdrop;
        ApplyBackdropAndTheme();
    }

    private void ApplyBackdropAndTheme()
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(ApplyBackdropAndThemeOnUiThread);
            return;
        }

        ApplyBackdropAndThemeOnUiThread();
    }

    private void ApplyBackdropAndThemeOnUiThread()
    {
        var backdropType = ToBackdropType(CurrentBackdrop);

        if (_window is not null && _isWatchingSystemTheme)
        {
            SystemThemeWatcher.UnWatch(_window);
            _isWatchingSystemTheme = false;
        }

        if (_window is FluentWindow fluentWindow)
        {
            fluentWindow.WindowBackdropType = backdropType;
        }

        switch (CurrentTheme)
        {
            case AppThemePreference.System:
                ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
                if (_window is not null)
                {
                    SystemThemeWatcher.Watch(_window, backdropType, updateAccents: true);
                    _isWatchingSystemTheme = true;
                }
                break;

            case AppThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, backdropType, updateAccent: true);
                break;

            case AppThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, backdropType, updateAccent: true);
                break;
        }

        if (_window is not null && _window.IsLoaded)
        {
            if (backdropType == WindowBackdropType.None)
            {
                WindowBackdrop.RemoveBackdrop(_window);
            }
            else
            {
                WindowBackdrop.ApplyBackdrop(_window, backdropType);
            }
        }
    }

    internal static WindowBackdropType ToBackdropType(WindowBackdropPreference backdrop) =>
        backdrop switch
        {
            WindowBackdropPreference.Mica => WindowBackdropType.Mica,
            WindowBackdropPreference.Acrylic => WindowBackdropType.Acrylic,
            _ => WindowBackdropType.None
        };
}
