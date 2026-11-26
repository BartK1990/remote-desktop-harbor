using Harbor.Core.Model;
using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-76, ADR-0054: what a user keeps for an entry on this PC only.
public class PersonalRdpTests
{
    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    public void Monitors_are_always_personal_and_the_rest_stays_shared()
    {
        var (shared, personal) = PersonalRdp.Split(Lines("use multimon:i:1", "selectedmonitors:s:0,1", "audiomode:i:2"),
            ownDisplay: false, sharedBefore: null);

        Assert.Equal(Lines("use multimon:i:1", "audiomode:i:2"), shared);
        Assert.False(personal!.OwnDisplay);
        Assert.Equal("selectedmonitors:s:0,1", personal.Lines);
    }

    [Fact]
    public void Own_display_keeps_the_entrys_display_for_everyone_else()
    {
        var before = Lines("screen mode id:i:1", "desktopwidth:i:1280", "desktopheight:i:720", "selectedmonitors:s:2", "audiomode:i:2");
        var edited = Lines("use multimon:i:1", "selectedmonitors:s:0,1", "audiomode:i:1");

        var (shared, personal) = PersonalRdp.Split(edited, ownDisplay: true, sharedBefore: before);

        // Non-display edits are shared; the entry's display lines stay, except monitors, which are never shared.
        Assert.Equal(Lines("audiomode:i:1", "screen mode id:i:1", "desktopwidth:i:1280", "desktopheight:i:720"), shared);
        Assert.True(personal!.OwnDisplay);
        Assert.Equal(Lines("use multimon:i:1", "selectedmonitors:s:0,1"), personal.Lines);
    }

    [Fact]
    public void Own_display_replaces_every_display_setting_of_the_entry_on_this_pc()
    {
        var shared = Lines("screen mode id:i:1", "desktopwidth:i:1280", "desktopheight:i:720", "audiomode:i:2");

        // Full screen at the local size is the default: no display lines at all, and none of the entry's apply.
        var merged = PersonalRdp.Merge(shared, new PersonalRdpSettings { OwnDisplay = true });
        Assert.Equal("audiomode:i:2", merged);

        // Without own display only the monitors are replaced.
        Assert.Equal(Lines("screen mode id:i:1", "desktopwidth:i:1280", "desktopheight:i:720", "audiomode:i:2", "selectedmonitors:s:1"),
            PersonalRdp.Merge(shared, new PersonalRdpSettings { Lines = "selectedmonitors:s:1" }));
        Assert.Same(shared, PersonalRdp.Merge(shared, null));
    }

    [Fact]
    public void Apply_changes_only_remote_desktop_entries()
    {
        var personal = new PersonalRdpSettings { Lines = "selectedmonitors:s:1" };
        var rdp = ConnectionEntry.CreateNew("a") with { Host = "h", RdpSettings = "use multimon:i:1" };
        Assert.Contains("selectedmonitors:s:1", PersonalRdp.Apply(rdp, personal).RdpSettings, StringComparison.Ordinal);

        var other = ConnectionEntry.CreateNew("b", EntryKind.CredentialOnly) with { Host = "h" };
        Assert.Same(other, PersonalRdp.Apply(other, personal));
    }

    [Fact]
    public void Nothing_personal_gives_no_settings()
    {
        var (_, personal) = PersonalRdp.Split("audiomode:i:2", ownDisplay: false, sharedBefore: null);
        Assert.Null(personal);
    }

    [Fact]
    public void Own_local_resources_are_kept_on_this_pc()
    {
        var before = Lines("audiomode:i:2", "drivestoredirect:s:*", "redirectprinters:i:0", "redirectsmartcards:i:0", "keyboardhook:i:1");
        var edited = Lines("audiocapturemode:i:1", "redirectclipboard:i:0", "keyboardhook:i:0");

        var (shared, personal) = PersonalRdp.Split(edited, ownDisplay: false, sharedBefore: before, ownResources: true);

        // Windows key combinations stay in the entry; audio, microphone, drives, printers, clipboard and smart cards
        // are kept on this PC.
        Assert.Equal(Lines("keyboardhook:i:0", "audiomode:i:2", "drivestoredirect:s:*", "redirectprinters:i:0", "redirectsmartcards:i:0"), shared);
        Assert.True(personal!.OwnResources);
        Assert.Equal(Lines("audiocapturemode:i:1", "redirectclipboard:i:0"), personal.Lines);

        // On this PC: none of the entry's lines of that group, only the user's (printers and smart cards back to on).
        Assert.Equal(Lines("keyboardhook:i:0", "audiocapturemode:i:1", "redirectclipboard:i:0"), PersonalRdp.Merge(shared, personal));
    }
}
