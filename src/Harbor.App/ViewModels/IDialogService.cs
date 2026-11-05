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

    /// <summary>Files to upload to an entry's host (SPEC FR-60).</summary>
    string[]? PickFilesToUpload();

    /// <summary>Files to download from <paramref name="remoteFolder"/>, a connected UNC folder (SPEC FR-60).</summary>
    string[]? PickFilesToDownload(string remoteFolder);

    string? PickFolder(string title, string? initialFolder);

    /// <summary>Shows the File transfer window (one, not modal). SPEC FR-60</summary>
    void ShowTransfers(TransfersViewModel transfers);
}
