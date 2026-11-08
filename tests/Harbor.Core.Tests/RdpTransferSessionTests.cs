using Harbor.Core.Model;
using Harbor.Core.Transfer.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-64, ADR-0042: what Harbor's own transfer window connects with, and when it cannot be used.
public sealed class RdpTransferSessionTests
{
    private static ConnectionEntry Entry(string? settings = null, string? user = "admin", string? domain = "CORP", int port = 3389) => new()
    {
        Id = Guid.NewGuid(), Name = "srv", Host = " srv01 ", Port = port, UserName = user, Domain = domain, RdpSettings = settings,
    };

    [Fact]
    public void The_window_uses_the_entrys_address_and_account()
    {
        var settings = RdpTransferSessionSettings.For(Entry(port: 3390))!;
        Assert.Equal(new RdpTransferSessionSettings("srv01", 3390, "admin", "CORP", 2, false), settings);
    }

    [Fact]
    public void A_domain_typed_in_the_user_name_is_split()
    {
        var settings = RdpTransferSessionSettings.For(Entry(user: @"LAB\ops", domain: null))!;
        Assert.Equal(("ops", "LAB"), (settings.UserName, settings.Domain));
        Assert.Null(RdpTransferSessionSettings.For(Entry(user: null, domain: null))!.UserName);
    }

    [Fact]
    public void Authentication_level_and_administrative_session_come_from_the_rdp_settings()
    {
        var settings = RdpTransferSessionSettings.For(Entry("authentication level:i:0\r\nadministrative session:i:1"))!;
        Assert.Equal((0, true), (settings.AuthenticationLevel, settings.AdministrativeSession));
        Assert.Equal(2, RdpTransferSessionSettings.For(Entry("authentication level:i:9"))!.AuthenticationLevel);
    }

    [Theory]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:1", false)]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:2", false)]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:4", false)]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:0", true)]
    [InlineData("gatewayhostname:s:\r\ngatewayusagemethod:i:1", true)]
    public void Entries_behind_a_gateway_connect_with_mstsc(string settings, bool usable) =>
        Assert.Equal(usable, RdpTransferSessionSettings.For(Entry(settings)) is not null);

    [Fact]
    public void Other_entries_cannot_be_connected()
    {
        var other = new ConnectionEntry { Id = Guid.NewGuid(), Name = "vnc", Host = "srv", Kind = EntryKind.Other };
        Assert.Null(RdpTransferSessionSettings.For(other));
    }
}
