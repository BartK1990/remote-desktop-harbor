using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-02: entries are shown in a folder tree.
public class FolderTreeTests
{
    private sealed record Item(string Name, string? Folder);

    private static FolderTreeNode<Item> Build(params Item[] items) => FolderTree.Build(items, i => i.Folder, i => i.Name);

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData("Production", new[] { "Production" })]
    [InlineData("Production/Web", new[] { "Production", "Web" })]
    [InlineData(@" Production \ Web //", new[] { "Production", "Web" })]
    public void Split_returns_trimmed_levels(string? folder, string[] expected) =>
        Assert.Equal(expected, FolderTree.Split(folder));

    [Fact]
    public void Normalize_and_ancestors_use_slash()
    {
        Assert.Null(FolderTree.Normalize(" / "));
        Assert.Equal("A/B/C", FolderTree.Normalize(@"A\ B /C"));
        Assert.Equal(["A", "A/B", "A/B/C"], FolderTree.AncestorPaths(@"A\B/C"));
        Assert.Empty(FolderTree.AncestorPaths(null));
    }

    [Fact]
    public void Build_nests_folders_and_sorts_folders_before_items()
    {
        var root = Build(
            new Item("zeta", null),
            new Item("web02", "Production/Web"),
            new Item("dc01", "Production"),
            new Item("web01", @"production\web"),
            new Item("lab01", "Lab"),
            new Item("alpha", ""));

        Assert.Equal(6, root.TotalCount);
        Assert.Equal(["alpha", "zeta"], root.Items.Select(i => i.Name));
        Assert.Equal(["Lab", "Production"], root.Folders.Select(f => f.Name));

        var production = root.Folders[1];
        Assert.Equal("Production", production.Path);
        Assert.Equal(3, production.TotalCount);
        Assert.Equal(["dc01"], production.Items.Select(i => i.Name));

        // Case and separator differences end up in the same folder, named after the first spelling seen.
        var web = Assert.Single(production.Folders);
        Assert.Equal("Web", web.Name);
        Assert.Equal("Production/Web", web.Path);
        Assert.Equal(["web01", "web02"], web.Items.Select(i => i.Name));
    }

    [Fact]
    public void Build_of_nothing_is_an_empty_root()
    {
        var root = Build();
        Assert.Equal(0, root.TotalCount);
        Assert.Empty(root.Folders);
        Assert.Empty(root.Items);
    }
}
