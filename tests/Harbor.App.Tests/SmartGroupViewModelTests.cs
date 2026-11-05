using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using Harbor.Core;
using Harbor.Core.Model;

namespace Harbor.App.Tests;

// SPEC FR-62, ADR-0040: smart groups in the entry tree and their editor. Windows only (WPF).
public class SmartGroupViewModelTests
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
        public string[]? PickFilesToUpload() => null;
        public string[]? PickFilesToDownload(string remoteFolder) => null;
        public string? PickFolder(string title, string? initialFolder) => null;
        public void ShowTransfers(TransfersViewModel transfers) { }
    }

    private static readonly SmartGroup SqlServers = new()
    {
        Name = "SQL servers",
        Rules = [new SmartGroupRule { Field = SmartGroupField.Host, Operator = SmartGroupOperator.StartsWith, Value = "sql" }],
    };

    private static MainViewModel CreateMain(out EntryViewModel sql01, out EntryViewModel web01, params SmartGroup[] groups)
    {
        var settings = new HarborSettings { SmartGroups = [.. groups] };
        var vm = new MainViewModel(settings, new NoDialogs());
        sql01 = new EntryViewModel(ConnectionEntry.CreateNew("SQL01") with { Folder = "Production/DB", Host = "sql01" });
        web01 = new EntryViewModel(ConnectionEntry.CreateNew("WEB01") with { Folder = "Production/Web", Host = "web01" });
        vm.Entries.Add(sql01);
        vm.Entries.Add(web01);
        vm.TreeFilter = TreeFilter.Pinned; // rebuilds the tree
        vm.TreeFilter = TreeFilter.All;
        return vm;
    }

    [Fact]
    public void A_top_level_smart_group_comes_first_and_lists_matching_entries()
    {
        var vm = CreateMain(out var sql01, out _, SqlServers);

        var group = Assert.IsType<SmartGroupViewModel>(vm.Tree[0]);
        Assert.Equal("SQL servers", group.Name);
        Assert.Equal(1, group.Count);
        Assert.Same(sql01, Assert.IsType<EntryLinkViewModel>(Assert.Single(group.Children)).Entry);
        Assert.IsType<FolderViewModel>(vm.Tree[1]); // the entry keeps its row in its own folder
    }

    [Fact]
    public void Selecting_a_listed_entry_selects_the_entry_and_keeps_the_row()
    {
        var vm = CreateMain(out var sql01, out _, SqlServers);
        var link = (EntryLinkViewModel)((SmartGroupViewModel)vm.Tree[0]).Children[0];

        vm.SelectFromTree(link);

        Assert.Same(link, vm.SelectedNode);
        Assert.Same(sql01, vm.Selected);
        Assert.Same(sql01.DefaultAccount, vm.SelectedAccount);
        Assert.Same(link.Group, vm.SelectedSmartGroup);
    }

    [Fact]
    public void A_group_in_a_folder_is_shown_there_and_hidden_by_a_search_without_matches()
    {
        var vm = CreateMain(out _, out _, SqlServers with { Id = Guid.NewGuid(), Folder = "Production" });
        var production = Assert.IsType<FolderViewModel>(Assert.Single(vm.Tree));
        Assert.IsType<SmartGroupViewModel>(production.Children[0]);

        vm.SearchText = "web";
        production = Assert.IsType<FolderViewModel>(Assert.Single(vm.Tree));
        Assert.DoesNotContain(production.Children, c => c is SmartGroupViewModel);
        Assert.Equal("1 of 2 entries", vm.StatusText);
    }

    [Fact]
    public void Editor_previews_matches_and_reports_problems()
    {
        var entries = new[]
        {
            ConnectionEntry.CreateNew("SQL01") with { Host = "sql01" },
            ConnectionEntry.CreateNew("WEB01") with { Host = "web01" },
        };
        var editor = new SmartGroupEditorViewModel(new SmartGroup { Name = "New smart group" }, isNew: true, [], entries);

        var rule = Assert.Single(editor.Rules);
        Assert.Equal("2 entries match (a rule is incomplete: enter the text to look for.)", editor.Preview);
        rule.Field = SmartGroupField.Host;
        rule.Operator = SmartGroupOperator.Matches;
        rule.Value = "^sql";
        Assert.Equal("1 entry matches", editor.Preview);

        rule.Value = "sql(";
        Assert.False(editor.Validate());
        Assert.Contains("not a valid regular expression", editor.Error, StringComparison.Ordinal);

        editor.RemoveRuleCommand.Execute(rule);
        editor.Name = "  Everything  ";
        Assert.True(editor.Validate());
        Assert.Equal("Everything", editor.BuildGroup().Name);
    }

    [Fact]
    public void Smart_groups_round_trip_through_the_settings_file()
    {
        var group = SqlServers with { Folder = "Production", Match = SmartGroupMatch.Any };
        var json = SettingsStore.Serialize(new HarborSettings { SmartGroups = [group], CollapsedSmartGroups = [group.Id] });

        var loaded = SettingsStore.Deserialize(json);

        var back = Assert.Single(loaded.SmartGroups);
        Assert.Equal((group.Id, group.Name, group.Folder, group.Match), (back.Id, back.Name, back.Folder, back.Match));
        Assert.Equal(group.Rules, back.Rules);
        Assert.Equal([group.Id], loaded.CollapsedSmartGroups);
        Assert.Contains("\"StartsWith\"", json, StringComparison.Ordinal); // enums are written by name
    }
}
