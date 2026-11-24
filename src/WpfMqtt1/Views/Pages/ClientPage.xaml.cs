using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class ClientPage : INavigableView<ClientViewModel>
{
    public ClientViewModel ViewModel { get; }

    public ClientPage(ClientViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        Loaded += OnLoaded;
        ViewModel.MessagesAppended += OnMessagesAppended;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureLoadedAsync();
    }

    private void OnMessagesAppended(object? sender, EventArgs e)
    {
        if (ViewModel.VisibleMessages.Count > 0)
        {
            MessageFeedListView.ScrollIntoView(ViewModel.VisibleMessages[^1]);
        }
    }
}
