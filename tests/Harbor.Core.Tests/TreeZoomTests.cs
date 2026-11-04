using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-55, ADR-0034
public class TreeZoomTests
{
    [Fact]
    public void Steps_by_ten_percent_between_80_and_160()
    {
        Assert.Equal(1.1, TreeZoom.In(1.0));
        Assert.Equal(0.9, TreeZoom.Out(1.0));
        Assert.Equal(1.6, TreeZoom.In(1.6));
        Assert.Equal(0.8, TreeZoom.Out(0.8));
    }

    [Fact]
    public void Repeated_steps_land_on_whole_levels()
    {
        var zoom = TreeZoom.Default;
        for (var i = 0; i < 3; i++) zoom = TreeZoom.In(zoom);
        Assert.Equal(1.3, zoom);
        Assert.Equal("130%", TreeZoom.Label(zoom));
        for (var i = 0; i < 3; i++) zoom = TreeZoom.Out(zoom);
        Assert.True(TreeZoom.IsDefault(zoom));
    }

    [Theory]
    [InlineData(0.1, 0.8)]
    [InlineData(5.0, 1.6)]
    [InlineData(1.24, 1.2)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void Stored_values_are_read_as_a_valid_level(double stored, double expected) =>
        Assert.Equal(expected, TreeZoom.Normalize(stored));

    [Fact]
    public void Defaults_to_100_percent() => Assert.Equal("100%", TreeZoom.Label(new HarborSettings().TreeZoom));
}
