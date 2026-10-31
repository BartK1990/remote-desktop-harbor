using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

public partial class EntryEditorWindow : Window
{
    private readonly EntryEditorViewModel _viewModel;

    public EntryEditorWindow(EntryEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        PasswordBox.Password = viewModel.Password;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    // PasswordBox cannot bind: mirror the view model's password when a command (Remove, Keep) clears it.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryEditorViewModel.Password) && PasswordBox.Password != _viewModel.Password)
        {
            PasswordBox.Password = _viewModel.Password;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordBox.Password;

    // Replace… shows the password box; put the cursor in it once it is visible (SPEC FR-46).
    private void ReplacePassword_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PasswordBox.Focus());

    private void AccountPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: AccountEditorViewModel account } box) account.Password = box.Password;
    }

    // An account's box appears after Replace…; it starts empty, like the view model's password after Keep.
    private void AccountPasswordBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: AccountEditorViewModel account } box && e.NewValue is true && box.Password != account.Password)
        {
            box.Password = account.Password;
            if (account.HadPassword) box.Focus();
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save();

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_viewModel.Validate())
        {
            DialogResult = true;
            return;
        }

        // SPEC FR-46: open the tab with the problem and put the cursor in the field.
        if (_viewModel.ErrorLocation is not var (tab, field)) return;
        if (Tabs.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Header, tab)) is { } item) item.IsSelected = true;
        if (field.Length > 0 && FindName(field) is UIElement target)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => target.Focus());
        }
    }
}
