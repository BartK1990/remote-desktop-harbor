using System.Windows;
using Harbor.App.ViewModels;
using Harbor.Core.Backup;
using Microsoft.Win32;

namespace Harbor.App.Views;

public sealed class DialogService : IDialogService
{
    public Window? Owner { get; set; }

    public bool EditEntry(EntryEditorViewModel editor) => new EntryEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool EditSettings(SettingsViewModel settings) => new SettingsWindow(settings) { Owner = Owner }.ShowDialog() == true;

    public bool EditSmartGroup(SmartGroupEditorViewModel editor) => new SmartGroupEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

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

    public string? PickRdmCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Remote Desktop Manager export (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import from Remote Desktop Manager",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    // SPEC FR-68, ADR-0046
    public string? PickBackupFileToSave(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = BackupFilter,
            Title = "Export backup",
            FileName = suggestedName,
            DefaultExt = BackupFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickBackupFileToOpen()
    {
        var dialog = new OpenFileDialog { Filter = BackupFilter, Title = "Restore backup" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? AskBackupPassphrase(bool isNew)
    {
        var dialog = PassphraseWindow.ForBackup(isNew);
        dialog.Owner = Owner;
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    private const string BackupFilter = "Remote Desktop Harbor backup (*" + BackupFile.Extension + ")|*" + BackupFile.Extension + "|All files (*.*)|*.*";

    public string[]? PickFilesToUpload()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Files to upload" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public string[]? PickFilesToDownload(string remoteFolder)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Files to download", InitialDirectory = remoteFolder };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initialFolder ?? "" };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    private TransfersWindow? _transfers;

    public void ShowTransfers(TransfersViewModel transfers)
    {
        // One window for all transfers; it keeps running while the main window is used (SPEC FR-60).
        if (_transfers is null)
        {
            _transfers = new TransfersWindow(transfers) { Owner = Owner };
            _transfers.Closed += (_, _) => _transfers = null;
            _transfers.Show();
        }

        if (_transfers.WindowState == WindowState.Minimized) _transfers.WindowState = WindowState.Normal;
        _transfers.Activate();
    }
}
