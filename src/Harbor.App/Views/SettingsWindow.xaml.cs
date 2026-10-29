using System.Windows;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Validate())
        {
            DialogResult = true;
        }
    }
}
