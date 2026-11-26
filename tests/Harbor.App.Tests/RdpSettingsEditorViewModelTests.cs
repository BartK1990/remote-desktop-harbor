using Harbor.App.ViewModels;
using Harbor.Core.Rdp;

namespace Harbor.App.Tests;

// SPEC FR-13, ADR-0022: the RDP settings form.
public class RdpSettingsEditorViewModelTests
{
    [Fact]
    public void A_common_resolution_selects_its_preset()
    {
        var vm = new RdpSettingsEditorViewModel("desktopwidth:i:1920\ndesktopheight:i:1080");
        Assert.Equal(new RdpResolution(1920, 1080), vm.Resolution.Value);
        Assert.False(vm.IsCustomResolution);
    }

    [Fact]
    public void Another_resolution_is_custom_and_is_validated()
    {
        var vm = new RdpSettingsEditorViewModel("desktopwidth:i:1700\ndesktopheight:i:1000\ngatewayhostname:s:gw");
        Assert.True(vm.IsCustomResolution);
        Assert.Equal((1700, 1000), (vm.CustomWidth, vm.CustomHeight));
        Assert.True(vm.ShowOtherSettings);
        Assert.Null(vm.Validate());

        vm.CustomWidth = 50;
        Assert.NotNull(vm.Validate());
    }

    [Fact]
    public void Build_writes_the_form_and_keeps_other_lines()
    {
        var vm = new RdpSettingsEditorViewModel("gatewayhostname:s:gw");
        vm.DisplayMode = RdpDisplayMode.Window;
        vm.Resolution = vm.Resolutions.Single(r => r.Value == new RdpResolution(1280, 720));
        vm.RedirectDrives = true;

        Assert.Equal(string.Join(Environment.NewLine,
            "screen mode id:i:1", "desktopwidth:i:1280", "desktopheight:i:720", "drivestoredirect:s:*", "gatewayhostname:s:gw"), vm.Build());
    }

    [Fact]
    public void Untouched_defaults_write_nothing()
    {
        var vm = new RdpSettingsEditorViewModel("screen mode id:i:2\nredirectclipboard:i:1");
        Assert.Equal("", vm.Build());
        Assert.False(vm.ShowOtherSettings);
    }

    // SPEC FR-75: the monitor picker lists this PC's monitors with Remote Desktop's numbers (0, 4, 5 like mstsc /l
    // shows them on a PC with a dock) and keeps the picked ones by position and size.
    private static readonly Harbor.Core.Rdp.LocalMonitor[] ThreeMonitors =
    [
        new(0, 0, 0, 1920, 1200, true), new(4, -2560, 0, 2560, 1080, false), new(5, -4480, 0, 1920, 1080, false),
    ];

    [Fact]
    public void Monitor_picker_lists_this_pcs_monitors_and_keeps_them_by_position()
    {
        Harbor.App.Infrastructure.Monitors.Current = () => ThreeMonitors;
        try
        {
            var vm = new RdpSettingsEditorViewModel("use multimon:i:1");
            Assert.Equal([0, 4, 5], vm.Monitors.Select(m => m.Id));
            Assert.Equal("2560 × 1080 · 1st left · no. 4", vm.Monitors[1].Label);
            Assert.All(vm.Monitors, m => Assert.True(m.IsSelected));
            Assert.Null(vm.PickedMonitors());

            vm.Monitors[2].IsSelected = false;
            Assert.Contains("selectedmonitors:s:0,4", vm.Build(), StringComparison.Ordinal);
            Assert.Equal([new(0, 0, 1920, 1200), new(-2560, 0, 2560, 1080)], vm.PickedMonitors()!);

            vm.Monitors[0].IsSelected = false;
            vm.Monitors[1].IsSelected = false;
            Assert.NotNull(vm.Validate());

            var loaded = new RdpSettingsEditorViewModel("use multimon:i:1\nselectedmonitors:s:4,5");
            Assert.Equal([4, 5], loaded.Monitors.Where(m => m.IsSelected).Select(m => m.Id));
        }
        finally
        {
            Harbor.App.Infrastructure.Monitors.Current = () => [];
        }
    }
}
