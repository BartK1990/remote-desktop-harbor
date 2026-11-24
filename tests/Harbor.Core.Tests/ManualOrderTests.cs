using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-72, ADR-0052: the user's own order of the entry tree.
public class ManualOrderTests
{
    private sealed record Item(Guid Id, string Name, string? Folder);

    private static Item I(string name, string? folder = null) => new(Guid.NewGuid(), name, folder);

    private static string Key(Item item) => ManualOrder.EntryKey(item.Id);

    private static FolderTreeNode<Item> Build(IEnumerable<string>? order, params Item[] items) =>
        FolderTree.Build(items, i => i.Folder, i => i.Name, order is null ? null : new ManualOrder(order), Key);

    [Fact]
    public void Listed_keys_come_first_in_their_order_then_the_rest_by_name()
    {
        var (a, b, c, d) = (I("alpha"), I("bravo"), I("charlie"), I("delta"));
        var root = Build([Key(c), Key(a)], d, b, a, c);
        Assert.Equal(["charlie", "alpha", "bravo", "delta"], root.Items.Select(i => i.Name));
    }

    [Fact]
    public void Without_an_order_the_tree_is_by_name()
    {
        var (a, b) = (I("alpha"), I("bravo"));
        Assert.Equal(["alpha", "bravo"], Build(null, b, a).Items.Select(i => i.Name));
        Assert.Equal(["alpha", "bravo"], Build([], b, a).Items.Select(i => i.Name));
    }

    [Fact]
    public void Folders_are_ordered_by_path_ignoring_case_and_stay_before_entries()
    {
        var root = Build([ManualOrder.FolderKey("staging"), ManualOrder.FolderKey(@"Production\Web"), ManualOrder.FolderKey("Production/DB")],
            I("x", "Production/DB"), I("y", "Production/Web"), I("z", "Staging"), I("top"));
        Assert.Equal(["Staging", "Production"], root.Folders.Select(f => f.Name));
        Assert.Equal(["Web", "DB"], root.Folders[1].Folders.Select(f => f.Name));
        Assert.Equal(["top"], root.Items.Select(i => i.Name));
    }

    [Fact]
    public void Move_swaps_with_the_neighbour_and_lists_all_siblings_as_shown()
    {
        List<string> siblings = ["a", "b", "c"];
        Assert.Equal(["b", "a", "c"], ManualOrder.Move([], siblings, "b", -1));
        Assert.Equal(["a", "c", "b"], ManualOrder.Move([], siblings, "b", 1));
    }

    [Fact]
    public void Move_keeps_other_folders_keys_and_drops_unknown_ones()
    {
        var moved = ManualOrder.Move(["other", "gone", "b", "a"], ["b", "a"], "a", -1,
            new HashSet<string>(["other", "a", "b"], StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["other", "a", "b"], moved);
    }

    [Fact]
    public void Move_past_the_ends_or_of_an_unknown_key_changes_nothing()
    {
        List<string> order = ["x"];
        Assert.Equal(order, ManualOrder.Move(order, ["a", "b"], "a", -1));
        Assert.Equal(order, ManualOrder.Move(order, ["a", "b"], "b", 1));
        Assert.Equal(order, ManualOrder.Move(order, ["a", "b"], "c", 1));
    }

    [Fact]
    public void Place_after_puts_a_new_key_right_after_another()
    {
        Assert.Equal(["x", "a", "copy", "b"], ManualOrder.PlaceAfter(["x", "b", "a"], ["a", "b"], "copy", "a"));
        Assert.Equal(["a", "b", "copy"], ManualOrder.PlaceAfter([], ["a", "b"], "copy", "missing"));
    }
}

// SPEC FR-73: the name of a duplicated entry.
public class CopyNameTests
{
    [Theory]
    [InlineData("Web 01", new string[0], "Web 01 (copy)")]
    [InlineData("Web 01", new[] { "web 01 (COPY)" }, "Web 01 (copy 2)")]
    [InlineData("Web 01", new[] { "Web 01 (copy)", "Web 01 (copy 2)" }, "Web 01 (copy 3)")]
    [InlineData("Web 01 (copy)", new[] { "Web 01", "Web 01 (copy)" }, "Web 01 (copy 2)")]
    [InlineData("Web 01 (copy 2)", new[] { "Web 01 (copy 2)" }, "Web 01 (copy)")]
    [InlineData("(copy)", new string[0], "(copy) (copy)")]
    public void Copy_name_is_the_first_free_one(string name, string[] taken, string expected) =>
        Assert.Equal(expected, ConnectionEntry.CopyName(name, taken));
}
