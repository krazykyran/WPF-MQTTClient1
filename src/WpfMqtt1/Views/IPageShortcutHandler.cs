using System.Windows.Input;

namespace WpfMqtt1.Views;

public interface IPageShortcutHandler
{
    bool FocusSearch();
    bool HandleShortcut(Key key, ModifierKeys modifiers);
}
