using Harbor.App.ViewModels;
using Harbor.Core;
using Harbor.Core.Model;

namespace Harbor.App.Tests;

// SPEC FR-48, FR-49, FR-50, ADR-0028. Windows only (WPF).
public class QuickConnectViewModelTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool EditEntry(EntryEditorViewModel editor) => false;
        public bool EditSettings(SettingsViewModel settings) => false;
        public bool Confirm(string title, string message) => false;
        public void ShowError(string title, string message) { }
        public string? PickRdpFile() => null;
    }

    private static readonly ConnectionAccount Ops = ConnectionAccount.CreateNew("ops");

    private static MainViewModel CreateMain(out EntryViewModel app01, out EntryViewModel sql01)
    {
        var vm = new MainViewModel(new HarborSettings(), new NoDialogs());
        app01 = new EntryViewModel(ConnectionEntry.CreateNew("APP01") with
        {
            Folder = "Production/Web", Host = "srv-app01", Domain = "CONTOSO", UserName = "svc", Accounts = [Ops],
        });
        sql01 = new EntryViewModel(ConnectionEntry.CreateNew("SQL01") with { Folder = "Staging", Host = "sql01.contoso.local" });
        vm.Entries.Add(app01);
        vm.Entries.Add(sql01);
        return vm;
    }

    [Fact]
    public void Palette_lists_entries_and_their_accounts_and_reveals_the_chosen_account()
    {
        var vm = CreateMain(out var app01, out _);
        vm.OpenQuickConnectCommand.Execute(null);
        vm.QuickQuery = "ops";

        var result = Assert.Single(vm.QuickResults);
        Assert.Same(app01, result.Entry);
        Assert.Equal(@"CONTOSO\ops", result.User);

        vm.QuickConnectRevealCommand.Execute(null);
        Assert.False(vm.IsQuickConnectOpen);
        Assert.Equal(@"CONTOSO\ops", vm.SelectedAccount!.QualifiedUserName);
        Assert.Same(app01, vm.Selected);
    }

    [Fact]
    public void Palette_searches_folders_and_hosts()
    {
        var vm = CreateMain(out _, out var sql01);
        vm.QuickQuery = "staging contoso";
        Assert.Same(sql01, Assert.Single(vm.QuickResults).Entry);
    }

    [Fact]
    public void Tree_filter_shows_only_pinned_entries_and_entries_get_their_environment()
    {
        var vm = CreateMain(out var app01, out var sql01);
        app01.IsPinned = true;

        vm.TreeFilter = TreeFilter.Pinned;
        var folder = Assert.IsType<FolderViewModel>(Assert.Single(vm.Tree));
        Assert.Equal(EnvironmentKind.Production, folder.Environment);
        Assert.Equal("PROD", app01.EnvironmentLabel);
        Assert.Equal("STG", sql01.EnvironmentLabel);

        vm.TreeFilter = TreeFilter.All;
        Assert.Equal(2, vm.Tree.Count);
    }
}
