using System.Windows;
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
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Validate())
        {
            DialogResult = true;
        }
    }
}
