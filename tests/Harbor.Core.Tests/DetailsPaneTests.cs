using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-33, FR-47, ADR-0026: what the details pane shows.
public class DetailsPaneTests
{
    [Fact]
    public void Default_settings_summarize_as_a_launch_opens()
    {
        Assert.Equal(
            [
                new RdpSummaryLine("Display", "full screen · local size"),
                new RdpSummaryLine("Redirect", "clipboard, printers, smart cards"),
                new RdpSummaryLine("Security", "warn if unverified"),
            ],
            RdpSummary.Describe(null));
    }

    [Fact]
    public void Changed_settings_and_unknown_lines_are_summarized()
    {
        var lines = RdpSummary.Describe(
            "screen mode id:i:1\r\ndesktopwidth:i:1920\r\ndesktopheight:i:1080\r\nuse multimon:i:1\r\nsession bpp:i:16\r\n" +
            "redirectclipboard:i:0\r\nredirectprinters:i:0\r\nredirectsmartcards:i:0\r\n" +
            "authentication level:i:1\r\nadministrative session:i:1\r\nautoreconnection enabled:i:0\r\n" +
            "gatewayhostname:s:gw.contoso.com\r\nalternate shell:s:cmd.exe");

        Assert.Equal(
            [
                new RdpSummaryLine("Display", "window · 1920×1080 · all monitors · 16-bit"),
                new RdpSummaryLine("Redirect", "nothing"),
                new RdpSummaryLine("Security", "do not connect if unverified"),
                new RdpSummaryLine("Session", "admin, no auto-reconnect"),
                new RdpSummaryLine("Other", "2 more .rdp settings"),
            ],
            lines);
    }

    [Fact]
    public void Connecting_takes_over_sessions_with_the_same_or_an_unknown_account()
    {
        var now = DateTimeOffset.UtcNow;
        ActiveSession Session(string? account) =>
            new(Guid.NewGuid(), Guid.NewGuid(), "jane", "Jane", null, "PC-42", now, now, now.AddMinutes(2), account);
        var sameAccount = Session(@"CONTOSO\ops");
        var otherAccount = Session(@"CONTOSO\svc");
        var olderClient = Session(null);
        var sessions = new[] { sameAccount, otherAccount, olderClient };

        Assert.Equal([sameAccount, olderClient], sessions.TakenOverBy(@"contoso\OPS"));
        Assert.Equal(sessions, sessions.TakenOverBy(null));
    }
}

// SPEC FR-46: pasted host:port and DOMAIN\user are split into the editor's fields.
public class EntryInputTests
{
    [Theory]
    [InlineData("srv01", "srv01", null)]
    [InlineData("srv01.contoso.local:3390", "srv01.contoso.local", 3390)]
    [InlineData("10.20.4.12:3389", "10.20.4.12", 3389)]
    [InlineData("[fe80::1]:3390", "[fe80::1]", 3390)]
    [InlineData("fe80::1", "fe80::1", null)]
    [InlineData("srv:abc", "srv:abc", null)]
    public void Host_and_port_are_split(string value, string host, int? port) =>
        Assert.Equal((host, port), EntryInput.SplitHostPort(value));

    [Theory]
    [InlineData(@"CONTOSO\jdoe", "CONTOSO", "jdoe")]
    [InlineData("jdoe", null, "jdoe")]
    [InlineData("jdoe@contoso.com", null, "jdoe@contoso.com")]
    [InlineData(@".\admin", null, @".\admin")]
    [InlineData(@"CONTOSO\", null, @"CONTOSO\")]
    public void Domain_and_user_are_split(string value, string? domain, string user) =>
        Assert.Equal((domain, user), EntryInput.SplitDomainUser(value));
}
