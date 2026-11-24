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
    }
}
