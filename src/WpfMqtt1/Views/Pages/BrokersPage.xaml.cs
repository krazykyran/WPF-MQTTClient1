using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class BrokersPage : INavigableView<BrokersViewModel>
{
    public BrokersViewModel ViewModel { get; }

    public BrokersPage(BrokersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureLoadedAsync();
    }
}
