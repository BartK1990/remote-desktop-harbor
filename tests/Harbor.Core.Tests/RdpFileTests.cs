using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-03, FR-05, FR-10, NFR-12
public class RdpFileTests
{
    [Fact]
    public void Import_extracts_fields_and_drops_password()
    {
        var text = "﻿screen mode id:i:1\r\nfull address:s:srv01.contoso.local:3390\r\nusername:s:CONTOSO\\jdoe\r\n" +
                   "password 51:b:01000000D08C9DDF\r\naudiomode:i:2\r\n";
        var r = RdpFile.Import(text);

        Assert.Equal("srv01.contoso.local", r.Host);
        Assert.Equal(3390, r.Port);
        Assert.Equal("CONTOSO", r.Domain);
        Assert.Equal("jdoe", r.UserName);
        Assert.Contains("audiomode:i:2", r.RdpSettings);
        Assert.Contains("screen mode id:i:1", r.RdpSettings);
        Assert.DoesNotContain("password", r.RdpSettings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_overrides_managed_keys_and_keeps_custom_settings()
    {
        var entry = new ConnectionEntry
        {
            Id = Guid.NewGuid(),
            Name = "x",
            Host = "srv01",
            Port = 3390,
            Domain = "CONTOSO",
            UserName = "jdoe",
            RdpSettings = "screen mode id:i:1\nfull address:s:evil\npassword 51:b:AA\naudiomode:i:2",
        };
        var rdp = RdpFile.Build(entry);

        Assert.Contains("full address:s:srv01:3390\r\n", rdp);
        Assert.Contains("username:s:CONTOSO\\jdoe\r\n", rdp);
        Assert.Contains("screen mode id:i:1\r\n", rdp);
        Assert.Contains("audiomode:i:2\r\n", rdp);
        Assert.DoesNotContain("evil", rdp);
        Assert.DoesNotContain("password", rdp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Credential_only_entry_ignores_rdp_settings_and_uses_defaults()
    {
        var entry = new ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "x", Kind = EntryKind.CredentialOnly, Host = "srv02", UserName = "a@contoso.com",
            RdpSettings = "audiomode:i:2",
        };
        var rdp = RdpFile.Build(entry);
        Assert.Contains("full address:s:srv02\r\n", rdp);
        Assert.Contains("username:s:a@contoso.com\r\n", rdp);
        Assert.Contains("screen mode id:i:2", rdp);
        Assert.DoesNotContain("audiomode", rdp);
    }

    [Fact]
    public void Build_without_host_throws()
    {
        Assert.Throws<InvalidOperationException>(() => RdpFile.Build(ConnectionEntry.CreateNew("x")));
    }

    [Theory]
    [InlineData("[fe80::1]:3390", "[fe80::1]", 3390)]
    [InlineData("10.0.0.5", "10.0.0.5", 3389)]
    public void Import_parses_addresses(string address, string host, int port)
    {
        var r = RdpFile.Import($"full address:s:{address}");
        Assert.Equal(host, r.Host);
        Assert.Equal(port, r.Port);
    }
}
