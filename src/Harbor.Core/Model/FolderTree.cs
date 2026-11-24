namespace Harbor.Core.Model;

/// <summary>
/// Builds the folder tree shown in the main window. A folder value is a path whose levels are separated by
/// <c>/</c> or <c>\</c> (e.g. <c>Production/Web</c>); folders are matched case-insensitively. See SPEC FR-02.
/// </summary>
public static class FolderTree
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The levels of <paramref name="folder"/>; empty for no folder. Blank levels are ignored.</summary>
    public static IReadOnlyList<string> Split(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? []
            : folder.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The canonical path of <paramref name="folder"/> (levels joined with <c>/</c>), or <c>null</c> for no folder.</summary>
    public static string? Normalize(string? folder) => Split(folder) is { Count: > 0 } levels ? string.Join('/', levels) : null;

    /// <summary>The canonical paths of <paramref name="folder"/> and all its ancestors, outermost first.</summary>
    public static IReadOnlyList<string> AncestorPaths(string? folder)
    {
        var levels = Split(folder);
        var paths = new List<string>(levels.Count);
        for (var i = 1; i <= levels.Count; i++)
        {
            paths.Add(string.Join('/', levels.Take(i)));
        }

        return paths;
    }

    /// <summary>
    /// Groups <paramref name="items"/> into a tree by their folder. Within a folder, sub-folders come first, then items;
    /// both are sorted by name, or by <paramref name="order"/> when given (SPEC FR-72; items need <paramref name="keyOf"/>,
    /// their <see cref="ManualOrder.EntryKey"/>). Items without a folder end up directly in the returned root.
    /// </summary>
    public static FolderTreeNode<T> Build<T>(IEnumerable<T> items, Func<T, string?> folderOf, Func<T, string> nameOf,
        ManualOrder? order = null, Func<T, string>? keyOf = null)
    {
        var root = new FolderTreeNode<T>("", "");
        foreach (var item in items)
        {
            var node = root;
            foreach (var level in Split(folderOf(item)))
            {
                node = node.GetOrAddFolder(level);
            }

            node.ItemList.Add(item);
        }

        Comparison<FolderTreeNode<T>> folders = order is null
            ? (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
            : (a, b) => order.Compare(ManualOrder.FolderKey(a.Path), a.Name, ManualOrder.FolderKey(b.Path), b.Name);
        Comparison<T> itemOrder = order is null || keyOf is null
            ? (a, b) => string.Compare(nameOf(a), nameOf(b), StringComparison.CurrentCultureIgnoreCase)
            : (a, b) => order.Compare(keyOf(a), nameOf(a), keyOf(b), nameOf(b));
        root.Complete(folders, itemOrder);
        return root;
    }
}

/// <summary>A folder in a <see cref="FolderTree"/>. The root has an empty <see cref="Name"/> and <see cref="Path"/>.</summary>
public sealed class FolderTreeNode<T>
{
    private readonly Dictionary<string, FolderTreeNode<T>> _byName = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly List<FolderTreeNode<T>> _folders = [];

    internal FolderTreeNode(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>Display name: the spelling of the first item seen in this folder.</summary>
    public string Name { get; }

    /// <summary>Canonical path (levels joined with <c>/</c>); see <see cref="FolderTree.Normalize"/>.</summary>
    public string Path { get; }

    public IReadOnlyList<FolderTreeNode<T>> Folders => _folders;
    public IReadOnlyList<T> Items => ItemList;

    /// <summary>Number of items in this folder and all its sub-folders.</summary>
    public int TotalCount { get; private set; }

    internal List<T> ItemList { get; } = [];

    internal FolderTreeNode<T> GetOrAddFolder(string name)
    {
        if (!_byName.TryGetValue(name, out var folder))
        {
            folder = new FolderTreeNode<T>(name, Path.Length == 0 ? name : $"{Path}/{name}");
            _byName.Add(name, folder);
            _folders.Add(folder);
        }

        return folder;
    }

    internal void Complete(Comparison<FolderTreeNode<T>> folderOrder, Comparison<T> itemOrder)
    {
        _folders.Sort(folderOrder);
        ItemList.Sort(itemOrder);
        TotalCount = ItemList.Count;
        foreach (var folder in _folders)
        {
            folder.Complete(folderOrder, itemOrder);
            TotalCount += folder.TotalCount;
        }
    }
}
