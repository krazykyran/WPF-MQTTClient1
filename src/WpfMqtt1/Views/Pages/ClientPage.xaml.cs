using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class ClientPage : INavigableView<ClientViewModel>, IPageShortcutHandler
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

    public bool FocusSearch()
    {
        FeedSearchBox.Focus();
        FeedSearchBox.SelectAll();
        return true;
    }

    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return false;
        }

        switch (key)
        {
            case Key.L:
                if (ViewModel.ClearMessagesCommand.CanExecute(null))
                {
                    ViewModel.ClearMessagesCommand.Execute(null);
                    return true;
                }
                break;

            case Key.P:
                if (ViewModel.TogglePauseCommand.CanExecute(null))
                {
                    ViewModel.TogglePauseCommand.Execute(null);
                    return true;
                }
                break;

            case Key.R:
                if (ViewModel.ToggleRecordingCommand.CanExecute(null))
                {
                    ViewModel.ToggleRecordingCommand.Execute(null);
                    return true;
                }
                break;
        }

        return false;
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
