using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfMqtt1.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Offline SQLite session recording, search/filter, payload inspection, and CSV/JSON export will be wired up in Phase 4.";
}
