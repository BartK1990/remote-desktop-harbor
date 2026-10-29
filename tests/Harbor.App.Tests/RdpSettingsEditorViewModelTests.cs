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
}
