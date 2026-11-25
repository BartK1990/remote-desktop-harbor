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

    // SPEC FR-75: the monitor picker.
    [Fact]
    public void Monitor_picker_writes_only_a_subset()
    {
        RdpSettingsEditorViewModel.LocalMonitorCount = () => 3;
        try
        {
            var vm = new RdpSettingsEditorViewModel("use multimon:i:1");
            Assert.Equal([0, 1, 2], vm.Monitors.Select(m => m.Id));
            Assert.All(vm.Monitors, m => Assert.True(m.IsSelected));
            Assert.DoesNotContain("selectedmonitors", vm.Build(), StringComparison.Ordinal);

            vm.Monitors[0].IsSelected = false;
            Assert.Contains("selectedmonitors:s:1,2", vm.Build(), StringComparison.Ordinal);

            vm.Monitors[1].IsSelected = false;
            vm.Monitors[2].IsSelected = false;
            Assert.NotNull(vm.Validate());

            var loaded = new RdpSettingsEditorViewModel("use multimon:i:1\nselectedmonitors:s:1,4");
            Assert.Equal(5, loaded.Monitors.Count); // an id beyond this PC's monitors is still shown
            Assert.Equal([1, 4], loaded.Monitors.Where(m => m.IsSelected).Select(m => m.Id));
        }
        finally
        {
            RdpSettingsEditorViewModel.LocalMonitorCount = () => 1;
        }
    }
}
