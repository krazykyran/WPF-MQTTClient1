using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;
using WpfMqtt1.Models;
using WpfMqtt1.ViewModels;

namespace WpfMqtt1.Views.Pages;

public partial class HistoryPage : INavigableView<HistoryViewModel>, IPageShortcutHandler
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public bool FocusSearch()
    {
        if (ViewModel.HasSelectedSession && MessageSearchBox.IsEnabled)
        {
            MessageSearchBox.Focus();
            MessageSearchBox.SelectAll();
            return true;
        }

        SessionSearchBox.Focus();
        SessionSearchBox.SelectAll();
        return true;
    }

    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None && key == Key.F5)
        {
            if (ViewModel.RefreshCommand.CanExecute(null))
            {
                ViewModel.RefreshCommand.Execute(null);
                return true;
            }
        }

        return false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncColumnSortIndicators();
        await ViewModel.RefreshSessionsAsync();
    }

    private async void MessagesDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        var targetColumn = e.Column.SortMemberPath switch
        {
            nameof(MqttLogMessage.TimestampUtc) => SessionMessageSortColumn.Timestamp,
            nameof(MqttLogMessage.Topic) => SessionMessageSortColumn.Topic,
            _ => (SessionMessageSortColumn?)null
        };

        if (targetColumn is null)
        {
            return;
        }

        var nextDescending = ViewModel.SortColumn == targetColumn
            ? !ViewModel.SortDescending
            : false;

        var sortTask = ViewModel.SetSortAsync(targetColumn.Value, nextDescending);
        SyncColumnSortIndicators();
        await sortTask;
    }

    private void SyncColumnSortIndicators()
    {
        foreach (var col in MessagesDataGrid.Columns)
        {
            col.SortDirection = null;
        }

        var direction = ViewModel.SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        var activeColumn = ViewModel.SortColumn switch
        {
            SessionMessageSortColumn.Topic => TopicColumn,
            _ => TimestampColumn
        };

        activeColumn.SortDirection = direction;
    }
}
