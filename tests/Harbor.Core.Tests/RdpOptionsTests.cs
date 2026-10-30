using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-13, ADR-0022: common .rdp settings edited as a form; everything else kept as text.
public class RdpOptionsTests
{
    [Fact]
    public void Empty_settings_are_the_defaults()
    {
        var (options, other) = RdpOptions.Parse(null);
        Assert.Equal(RdpOptions.Default, options);
        Assert.Equal("", other);
        Assert.Equal("", RdpOptions.Format(options, other));
    }

    [Fact]
    public void Defaults_match_what_a_launch_uses()
    {
        var (options, other) = RdpOptions.Parse(string.Join("\n", RdpFile.DefaultSettings.Values));
        Assert.Equal(RdpOptions.Default, options);
        Assert.Equal("prompt for credentials:i:0", other);
    }

    [Fact]
    public void Known_settings_become_fields_and_the_rest_is_kept()
    {
        var (options, other) = RdpOptions.Parse(
            "screen mode id:i:1\r\ndesktopwidth:i:1600\r\ndesktopheight:i:900\r\nsession bpp:i:16\r\naudiomode:i:2\r\n" +
            "redirectclipboard:i:0\r\ndrivestoredirect:s:*\r\nadministrative session:i:1\r\ngatewayhostname:s:gw.contoso.com\r\n" +
            "keyboardhook:i:7\r\ndrivestoredirect:s:C:\\;");

        Assert.Equal(RdpDisplayMode.Window, options.DisplayMode);
        Assert.Equal(new RdpResolution(1600, 900), options.Resolution);
        Assert.Equal(RdpColorDepth.HighColor16, options.ColorDepth);
        Assert.Equal(RdpAudioMode.DoNotPlay, options.Audio);
        Assert.False(options.RedirectClipboard);
        Assert.True(options.RedirectDrives);
        Assert.True(options.AdminSession);
        Assert.Equal(RdpKeyboardMode.FullScreenOnly, options.Keyboard);
        // Unknown keys and values the form cannot show stay as text, in their order.
        Assert.Equal(string.Join(Environment.NewLine, "gatewayhostname:s:gw.contoso.com", "keyboardhook:i:7", "drivestoredirect:s:C:\\;"), other);
    }

    [Fact]
    public void Format_writes_only_changed_settings_then_the_other_lines()
    {
        var options = RdpOptions.Default with
        {
            DisplayMode = RdpDisplayMode.Window, Resolution = new RdpResolution(1280, 720), RedirectDrives = true, AutoReconnect = false,
        };

        var text = RdpOptions.Format(options, "gatewayhostname:s:gw\n");

        Assert.Equal(string.Join(Environment.NewLine,
            "screen mode id:i:1", "autoreconnection enabled:i:0", "desktopwidth:i:1280", "desktopheight:i:720",
            "drivestoredirect:s:*", "gatewayhostname:s:gw"), text);
        Assert.Equal((options, "gatewayhostname:s:gw"), RdpOptions.Parse(text));
    }

    [Theory]
    [InlineData("desktopwidth:i:1600")]
    [InlineData("desktopwidth:i:100\ndesktopheight:i:900")]
    [InlineData("session bpp:s:32")]
    public void Settings_the_form_cannot_show_are_kept_as_text(string line)
    {
        var (options, other) = RdpOptions.Parse(line);
        Assert.Null(options.Resolution);
        Assert.NotEqual("", other);
        Assert.Contains(other.Split(Environment.NewLine)[0], line, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatted_settings_launch_as_set_in_the_form()
    {
        var settings = RdpOptions.Format(RdpOptions.Default with { DisplayMode = RdpDisplayMode.Window, RedirectClipboard = false }, null);
        var rdp = RdpFile.Build(new ConnectionEntry { Id = Guid.NewGuid(), Name = "x", Host = "srv", RdpSettings = settings });

        Assert.Contains("screen mode id:i:1\r\n", rdp);
        Assert.Contains("redirectclipboard:i:0\r\n", rdp);
        Assert.DoesNotContain("screen mode id:i:2", rdp);
    }
}
