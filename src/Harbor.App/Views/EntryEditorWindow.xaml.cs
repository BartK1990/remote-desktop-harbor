using System.Windows;
using Harbor.App.ViewModels;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Harbor.App.Views;

public partial class EntryEditorWindow : FluentWindow
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

    private void AccountPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox { DataContext: AccountEditorViewModel account } box) account.Password = box.Password;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Validate())
        {
            DialogResult = true;
        }
    }
}
