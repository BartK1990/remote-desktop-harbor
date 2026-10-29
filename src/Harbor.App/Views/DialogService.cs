using System.Windows;
using Harbor.App.ViewModels;
using Microsoft.Win32;

namespace Harbor.App.Views;

public sealed class DialogService : IDialogService
{
    public Window? Owner { get; set; }

    public bool EditEntry(EntryEditorViewModel editor) => new EntryEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool EditSettings(SettingsViewModel settings) => new SettingsWindow(settings) { Owner = Owner }.ShowDialog() == true;

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public string? PickRdpFile()
    {
        var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp|All files (*.*)|*.*", Title = "Import .rdp file" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
