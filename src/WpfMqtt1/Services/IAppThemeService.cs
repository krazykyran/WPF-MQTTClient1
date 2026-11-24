using System.Windows;
using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public interface IAppThemeService
{
    AppThemePreference CurrentTheme { get; }
    WindowBackdropPreference CurrentBackdrop { get; }

    void AttachWindow(Window window);
    void ApplyTheme(AppThemePreference theme);
    void ApplyBackdrop(WindowBackdropPreference backdrop);
}
