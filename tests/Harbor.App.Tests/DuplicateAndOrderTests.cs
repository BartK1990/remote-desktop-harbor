using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Services;

namespace Harbor.App.Tests;

// SPEC FR-72, FR-73, ADR-0052. Windows only (WPF).
public class DuplicateAndOrderTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool EditEntry(EntryEditorViewModel editor) => false;
        public bool EditSettings(SettingsViewModel settings) => false;
        public bool EditSmartGroup(SmartGroupEditorViewModel editor) => false;
        public bool Confirm(string title, string message) => false;
        public bool ConfirmRisky(string title, string message, string confirmText) => false;
        public void ShowError(string title, string message) { }
        public string? PickRdpFile() => null;
        public string? PickRdmCsvFile() => null;
        public string? PickBackupFileToSave(string suggestedName) => null;
        public string? PickBackupFileToOpen() => null;
        public string? AskBackupPassphrase(bool isNew) => null;
        public string[]? PickFilesToUpload() => null;
        public string[]? PickFilesToDownload(string remoteFolder) => null;
        public string? PickFolder(string title, string? initialFolder) => null;
        public void ShowTransfers(TransfersViewModel transfers) { }
    }

    [Fact]
    public void Duplicate_editor_keeps_the_copied_passwords_and_the_storage()
    {
        var copy = ConnectionEntry.CreateNew("Web 01 (copy)") with { Host = "web01", PasswordCipher = [1, 2, 3], IsLocalOnly = true };
        var editor = new EntryEditorViewModel(copy, isNew: true, new NoDialogs(), [], copyOf: "Web 01");
        editor.OfferLocalStorage(sharedAvailable: false);

        Assert.True(editor.IsNew);
        Assert.Equal("Duplicate – Web 01", editor.Title);
        Assert.True(editor.ShowStoredPassword);
        Assert.Equal(SecretUpdate.Keep, editor.BuildSecretUpdate());
        Assert.False(editor.CanChangeStorage);
        Assert.True(editor.BuildEntry().IsLocalOnly);
    }

    [Fact]
    public void Duplicate_of_a_shared_entry_is_shared_by_default_and_can_be_kept_on_this_pc()
    {
        var copy = ConnectionEntry.CreateNew("Web 01 (copy)") with { Host = "web01" };
        var editor = new EntryEditorViewModel(copy, isNew: true, new NoDialogs(), [], copyOf: "Web 01");
        editor.OfferLocalStorage(sharedAvailable: true);
        Assert.True(editor.CanChangeStorage);
        Assert.False(editor.BuildEntry().IsLocalOnly);

        editor.IsLocalOnly = true;
        Assert.True(editor.BuildEntry().IsLocalOnly);

        var offline = new EntryEditorViewModel(copy, isNew: true, new NoDialogs(), [], copyOf: "Web 01");
        offline.OfferLocalStorage(sharedAvailable: false);
        Assert.True(offline.BuildEntry().IsLocalOnly); // like a new entry while the shared database is offline
    }

    [Fact]
    public void Order_settings_survive_a_round_trip()
    {
        var settings = new HarborSettings { TreeOrder = TreeOrder.Manual, ManualOrder = ["folder:Production", "abc"] };
        var read = SettingsStore.Deserialize(SettingsStore.Serialize(settings));
        Assert.Equal(TreeOrder.Manual, read.TreeOrder);
        Assert.Equal(["folder:Production", "abc"], read.ManualOrder);
        Assert.Equal(TreeOrder.Name, SettingsStore.Deserialize("{}").TreeOrder);
    }

    [Fact]
    public void Manual_order_is_applied_to_the_tree()
    {
        var a = new EntryViewModel(ConnectionEntry.CreateNew("alpha") with { Host = "a" });
        var b = new EntryViewModel(ConnectionEntry.CreateNew("bravo") with { Host = "b" });
        var settings = new HarborSettings { TreeOrder = TreeOrder.Manual, ManualOrder = [ManualOrder.EntryKey(b.Model.Id)] };
        var vm = new MainViewModel(settings, new NoDialogs());
        vm.Entries.Add(a);
        vm.Entries.Add(b);
        vm.TreeFilter = TreeFilter.Pinned; // rebuilds the tree
        vm.TreeFilter = TreeFilter.All;

        Assert.Equal([b, a], vm.Tree.Cast<EntryViewModel>());
        Assert.True(vm.IsManualOrder);

        vm.IsReady = true;
        vm.SelectFromTree(b);
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.True(vm.MoveDownCommand.CanExecute(null));
        vm.SearchText = "a";
        Assert.False(vm.MoveDownCommand.CanExecute(null)); // only while the whole tree is shown
    }

    [Fact]
    public void Rows_can_be_dropped_only_next_to_siblings_of_the_same_kind()
    {
        var a = new EntryViewModel(ConnectionEntry.CreateNew("alpha") with { Host = "a", Folder = "Web" });
        var b = new EntryViewModel(ConnectionEntry.CreateNew("bravo") with { Host = "b", Folder = "Web" });
        var c = new EntryViewModel(ConnectionEntry.CreateNew("charlie") with { Host = "c", Folder = "DB" });
        var vm = new MainViewModel(new HarborSettings { TreeOrder = TreeOrder.Manual }, new NoDialogs());
        foreach (var e in new[] { a, b, c }) vm.Entries.Add(e);
        vm.TreeFilter = TreeFilter.Pinned; // rebuilds the tree
        vm.TreeFilter = TreeFilter.All;
        vm.IsReady = true;
        var folders = vm.Tree.OfType<FolderViewModel>().ToList();

        Assert.Equal("Order: Manual", vm.OrderText);
        Assert.True(vm.CanDrop(a, b));
        Assert.False(vm.CanDrop(a, c)); // another folder
        Assert.False(vm.CanDrop(a, folders[0])); // a folder is not an entry's sibling
        Assert.True(vm.CanDrop(folders[0], folders[1]));
        Assert.False(vm.CanDrop(a, a));
        Assert.False(vm.ResetManualOrderCommand.CanExecute(null)); // nothing moved yet

        vm.SearchText = "a";
        Assert.False(vm.CanDrag(a)); // only while the whole tree is shown
    }

    // SPEC FR-76: the editor shows the settings as this PC connects and splits them again on save.
    [Fact]
    public void Editor_keeps_own_display_settings_for_this_pc()
    {
        var entry = ConnectionEntry.CreateNew("srv") with { Host = "srv", RdpSettings = "screen mode id:i:1\naudiomode:i:2" };
        var personal = new Harbor.Core.Rdp.PersonalRdpSettings { OwnDisplay = true, Lines = "use multimon:i:1" };
        var editor = new EntryEditorViewModel(entry, isNew: false, new NoDialogs(), [], personal: personal);

        Assert.True(editor.RdpSettings.OwnDisplay);
        Assert.True(editor.RdpSettings.UseAllMonitors);
        Assert.Equal(Harbor.Core.Rdp.RdpDisplayMode.FullScreen, editor.RdpSettings.DisplayMode); // this PC's, not the entry's window

        Assert.Contains("screen mode id:i:1", editor.BuildEntry().RdpSettings, StringComparison.Ordinal); // unchanged for others
        Assert.DoesNotContain("use multimon", editor.BuildEntry().RdpSettings, StringComparison.Ordinal);
        Assert.Contains("use multimon:i:1", editor.BuildPersonalRdp()!.Lines, StringComparison.Ordinal);

        editor.RdpSettings.OwnDisplay = false; // back to the entry's settings, edited for everyone
        Assert.Contains("use multimon:i:1", editor.BuildEntry().RdpSettings, StringComparison.Ordinal);
        Assert.Null(editor.BuildPersonalRdp());
    }

    [Fact]
    public void Personal_settings_survive_a_round_trip()
    {
        var id = Guid.NewGuid();
        var settings = new HarborSettings { PersonalRdp = { [id] = new() { OwnDisplay = true, OwnResources = true, Lines = "selectedmonitors:s:0,1" } } };
        var read = SettingsStore.Deserialize(SettingsStore.Serialize(settings));
        Assert.Equal(settings.PersonalRdp[id], read.PersonalRdp[id]);
    }
}
