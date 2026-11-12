namespace Harbor.App.ViewModels;

public interface IDialogService
{
    bool EditEntry(EntryEditorViewModel editor);
    bool EditSettings(SettingsViewModel settings);

    /// <summary>Shows the smart group editor (SPEC FR-62); <c>true</c> when the group was saved.</summary>
    bool EditSmartGroup(SmartGroupEditorViewModel editor);
    bool Confirm(string title, string message);

    /// <summary>
    /// A confirmation whose "yes" has consequences (SPEC FR-53): <paramref name="confirmText"/> names the action and
    /// Cancel is the default, so Enter never confirms by accident.
    /// </summary>
    bool ConfirmRisky(string title, string message, string confirmText);
    void ShowError(string title, string message);
    string? PickRdpFile();

    /// <summary>Picks a Remote Desktop Manager CSV export to import (SPEC FR-54).</summary>
    string? PickRdmCsvFile();

    /// <summary>Where to write a backup of the catalog (SPEC FR-68); <paramref name="suggestedName"/> is the default file name.</summary>
    string? PickBackupFileToSave(string suggestedName);

    /// <summary>A backup file to restore (SPEC FR-68).</summary>
    string? PickBackupFileToOpen();

    /// <summary>
    /// Asks for the passphrase of a backup (SPEC FR-68): a new one, typed twice and at least
    /// <see cref="Harbor.Core.Backup.BackupFile.MinPassphraseLength"/> characters (<paramref name="isNew"/>), or the one
    /// the backup was written with. <c>null</c> when cancelled.
    /// </summary>
    string? AskBackupPassphrase(bool isNew);

    /// <summary>Files to upload to an entry's host (SPEC FR-60).</summary>
    string[]? PickFilesToUpload();

    /// <summary>Files to download from <paramref name="remoteFolder"/>, a connected UNC folder (SPEC FR-60).</summary>
    string[]? PickFilesToDownload(string remoteFolder);

    string? PickFolder(string title, string? initialFolder);

    /// <summary>Shows the File transfer window (one, not modal). SPEC FR-60</summary>
    void ShowTransfers(TransfersViewModel transfers);
}
