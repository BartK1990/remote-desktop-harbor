using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-02 (host in the tree row), FR-08 (theme), ADR-0025
public class AppearanceTests
{
    [Theory]
    [InlineData("srv-app02.contoso.local", 3389, "srv-app02.contoso.local")]
    [InlineData("10.20.4.11", 3390, "10.20.4.11:3390")]
    [InlineData(null, 3389, null)]
    [InlineData("  ", 3390, null)]
    public void Address_shows_the_port_only_when_it_is_not_the_rdp_default(string? host, int port, string? expected)
    {
        var entry = ConnectionEntry.CreateNew("e") with { Host = host, Port = port };

        Assert.Equal(expected, entry.Address);
    }

    [Theory]
    [InlineData(AppTheme.Light, true, false)]
    [InlineData(AppTheme.Dark, false, true)]
    [InlineData(AppTheme.System, true, true)]
    [InlineData(AppTheme.System, false, false)]
    public void Only_the_system_theme_follows_the_windows_app_mode(AppTheme theme, bool windowsDark, bool expected) =>
        Assert.Equal(expected, theme.IsDark(windowsDark));
}
