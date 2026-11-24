using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class HistoryPage : INavigableView<HistoryViewModel>
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
