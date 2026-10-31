namespace Harbor.App.ViewModels;

public interface IDialogService
{
    bool EditEntry(EntryEditorViewModel editor);
    bool EditSettings(SettingsViewModel settings);
    bool Confirm(string title, string message);

    /// <summary>
    /// A confirmation whose "yes" has consequences (SPEC FR-53): <paramref name="confirmText"/> names the action and
    /// Cancel is the default, so Enter never confirms by accident.
    /// </summary>
    bool ConfirmRisky(string title, string message, string confirmText);
    void ShowError(string title, string message);
    string? PickRdpFile();
}
