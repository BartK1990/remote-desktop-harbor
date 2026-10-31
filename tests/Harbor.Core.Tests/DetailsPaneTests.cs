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
