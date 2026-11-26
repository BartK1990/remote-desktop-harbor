using Harbor.Core.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-75, ADR-0054: monitors are picked by position and size; their Remote Desktop numbers are looked up when
// connecting, because they change (0, 4, 5 on a PC with a dock, as mstsc /l shows).
public class MonitorSelectionTests
{
    private static readonly LocalMonitor Primary = new(0, 0, 0, 1920, 1200, true);
    private static readonly LocalMonitor Left = new(4, -2560, 0, 2560, 1080, false);
    private static readonly LocalMonitor FarLeft = new(5, -4480, 0, 1920, 1080, false);
    private static readonly LocalMonitor[] Desk = [Primary, Left, FarLeft];

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 0)]
    [InlineData(@"\\.\DISPLAY5", 4)]
    [InlineData(@"\\.\display12", 11)]
    [InlineData("something else", null)]
    [InlineData(null, null)]
    public void Number_comes_from_the_device_name(string? name, int? expected) =>
        Assert.Equal(expected, MonitorSelection.IdFromDeviceName(name));

    // A PC whose mstsc /l showed 0: (0, 0), 4: (3840, 0), 5: (1920, 0) while the screens were DISPLAY1, DISPLAY9 and
    // DISPLAY10: the numbers are places in Windows' list of display devices, which skips DISPLAY5 to DISPLAY8.
    [Theory]
    [InlineData(@"\\.\DISPLAY1", 0)]
    [InlineData(@"\\.\DISPLAY9", 4)]
    [InlineData(@"\\.\display10", 5)]
    [InlineData(@"\\.\DISPLAY7", 6)] // not in the list: the name less one
    public void Number_is_the_place_in_the_display_device_list(string name, int expected)
    {
        string[] devices = [@"\\.\DISPLAY1", @"\\.\DISPLAY2", @"\\.\DISPLAY3", @"\\.\DISPLAY4", @"\\.\DISPLAY9", @"\\.\DISPLAY10"];
        Assert.Equal(expected, MonitorSelection.IdFor(name, devices));
    }

    [Fact]
    public void Monitors_are_listed_like_mstsc_and_ordered_by_place()
    {
        LocalMonitor middle = new(5, 1920, 0, 1920, 1080, false), right = new(4, 3840, 0, 1920, 1080, false);
        LocalMonitor primary = new(0, 0, 0, 1920, 1200, true);
        Assert.Equal("4: 1920 x 1080; (3840, 0, 5759, 1079)", MonitorSelection.MstscLine(right));
        Assert.Equal("0: 1920 x 1200; (0, 0, 1919, 1199)", MonitorSelection.MstscLine(primary));
        Assert.Equal([primary, middle, right], MonitorSelection.ByPlace([right, primary, middle]));
        Assert.Equal("1920 × 1080 · 1st right · no. 5", MonitorSelection.Describe(middle, [primary, middle, right]));
    }

    [Fact]
    public void Picked_monitors_get_their_numbers_of_the_moment()
    {
        List<MonitorRef> picked = [MonitorSelection.RefOf(Primary), MonitorSelection.RefOf(Left)];
        Assert.Equal([0, 4], MonitorSelection.Resolve(picked, Desk));

        // Docked again: the same screens now have other numbers.
        LocalMonitor[] redocked = [Primary with { Id = 0 }, Left with { Id = 6 }, FarLeft with { Id = 7 }];
        Assert.Equal([0, 6], MonitorSelection.Resolve(picked, redocked));
    }

    [Fact]
    public void A_moved_monitor_is_found_by_size_and_a_missing_one_left_out()
    {
        List<MonitorRef> picked = [MonitorSelection.RefOf(Left), MonitorSelection.RefOf(FarLeft)];
        LocalMonitor[] laptop = [Primary, Left with { X = 1920, Id = 2 }]; // the 2560 screen now on the right; no 1920 x 1080
        Assert.Equal([2], MonitorSelection.Resolve(picked, laptop));
        Assert.Empty(MonitorSelection.Resolve(picked, [Primary]));
    }

    [Fact]
    public void Monitors_are_named_by_size_and_place()
    {
        Assert.Equal("1920 × 1200 · primary · no. 0", MonitorSelection.Describe(Primary, Desk));
        Assert.Equal("2560 × 1080 · 1st left · no. 4", MonitorSelection.Describe(Left, Desk));
        Assert.Equal("1920 × 1080 · 2nd left · no. 5", MonitorSelection.Describe(FarLeft, Desk));
        Assert.Equal("2560 × 1080 · left · no. 4", MonitorSelection.Describe(Left, [Primary, Left]));
    }

    [Fact]
    public void Personal_monitors_become_a_selectedmonitors_line_when_connecting()
    {
        var personal = new PersonalRdpSettings { Monitors = [MonitorSelection.RefOf(Left), MonitorSelection.RefOf(FarLeft)] };
        Assert.Equal("use multimon:i:1" + Environment.NewLine + "selectedmonitors:s:4,5",
            PersonalRdp.Merge("use multimon:i:1\nselectedmonitors:s:1", personal, Desk));

        // None of them here any more: no line, so all monitors are used.
        Assert.Equal("use multimon:i:1", PersonalRdp.Merge("use multimon:i:1", personal, [Primary]));
    }

    [Fact]
    public void With_monitors_replaces_a_numbers_line()
    {
        var personal = new PersonalRdpSettings { OwnDisplay = true, Lines = "use multimon:i:1\nselectedmonitors:s:0,4" };
        var stored = PersonalRdp.WithMonitors(personal, [MonitorSelection.RefOf(Primary)])!;
        Assert.Equal("use multimon:i:1", stored.Lines);
        Assert.Equal([MonitorSelection.RefOf(Primary)], stored.Monitors!);
        Assert.Null(PersonalRdp.WithMonitors(new PersonalRdpSettings { Lines = "selectedmonitors:s:0" }, null));
    }
}
