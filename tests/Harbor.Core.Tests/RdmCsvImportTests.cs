using Harbor.Core.Import;
using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-54, ADR-0032: import from a Remote Desktop Manager CSV export.
public class RdmCsvImportTests
{
    private const string Header =
        "﻿ConnectionType,ConnectionSubType,SubMode,Name,Group,Description,Keywords,Expiration,Parent,Host,Port,CredentialUserName," +
        "CredentialDomain,CredentialPassword,OpenInConsole,URL,DataEntry.WebUserName,DataEntry.WebDomain,DataEntry.WebPassword,WebUrl\r\n";

    private static ImportResult Parse(params string[] rows) => RdmCsvImport.Parse(Header + string.Join("\r\n", rows) + "\r\n");

    [Fact]
    public void Rdp_session_becomes_an_rdp_connection_with_folder_credentials_and_notes()
    {
        var result = Parse(
            "RDP (Microsoft Remote Desktop),,0,web01,Production\\Web,\"Line one, with comma\r\nsays \"\"hi\"\"\",,,,10.0.0.5,3390,jdoe,CONTOSO,s3cret,Yes,,,,,rdm://select?x");

        var item = Assert.Single(result.Entries);
        var e = item.Entry;
        Assert.Equal(EntryKind.RdpConnection, e.Kind);
        Assert.Equal("web01", e.Name);
        Assert.Equal("Production/Web", e.Folder);
        Assert.Equal(("10.0.0.5", 3390), (e.Host, e.Port));
        Assert.Equal(("CONTOSO", "jdoe"), (e.Domain, e.UserName));
        Assert.Equal("Line one, with comma\r\nsays \"hi\"", e.Notes);
        Assert.Equal("administrative session:i:1", e.RdpSettings);
        Assert.Equal("s3cret", item.Password);
        Assert.Null(e.PasswordCipher);
        Assert.DoesNotContain("s3cret", item.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Credentials_become_credentials_only_and_split_domain_user()
    {
        var result = Parse(
            "Username and password,,0,Admin,,,,,,,,CONTOSO\\admin,,pw,,,,,,",
            "Username and password,,0,UPN,,,tag1,,,,,jdoe@contoso.com,,,,,,,,");

        Assert.Equal(2, result.Entries.Count);
        var a = result.Entries[0].Entry;
        Assert.Equal(EntryKind.CredentialOnly, a.Kind);
        Assert.Equal(("CONTOSO", "admin"), (a.Domain, a.UserName));
        Assert.Null(a.Host);
        Assert.Equal(ConnectionEntry.DefaultRdpPort, a.Port);
        Assert.Null(a.Folder);

        var b = result.Entries[1];
        Assert.Equal((null, "jdoe@contoso.com"), (b.Entry.Domain, b.Entry.UserName));
        Assert.Equal("Keywords: tag1", b.Entry.Notes);
        Assert.Null(b.Password);
    }

    [Fact]
    public void Folder_rows_and_blank_rows_are_skipped()
    {
        var result = Parse(
            "Folder,,0,Web,Production\\Web,,,,,,,,,,,,,,,",
            "RDP (Microsoft Remote Desktop),,0,srv,,,,,,srv:3391,,,,,No,,,,,",
            ",,,,,,,,,,,,,,,,,,,");

        var e = Assert.Single(result.Entries).Entry;
        Assert.Equal(("srv", 3391), (e.Host, e.Port));
        Assert.Null(e.RdpSettings);
        Assert.Equal(1, result.SkippedFolders);
    }

    // ADR-0033: VNC, TeamViewer, web, VPN… keep their credentials as Other entries, never launched.
    [Fact]
    public void Other_connection_types_become_other_entries_that_name_their_type()
    {
        var result = Parse(
            "VNC,TightVNC,0,vnc,Lab,\"desc\",,,,10.0.0.9,5900,admin,,pw,,,,,,",
            "Website,Default,0,portal,,,,,,,,,,,,https://portal.contoso.com,,,,");

        var vnc = result.Entries[0];
        Assert.Equal(EntryKind.Other, vnc.Entry.Kind);
        Assert.Equal(("10.0.0.9", 5900), (vnc.Entry.Host, vnc.Entry.Port));
        Assert.Equal("Type: VNC (TightVNC)\r\n\r\ndesc", vnc.Entry.Notes);
        Assert.Equal("pw", vnc.Password);
        Assert.False(vnc.Entry.CanLaunch);

        var web = result.Entries[1].Entry;
        Assert.Equal(EntryKind.Other, web.Kind);
        Assert.Equal("Type: Website\r\nURL: https://portal.contoso.com", web.Notes);
    }

    [Fact]
    public void Web_credentials_fill_an_empty_entry_or_become_an_additional_account()
    {
        var result = Parse(
            "Website (legacy),,0,a,,,,,,h,,,,,,,webuser,WEB,webpw,",
            "Website (legacy),,0,b,,,,,,h,,admin,,pw,,,webuser,,webpw,");

        var a = result.Entries[0];
        Assert.Equal(("WEB", "webuser", "webpw"), (a.Entry.Domain, a.Entry.UserName, a.Password));
        Assert.Empty(a.Entry.Accounts);

        var b = result.Entries[1];
        Assert.Equal(("admin", "pw"), (b.Entry.UserName, b.Password));
        var account = Assert.Single(b.Entry.Accounts);
        Assert.Equal("webuser", account.UserName);
        Assert.Equal("webpw", b.AccountPasswords![account.Id]);
        Assert.DoesNotContain("webpw", b.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_are_found_by_name_and_semicolons_are_accepted()
    {
        var result = RdmCsvImport.Parse("Name;Host;ConnectionType\nsrv;h1;RDP (Microsoft Remote Desktop)\n");

        var e = Assert.Single(result.Entries).Entry;
        Assert.Equal(("srv", "h1"), (e.Name, e.Host));
    }

    [Fact]
    public void A_file_without_the_rdm_columns_is_rejected()
    {
        Assert.Throws<FormatException>(() => RdmCsvImport.Parse("a,b,c\n1,2,3\n"));
        Assert.Throws<FormatException>(() => RdmCsvImport.Parse(""));
    }

    [Fact]
    public void Entries_already_in_the_catalog_are_left_out_but_repeats_in_the_file_are_kept()
    {
        var result = Parse(
            "RDP (Microsoft Remote Desktop),,0,srv,Prod,,,,,h1,,admin,CONTOSO,,,,,,,",
            "RDP (Microsoft Remote Desktop),,0,srv,Prod,,,,,h1,,admin,CONTOSO,,,,,,,",
            "RDP (Microsoft Remote Desktop),,0,srv,Prod,,,,,h2,,admin,CONTOSO,,,,,,,",
            "RDP (Microsoft Remote Desktop),,0,new,Prod,,,,,h3,,,,,,,,,,");
        var existing = ConnectionEntry.CreateNew("SRV") with { Folder = "prod", Host = "h2", Domain = "contoso", UserName = "admin" };

        var toImport = RdmCsvImport.WithoutExisting(result.Entries, [existing]);

        Assert.Equal(["h1", "h1", "h3"], toImport.Select(i => i.Entry.Host));
    }
}
