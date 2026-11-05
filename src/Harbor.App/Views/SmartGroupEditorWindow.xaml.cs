using System.Windows;
using System.Windows.Input;
using Harbor.App.ViewModels;

namespace Harbor.App.Views;

/// <summary>Smart group editor (SPEC FR-62, ADR-0039).</summary>
public partial class SmartGroupEditorWindow : Window
{
    private readonly SmartGroupEditorViewModel _viewModel;

    public SmartGroupEditorWindow(SmartGroupEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save();

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_viewModel.Validate()) DialogResult = true;
    }
}
