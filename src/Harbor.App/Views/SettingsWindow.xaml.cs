using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        DbPasswordBox.Password = viewModel.DbPassword ?? "";
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    // PasswordBox cannot be bound; mirror it both ways (the view model reloads it when leaving the raw editor).
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.DbPassword) && DbPasswordBox.Password != (_viewModel.DbPassword ?? ""))
        {
            DbPasswordBox.Password = _viewModel.DbPassword ?? "";
        }
    }

    private void DbPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.DbPassword = DbPasswordBox.Password;

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save_Click(sender, e);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate()) return;
        IsEnabled = false;
        try
        {
            if (await _viewModel.PrepareSaveAsync())
            {
                DialogResult = true;
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
