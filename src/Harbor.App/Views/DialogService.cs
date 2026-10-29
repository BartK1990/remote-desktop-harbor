using System.Windows;
using Harbor.App.ViewModels;
using Microsoft.Win32;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Harbor.App.Views;

public sealed class DialogService : IDialogService
{
    public Window? Owner { get; set; }

    public bool EditEntry(EntryEditorViewModel editor) => new EntryEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool EditSettings(SettingsViewModel settings) => new SettingsWindow(settings) { Owner = Owner }.ShowDialog() == true;

    public Task<bool> ConfirmAsync(string title, string message) => ConfirmAsync(Owner, title, message);

    public Task ShowErrorAsync(string title, string message) => ShowErrorAsync(Owner, title, message);

    public string? PickRdpFile()
    {
        var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp|All files (*.*)|*.*", Title = "Import .rdp file" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    // WPF UI message boxes (ADR-0020): Fluent styled, follow the app theme. An empty button text hides the button.
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message) =>
        await Create(owner, title, message, primary: "Yes", close: "No").ShowDialogAsync() == MessageBoxResult.Primary;

    public static Task ShowErrorAsync(Window? owner, string title, string message) =>
        Create(owner, title, message, primary: "", close: "OK").ShowDialogAsync();

    private static MessageBox Create(Window? owner, string title, string message, string primary, string close)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            MaxWidth = 520,
        };
        if (owner is { IsLoaded: true })
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return box;
    }
}
