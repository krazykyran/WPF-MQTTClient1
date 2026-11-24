using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class BrokersPage : INavigableView<BrokersViewModel>, IPageShortcutHandler
{
    public BrokersViewModel ViewModel { get; }

    public BrokersPage(BrokersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public bool FocusSearch()
    {
        BrokerSearchBox.Focus();
        BrokerSearchBox.SelectAll();
        return true;
    }

    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.S)
        {
            if (ViewModel.SaveBrokerCommand.CanExecute(null))
            {
                ViewModel.SaveBrokerCommand.Execute(null);
                return true;
            }
        }

        return false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureLoadedAsync();
    }
}
