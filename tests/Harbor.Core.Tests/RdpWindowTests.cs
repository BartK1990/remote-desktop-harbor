using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-67, ADR-0045: what Harbor's resizable Remote Desktop window connects with, and when it cannot be used.
public sealed class RdpWindowTests
{
    private static ConnectionEntry Entry(string? settings = null, string? user = "admin", string? domain = "CORP", int port = 3389) => new()
    {
        Id = Guid.NewGuid(), Name = " Web 01 ", Host = " srv01 ", Port = port, UserName = user, Domain = domain, RdpSettings = settings,
    };

    [Fact]
    public void Without_rdp_settings_the_window_follows_its_own_size_with_mstscs_defaults()
    {
        var s = RdpWindowSettings.For(Entry(port: 3390))!;
        Assert.Equal(("Web 01", "srv01", 3390, "admin", "CORP"), (s.Title, s.Host, s.Port, s.UserName, s.Domain));
        Assert.Null(s.Resolution);
        Assert.True(s.DynamicResolution);
        Assert.False(s.SmartSizing);
        Assert.True(s.RedirectClipboard);
        Assert.False(s.RedirectDrives);
        Assert.Equal((2, 32, 0, 2), (s.AuthenticationLevel, s.ColorDepth, s.Audio, s.KeyboardHook));
        Assert.True(s.AutoReconnect);
    }

    [Fact]
    public void Full_screen_and_all_monitors_are_ignored_the_window_stays_a_window()
    {
        var s = RdpWindowSettings.For(Entry("screen mode id:i:2\r\nuse multimon:i:1"))!;
        Assert.Equal(RdpWindowSettings.For(Entry()), s);
    }

    [Fact]
    public void The_entrys_rdp_settings_apply()
    {
        var s = RdpWindowSettings.For(Entry(
            "desktopwidth:i:1600\r\ndesktopheight:i:900\r\ndynamic resolution:i:0\r\nsmart sizing:i:1\r\nredirectclipboard:i:0\r\n" +
            "drivestoredirect:s:*\r\nkeyboardhook:i:1\r\naudiomode:i:2\r\nsession bpp:i:16\r\nauthentication level:i:0\r\n" +
            "administrative session:i:1\r\nautoreconnection enabled:i:0"))!;
        Assert.Equal(new RdpResolution(1600, 900), s.Resolution);
        Assert.False(s.DynamicResolution);
        Assert.True(s.SmartSizing);
        Assert.False(s.RedirectClipboard);
        Assert.True(s.RedirectDrives);
        Assert.Equal((0, 16, 2, 1), (s.AuthenticationLevel, s.ColorDepth, s.Audio, s.KeyboardHook));
        Assert.True(s.AdministrativeSession);
        Assert.False(s.AutoReconnect);
    }

    [Fact]
    public void A_domain_typed_in_the_user_name_is_split()
    {
        var s = RdpWindowSettings.For(Entry(user: @"LAB\ops", domain: null))!;
        Assert.Equal(("ops", "LAB"), (s.UserName, s.Domain));
    }

    [Theory]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:1", false)]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:4", false)]
    [InlineData("gatewayhostname:s:gw.example.com\r\ngatewayusagemethod:i:0", true)]
    public void Entries_behind_a_gateway_open_in_mstsc(string settings, bool usable) =>
        Assert.Equal(usable, RdpWindowSettings.For(Entry(settings)) is not null);

    [Fact]
    public void Other_entries_cannot_be_connected()
    {
        var other = new ConnectionEntry { Id = Guid.NewGuid(), Name = "vnc", Host = "srv", Kind = EntryKind.Other };
        Assert.Null(RdpWindowSettings.For(other));
    }

    [Theory]
    [InlineData(1921, 1080, 1920, 1080)]
    [InlineData(100, 50, 200, 200)]
    [InlineData(9000, 9000, 8192, 8192)]
    public void The_desktop_size_is_within_limits_and_even(int width, int height, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), RdpWindowSettings.DesktopSizeFor(width, height));
}
