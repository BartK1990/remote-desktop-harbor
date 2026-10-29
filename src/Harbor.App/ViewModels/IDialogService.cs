namespace Harbor.App.ViewModels;

public interface IDialogService
{
    bool EditEntry(EntryEditorViewModel editor);
    bool EditSettings(SettingsViewModel settings);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    string? PickRdpFile();
}
