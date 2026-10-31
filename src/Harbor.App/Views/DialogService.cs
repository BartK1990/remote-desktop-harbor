using System.Windows;
using Harbor.App.ViewModels;
using Microsoft.Win32;

namespace Harbor.App.Views;

public sealed class DialogService : IDialogService
{
    public Window? Owner { get; set; }

    public bool EditEntry(EntryEditorViewModel editor) => new EntryEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool EditSettings(SettingsViewModel settings) => new SettingsWindow(settings) { Owner = Owner }.ShowDialog() == true;

    // SPEC FR-53, ADR-0030: the app's own message box instead of the system one.
    public bool Confirm(string title, string message) => MessageWindow.Show(Owner, title, message, MessageKind.Question);

    public bool ConfirmRisky(string title, string message, string confirmText) =>
        MessageWindow.Show(Owner, title, message, MessageKind.Risky, confirmText, "Cancel");

    public void ShowError(string title, string message) => MessageWindow.Show(Owner, title, message, MessageKind.Error);

    public string? PickRdpFile()
    {
        var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp|All files (*.*)|*.*", Title = "Import .rdp file" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
