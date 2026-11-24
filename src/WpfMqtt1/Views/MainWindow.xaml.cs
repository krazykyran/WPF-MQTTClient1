using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using WpfMqtt1.Services;
using WpfMqtt1.ViewModels;
using WpfMqtt1.Views.Pages;

namespace WpfMqtt1.Views;

public partial class MainWindow : INavigationWindow
{
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;
    private Type? _activePageType;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        ISettingsService settingsService,
        IServiceProvider serviceProvider,
        INavigationViewPageProvider navigationViewPageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        ViewModel = viewModel;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;
        DataContext = this;

        InitializeComponent();
        RestoreWindowBounds();

        SetPageService(navigationViewPageProvider);

        navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        RootNavigation.Navigated += OnNavigated;
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnWindowClosing;
    }

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
        RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        // Resolved via INavigationViewPageProvider registered in DI.
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        _activePageType = args.Page?.GetType();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    RootNavigation.Navigate(typeof(ClientPage));
                    e.Handled = true;
                    return;

                case Key.D2:
                case Key.NumPad2:
                    RootNavigation.Navigate(typeof(BrokersPage));
                    e.Handled = true;
                    return;

                case Key.D3:
                case Key.NumPad3:
                    RootNavigation.Navigate(typeof(HistoryPage));
                    e.Handled = true;
                    return;

                case Key.D4:
                case Key.NumPad4:
                    RootNavigation.Navigate(typeof(SettingsPage));
                    e.Handled = true;
                    return;

                case Key.F:
                    if (GetActiveShortcutHandler()?.FocusSearch() == true)
                    {
                        e.Handled = true;
                    }
                    return;
            }
        }

        if (GetActiveShortcutHandler()?.HandleShortcut(e.Key, modifiers) == true)
        {
            e.Handled = true;
        }
    }

    private IPageShortcutHandler? GetActiveShortcutHandler()
    {
        var pageType = _activePageType ?? RootNavigation.SelectedItem?.TargetPageType;
        if (pageType is null)
        {
            return null;
        }

        return _serviceProvider.GetService(pageType) as IPageShortcutHandler;
    }

    private void RestoreWindowBounds()
    {
        var s = _settingsService.Current;
        var resolved = WindowBoundsHelper.Resolve(
            s.WindowWidth,
            s.WindowHeight,
            s.WindowLeft,
            s.WindowTop,
            s.IsWindowMaximized,
            MinWidth,
            MinHeight,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        Width = resolved.Width;
        Height = resolved.Height;

        if (resolved.Left.HasValue && resolved.Top.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = resolved.Left.Value;
            Top = resolved.Top.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (resolved.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            var s = _settingsService.Current;
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
            {
                s.WindowWidth = bounds.Width;
                s.WindowHeight = bounds.Height;
                s.WindowLeft = bounds.Left;
                s.WindowTop = bounds.Top;
            }

            s.IsWindowMaximized = WindowState == WindowState.Maximized;
            _settingsService.SaveAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Never block window shutdown if settings disk write fails.
        }
    }
}
