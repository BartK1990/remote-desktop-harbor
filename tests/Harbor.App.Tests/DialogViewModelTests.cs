using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Services;
using Harbor.Data.Providers;

namespace Harbor.App.Tests;

// SPEC FR-08, FR-45, FR-46, ADR-0027. Windows only (WPF, DPAPI).
public class DialogViewModelTests
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

    private static EntryEditorViewModel Editor(ConnectionEntry entry, bool isNew = false) => new(entry, isNew, new NoDialogs(), []);

    [Fact]
    public void Pasted_host_port_and_domain_user_are_split_on_save()
    {
        var editor = Editor(ConnectionEntry.CreateNew("srv"), isNew: true);
        editor.Host = " srv01.contoso.local:3390 ";
        editor.UserName = @"CONTOSO\jdoe";
        editor.AddAccountCommand.Execute(null);
        editor.Accounts[0].UserName = @"LAB\ops";

        Assert.True(editor.Validate());
        var entry = editor.BuildEntry();
        Assert.Equal(("srv01.contoso.local", 3390), (entry.Host, entry.Port));
        Assert.Equal(("CONTOSO", "jdoe"), (entry.Domain, entry.UserName));
        Assert.Equal(("LAB", "ops"), (entry.Accounts[0].Domain, entry.Accounts[0].UserName));
    }

    [Fact]
    public void A_typed_port_wins_over_a_different_port_in_the_host()
    {
        var editor = Editor(ConnectionEntry.CreateNew("srv"), isNew: true);
        editor.Host = "srv:3390";
        editor.Port = 4000;
        editor.Domain = "LAB";
        editor.UserName = @"CONTOSO\jdoe";

        editor.Validate();
        Assert.Equal("srv:3390", editor.Host);
        Assert.Equal(4000, editor.Port);
        Assert.Equal(@"CONTOSO\jdoe", editor.UserName); // a domain is already set
    }

    [Theory]
    [InlineData("", "srv", 3389, "General", "NameBox")]
    [InlineData("n", "", 3389, "General", "HostBox")]
    [InlineData("n", "srv", 0, "General", "PortBox")]
    public void A_failed_save_says_where_the_problem_is(string name, string host, int port, string tab, string field)
    {
        var editor = Editor(ConnectionEntry.CreateNew("srv"), isNew: true);
        editor.Name = name;
        editor.Host = host;
        editor.Port = port;

        Assert.False(editor.Validate());
        Assert.Equal((tab, field), editor.ErrorLocation);
    }

    [Fact]
    public void Stored_password_is_kept_replaced_or_removed_explicitly()
    {
        var editor = Editor(ConnectionEntry.CreateNew("srv") with { Host = "srv", PasswordCipher = [1] });
        Assert.True(editor.ShowStoredPassword);
        Assert.Equal(SecretUpdateMode.Keep, editor.BuildSecretUpdate().Mode);

        editor.ReplacePasswordCommand.Execute(null);
        editor.Password = "new";
        Assert.False(editor.ShowStoredPassword);
        Assert.Equal("new", editor.BuildSecretUpdate().Value);

        editor.RemovePasswordCommand.Execute(null);
        Assert.True(editor.PasswordRemovalPending);
        Assert.Equal(SecretUpdateMode.Clear, editor.BuildSecretUpdate().Mode);

        editor.KeepPasswordCommand.Execute(null);
        Assert.True(editor.ShowStoredPassword);
        Assert.Equal(SecretUpdateMode.Keep, editor.BuildSecretUpdate().Mode);
    }

    [Fact]
    public void Saving_only_a_new_theme_needs_no_restart()
    {
        var settings = new HarborSettings { StorageMode = StorageMode.Remote, RemoteKeyDescriptor = "SID=S-1-5-21-1" };
        SettingsStore.SetRemoteConnectionString(settings, "Server=sql01;Database=Harbor;Integrated Security=true");

        Assert.False(new SettingsViewModel(settings).Apply());

        var vm = new SettingsViewModel(settings) { Theme = AppTheme.Dark };
        Assert.False(vm.Apply());
        Assert.Equal(AppTheme.Dark, settings.Theme);

        Assert.True(new SettingsViewModel(settings) { DbName = "Other" }.Apply());
    }

    [Fact]
    public void Connection_string_preview_masks_the_password()
    {
        var vm = new SettingsViewModel(new HarborSettings())
        {
            IsRemote = true,
            DbServer = "sql01",
            DbName = "Harbor",
            DbAuthentication = DatabaseAuthentication.SqlLogin,
            DbUserName = "harbor_user",
            DbPassword = "s3cret",
        };

        Assert.Contains("harbor_user", vm.ConnectionStringPreview, StringComparison.Ordinal);
        Assert.Contains(ConnectionStringMask.Mask, vm.ConnectionStringPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", vm.ConnectionStringPreview, StringComparison.Ordinal);
    }
}
