using Harbor.App.ViewModels;
using Harbor.Core.Model;
using Harbor.Core.Services;

namespace Harbor.App.Tests;

// SPEC FR-09, FR-33, ADR-0019: several accounts per entry, shown as rows under the entry.
public class AccountViewModelTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool EditEntry(EntryEditorViewModel editor) => false;
        public bool EditSettings(SettingsViewModel settings) => false;
        public bool Confirm(string title, string message) => false;
        public bool ConfirmRisky(string title, string message, string confirmText) => false;
        public void ShowError(string title, string message) { }
        public string? PickRdpFile() => null;
    }

    private static ConnectionEntry Entry(params ConnectionAccount[] accounts) => ConnectionEntry.CreateNew("srv") with
    {
        Host = "srv", Domain = "CONTOSO", UserName = "admin", Accounts = accounts,
    };

    [Fact]
    public void Entry_with_one_account_has_no_account_rows()
    {
        var vm = new EntryViewModel(Entry());
        Assert.Empty(vm.Children);
        Assert.Equal(@"CONTOSO\admin", vm.DefaultAccount.Name);
    }

    [Fact]
    public void Account_rows_follow_the_model_and_are_reused()
    {
        var svc = ConnectionAccount.CreateNew("svc");
        var vm = new EntryViewModel(Entry(svc));
        Assert.Equal([@"CONTOSO\admin", @"CONTOSO\svc"], vm.Children.Cast<AccountViewModel>().Select(a => a.Name));
        var svcRow = vm.FindAccount(svc.Id);

        vm.Model = Entry(svc, ConnectionAccount.CreateNew("ops")) with { Domain = "LAB" };
        Assert.Same(svcRow, vm.FindAccount(svc.Id));
        Assert.Equal([@"LAB\admin", @"LAB\svc", @"LAB\ops"], vm.Children.Cast<AccountViewModel>().Select(a => a.Name));

        vm.Model = Entry();
        Assert.Empty(vm.Children);
        Assert.Null(vm.FindAccount(svc.Id));
    }

    [Fact]
    public void Sessions_are_shown_on_the_account_they_use()
    {
        var svc = ConnectionAccount.CreateNew("svc");
        var vm = new EntryViewModel(Entry(svc));
        var now = DateTimeOffset.UtcNow;
        vm.ActiveSessions = [new ActiveSession(Guid.NewGuid(), vm.Model.Id, "bob", "Bob", null, "PC", now, now, now, @"CONTOSO\svc")];

        Assert.True(vm.IsInUse);
        Assert.False(vm.DefaultAccount.IsInUse);
        Assert.True(vm.FindAccount(svc.Id)!.IsInUse);
        Assert.Contains(@"as CONTOSO\svc", vm.PresenceDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_builds_accounts_and_their_password_updates()
    {
        var kept = ConnectionAccount.CreateNew("svc") with { PasswordCipher = [1] };
        var editor = new EntryEditorViewModel(Entry(kept), isNew: false, new NoDialogs(), []);
        editor.AddAccountCommand.Execute(null);
        editor.Accounts[1].UserName = " ops ";
        editor.Accounts[1].Password = "ops-pw";

        Assert.True(editor.Validate());
        var entry = editor.BuildEntry();
        Assert.Equal(["svc", "ops"], entry.Accounts.Select(a => a.UserName));
        Assert.Equal(new byte[] { 1 }, entry.Accounts[0].PasswordCipher);
        var updates = editor.BuildAccountSecretUpdates();
        Assert.Equal(SecretUpdateMode.Keep, updates[kept.Id].Mode);
        Assert.Equal("ops-pw", updates[entry.Accounts[1].Id].Value);

        editor.RemoveAccountCommand.Execute(editor.Accounts[1]);
        Assert.Single(editor.BuildEntry().Accounts);
    }

    [Theory]
    [InlineData("", "needs a user name")]
    [InlineData("ADMIN", "more than once")]
    public void Editor_rejects_blank_and_duplicate_accounts(string userName, string error)
    {
        var editor = new EntryEditorViewModel(Entry(), isNew: true, new NoDialogs(), []);
        editor.AddAccountCommand.Execute(null);
        editor.Accounts[0].UserName = userName;

        Assert.False(editor.Validate());
        Assert.Contains(error, editor.Error, StringComparison.Ordinal);
    }
}
