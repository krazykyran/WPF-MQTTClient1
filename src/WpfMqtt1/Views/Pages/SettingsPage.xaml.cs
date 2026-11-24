using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncFromSettings();
        ViewModel.RefreshStorageInfo();
    }
}
