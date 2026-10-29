using Harbor.App.Infrastructure;
using Harbor.App.ViewModels;
using Harbor.Core;
using Harbor.Core.Security;
using Harbor.Data.Providers;
using Microsoft.Data.SqlClient;

namespace Harbor.App.Tests;

// SPEC FR-26, FR-27, FR-44. Windows only (DPAPI-protected connection string).
public class SettingsViewModelTests
{
    private const string SqlLoginString = "Server=10.0.0.5;Database=Harbor;User ID=harbor;Password=s3cret;TrustServerCertificate=true";

    private static HarborSettings Remote(string connectionString)
    {
        var settings = new HarborSettings { StorageMode = StorageMode.Remote, RemoteKeyDescriptor = "SID=S-1-5-21-1" };
        SettingsStore.SetRemoteConnectionString(settings, connectionString);
        return settings;
    }

    // ------------------------------------------------------------------ FR-26: shows what is saved

    [Fact]
    public void Opens_with_the_saved_storage_key_protection_and_sign_in()
    {
        var settings = Remote(SqlLoginString);
        settings.RemoteKeyScheme = PassphraseKeyProtector.SchemeName;
        settings.AuthMode = AuthMode.EntraId;
        settings.EntraClientId = "client";

        var vm = new SettingsViewModel(settings);

        Assert.True(vm.IsRemote);
        Assert.False(vm.IsLocal);
        Assert.True(vm.UsePassphrase);
        Assert.False(vm.UseDpapiNg);
        Assert.False(vm.UseSignInKey);
        Assert.True(vm.UseEntraId);
        Assert.False(vm.UseWindowsAuth);
        Assert.False(vm.UseAnonymous);
    }

    [Fact]
    public void Saved_settings_survive_a_save_and_reopen()
    {
        var settings = Remote(SqlLoginString);
        settings.AuthMode = AuthMode.Windows;

        var first = new SettingsViewModel(settings);
        Assert.True(first.Validate());
        first.Apply();
        var reopened = new SettingsViewModel(settings);

        Assert.Equal(StorageMode.Remote, settings.StorageMode);
        Assert.True(reopened.IsRemote);
        Assert.True(reopened.UseWindowsAuth);
        Assert.Equal("10.0.0.5", reopened.DbServer);
    }

    // ------------------------------------------------------------------ FR-26: sections are independent

    [Theory]
    [InlineData(AuthMode.Windows)]
    [InlineData(AuthMode.EntraId)]
    [InlineData(AuthMode.Anonymous)]
    public void Choosing_a_sign_in_never_changes_the_storage(AuthMode mode)
    {
        var vm = new SettingsViewModel(Remote(SqlLoginString));

        // What WPF does on a click: check the new button, uncheck the others of its group.
        vm.UseWindowsAuth = mode == AuthMode.Windows;
        vm.UseEntraId = mode == AuthMode.EntraId;
        vm.UseAnonymous = mode == AuthMode.Anonymous;

        Assert.True(vm.IsRemote);
        Assert.False(vm.IsLocal);
    }

    [Fact]
    public void Choosing_a_storage_never_changes_the_sign_in()
    {
        var settings = Remote(SqlLoginString);
        settings.AuthMode = AuthMode.EntraId;
        var vm = new SettingsViewModel(settings);

        vm.IsLocal = true;
        vm.IsRemote = false;
        Assert.True(vm.UseEntraId);

        vm.IsRemote = true;
        vm.IsLocal = false;
        Assert.True(vm.UseEntraId);
        Assert.True(vm.IsRemote);
    }

    [Fact]
    public void Unchecking_a_radio_button_changes_nothing()
    {
        var vm = new SettingsViewModel(Remote(SqlLoginString));

        vm.IsLocal = false;
        vm.UseDpapiNg = false;
        vm.UsePassphrase = false;
        vm.UseEntraId = false;
        vm.UseAnonymous = false;

        Assert.True(vm.IsRemote);
        Assert.True(vm.UseSignInKey);
        Assert.True(vm.UseWindowsAuth);
    }

    // ------------------------------------------------------------------ FR-27: connection form

    [Fact]
    public void Saved_connection_string_fills_the_form()
    {
        var vm = new SettingsViewModel(Remote(SqlLoginString));

        Assert.True(vm.ShowFields);
        Assert.False(vm.EditRawConnectionString);
        Assert.Equal("10.0.0.5", vm.DbServer);
        Assert.Equal("Harbor", vm.DbName);
        Assert.Equal(DatabaseAuthentication.SqlLogin, vm.DbAuthentication);
        Assert.True(vm.UsesSqlLogin);
        Assert.Equal("harbor", vm.DbUserName);
        Assert.Equal("s3cret", vm.DbPassword);
        Assert.True(vm.DbTrustServerCertificate);
    }

    [Fact]
    public void New_settings_start_with_an_empty_form()
    {
        var vm = new SettingsViewModel(new HarborSettings());

        Assert.True(vm.IsLocal);
        Assert.True(vm.ShowFields);
        Assert.Null(vm.DbServer);
        Assert.Equal("Harbor", vm.DbName);
        Assert.Equal(DatabaseAuthentication.Windows, vm.DbAuthentication);
        Assert.True(vm.DbEncrypt);
    }

    [Fact]
    public void Form_fields_are_saved_as_an_encrypted_connection_string()
    {
        var settings = new HarborSettings();
        var vm = new SettingsViewModel(settings)
        {
            IsRemote = true,
            KeyDescriptor = "SID=S-1-5-21-1",
            DbServer = "sql01\\SQLEXPRESS",
            DbName = "Team",
            DbAuthentication = DatabaseAuthentication.SqlLogin,
            DbUserName = "u",
            DbPassword = "p@ss",
        };

        Assert.True(vm.Validate());
        vm.Apply();

        Assert.DoesNotContain("p@ss", settings.RemoteConnectionStringProtected, StringComparison.Ordinal);
        var saved = new SqlConnectionStringBuilder(SettingsStore.GetRemoteConnectionString(settings));
        Assert.Equal("sql01\\SQLEXPRESS", saved.DataSource);
        Assert.Equal("Team", saved.InitialCatalog);
        Assert.Equal("u", saved.UserID);
        Assert.Equal("p@ss", saved.Password);
        Assert.False(saved.IntegratedSecurity);
    }

    [Fact]
    public void Keywords_the_form_does_not_show_are_kept()
    {
        var settings = Remote("Server=sql01;Database=Harbor;Integrated Security=true;Application Name=Harbor;Connect Timeout=7");
        var vm = new SettingsViewModel(settings) { DbName = "Other" };

        vm.Apply();

        var saved = new SqlConnectionStringBuilder(SettingsStore.GetRemoteConnectionString(settings));
        Assert.Equal("Other", saved.InitialCatalog);
        Assert.Equal("Harbor", saved.ApplicationName);
        Assert.Equal(7, saved.ConnectTimeout);
    }

    [Fact]
    public void A_string_the_form_cannot_describe_opens_as_text_and_is_kept_unchanged()
    {
        const string adPassword = "Server=x;Database=Harbor;Authentication=Active Directory Password;User ID=a@b.c;Password=x";
        var settings = Remote(adPassword);

        var vm = new SettingsViewModel(settings);
        Assert.True(vm.EditRawConnectionString);
        Assert.False(vm.ShowFields);
        Assert.Equal(adPassword, vm.ConnectionString);

        vm.Apply();
        Assert.Equal(adPassword, SettingsStore.GetRemoteConnectionString(settings));
    }

    [Fact]
    public void Switching_to_text_and_back_carries_the_values()
    {
        var vm = new SettingsViewModel(new HarborSettings()) { IsRemote = true, DbServer = "10.1.1.1", DbName = "A" };

        vm.EditRawConnectionString = true;
        Assert.Contains("10.1.1.1", vm.ConnectionString, StringComparison.Ordinal);

        vm.ConnectionString = "Server=10.2.2.2;Database=B;User ID=x;Password=y";
        vm.EditRawConnectionString = false;

        Assert.Equal("10.2.2.2", vm.DbServer);
        Assert.Equal("B", vm.DbName);
        Assert.Equal(DatabaseAuthentication.SqlLogin, vm.DbAuthentication);
        Assert.Equal("y", vm.DbPassword);
    }

    [Fact]
    public void Switching_to_local_keeps_the_saved_connection_string()
    {
        var settings = Remote(SqlLoginString);
        var vm = new SettingsViewModel(settings) { IsLocal = true };

        vm.Apply();

        Assert.Equal(StorageMode.Local, settings.StorageMode);
        Assert.Equal(SqlLoginString, SettingsStore.GetRemoteConnectionString(settings));
    }

    // ------------------------------------------------------------------ validation

    public static TheoryData<string, Action<SettingsViewModel>> InvalidCases => new()
    {
        { "server", vm => vm.DbServer = " " },
        { "database name", vm => vm.DbName = "" },
        { "login name", vm => { vm.DbAuthentication = DatabaseAuthentication.SqlLogin; vm.DbUserName = null; } },
        { "connection string", vm => { vm.EditRawConnectionString = true; vm.ConnectionString = ""; } },
        { "client id", vm => { vm.UseEntraId = true; vm.EntraClientId = ""; } },
        { "your name", vm => { vm.UseAnonymous = true; vm.AnonymousName = "  "; } },
        { "descriptor", vm => { vm.UseDpapiNg = true; vm.KeyDescriptor = ""; } },
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validation_names_the_missing_field(string expected, Action<SettingsViewModel> breakIt)
    {
        var vm = new SettingsViewModel(Remote("Server=sql01;Database=Harbor;Integrated Security=true"));
        Assert.True(vm.Validate(), vm.Error);

        breakIt(vm);

        Assert.False(vm.Validate());
        Assert.Contains(expected, vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ FR-28: vault key protection

    [Fact]
    public void New_vaults_default_to_sign_in_protection_without_a_passphrase()
    {
        var vm = new SettingsViewModel(new HarborSettings());

        Assert.True(vm.UseSignInKey);
        Assert.False(vm.UseDpapiNg);
        Assert.False(vm.UsePassphrase);
        Assert.False(vm.ConvertKey);
    }

    [Theory]
    [InlineData(SignInKeyProtector.SchemeName)]
    [InlineData("dpapi-ng")]
    [InlineData(PassphraseKeyProtector.SchemeName)]
    public void Key_protection_choice_and_convert_request_are_saved(string scheme)
    {
        var settings = Remote(SqlLoginString);
        var vm = new SettingsViewModel(settings)
        {
            UseSignInKey = scheme == SignInKeyProtector.SchemeName,
            UseDpapiNg = scheme == "dpapi-ng",
            UsePassphrase = scheme == PassphraseKeyProtector.SchemeName,
            ConvertKey = true,
        };

        Assert.True(vm.Validate(), vm.Error);
        vm.Apply();

        Assert.Equal(scheme, settings.RemoteKeyScheme);
        Assert.True(settings.RemoteKeyConvert);
        Assert.True(new SettingsViewModel(settings).ConvertKey);
    }

    [Fact]
    public void Convert_request_is_dropped_in_local_mode()
    {
        var settings = new HarborSettings();
        var vm = new SettingsViewModel(settings) { ConvertKey = true };

        vm.Apply();

        Assert.False(settings.RemoteKeyConvert);
    }

    // ------------------------------------------------------------------ FR-44: no sign-in

    [Fact]
    public void No_sign_in_saves_the_entered_name()
    {
        var settings = new HarborSettings();
        var vm = new SettingsViewModel(settings) { UseAnonymous = true, AnonymousName = "  Bart  " };

        Assert.True(vm.Validate());
        vm.Apply();

        Assert.Equal(AuthMode.Anonymous, settings.AuthMode);
        Assert.Equal("Bart", settings.AnonymousName);
        Assert.True(new SettingsViewModel(settings).UseAnonymous);
    }
}
