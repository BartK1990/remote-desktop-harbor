using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-56
public class ListPaneWidthTests
{
    [Fact]
    public void Defaults_to_the_original_two_to_three_split() =>
        Assert.Equal(0.4, new HarborSettings().ListPaneWidth);

    [Fact]
    public void Share_comes_from_the_widths_on_screen()
    {
        Assert.Equal(0.25, ListPaneWidth.FromWidths(300, 900));
        Assert.Equal(0.5, ListPaneWidth.FromWidths(600, 600));
        Assert.Equal(0.333, ListPaneWidth.FromWidths(1, 2));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(600, 0)]
    [InlineData(double.NaN, 600)]
    public void Widths_before_layout_give_the_default(double list, double details) =>
        Assert.Equal(ListPaneWidth.Default, ListPaneWidth.FromWidths(list, details));

    [Theory]
    [InlineData(0.01, 0.1)]
    [InlineData(0.99, 0.9)]
    [InlineData(double.NaN, 0.4)]
    [InlineData(double.NegativeInfinity, 0.4)]
    public void Stored_values_are_read_as_a_valid_share(double stored, double expected) =>
        Assert.Equal(expected, ListPaneWidth.Normalize(stored));
}
