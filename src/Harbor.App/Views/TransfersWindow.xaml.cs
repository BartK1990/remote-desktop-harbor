using System.Windows;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

/// <summary>File transfer window (SPEC FR-60, ADR-0039). Closing it does not stop transfers.</summary>
public partial class TransfersWindow : Window
{
    public TransfersWindow(TransfersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
