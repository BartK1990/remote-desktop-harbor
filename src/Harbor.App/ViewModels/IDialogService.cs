namespace Harbor.App.ViewModels;

public interface IDialogService
{
    bool EditEntry(EntryEditorViewModel editor);
    bool EditSettings(SettingsViewModel settings);
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    string? PickRdpFile();
}
