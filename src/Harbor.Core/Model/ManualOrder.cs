namespace Harbor.Core.Model;

/// <summary>How the entry tree orders folders and entries within a folder (SPEC FR-72, ADR-0052).</summary>
public enum TreeOrder
{
    /// <summary>Alphabetically by name (the default).</summary>
    Name = 0,

    /// <summary>In the user's own order (<see cref="HarborSettings.ManualOrder"/>); what was never moved follows by name.</summary>
    Manual = 1,
}

/// <summary>
/// The user's own order of the entry tree (SPEC FR-72, ADR-0052): one list of keys — entries by id, folders by path —
/// where an earlier key comes first among its siblings. Only siblings are ever compared (sub-folders with sub-folders,
/// entries with entries of the same folder), so one flat list serves the whole tree. Keys not in the list follow the
/// listed ones, by name. Personal, like pins: kept in the user's settings, never in a shared database.
/// </summary>
public sealed class ManualOrder
{
    private const string FolderPrefix = "folder:";

    private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

    public ManualOrder(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            _ranks.TryAdd(key, _ranks.Count);
        }
    }

    /// <summary>The key of an entry.</summary>
    public static string EntryKey(Guid id) => id.ToString("N");

    /// <summary>The key of a folder, by its path (see <see cref="FolderTree.Normalize"/>); folders match ignoring case.</summary>
    public static string FolderKey(string path) => FolderPrefix + (FolderTree.Normalize(path) ?? "");

    /// <summary>Compares two siblings: listed keys by their place in the list, before unlisted ones, which go by name.</summary>
    public int Compare(string keyA, string nameA, string keyB, string nameB)
    {
        var listedA = _ranks.TryGetValue(keyA, out var rankA);
        var listedB = _ranks.TryGetValue(keyB, out var rankB);
        if (listedA && listedB && rankA != rankB) return rankA.CompareTo(rankB);
        if (listedA != listedB) return listedA ? -1 : 1;
        return string.Compare(nameA, nameB, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// The order after moving <paramref name="key"/> by <paramref name="offset"/> places (negative = up) among
    /// <paramref name="siblings"/>, given in the order they are shown. All siblings are listed afterwards, so they keep the
    /// order they were shown in. Keys not in <paramref name="known"/> (deleted entries, folders that no longer exist) are
    /// dropped. Returns <paramref name="order"/> unchanged when <paramref name="key"/> cannot move that way.
    /// </summary>
    public static List<string> Move(IReadOnlyList<string> order, IReadOnlyList<string> siblings, string key, int offset,
        IReadOnlySet<string>? known = null)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var index = siblings.ToList().FindIndex(s => comparer.Equals(s, key));
        var target = Math.Clamp(index + offset, 0, siblings.Count - 1);
        if (index < 0 || target == index) return [.. order];

        var moved = siblings.ToList();
        moved.RemoveAt(index);
        moved.Insert(target, siblings[index]);

        var inSiblings = new HashSet<string>(siblings, comparer);
        var result = order.Where(k => !inSiblings.Contains(k) && (known is null || known.Contains(k))).ToList();
        result.AddRange(moved);
        return result.Distinct(comparer).ToList();
    }

    /// <summary>
    /// The order after dropping <paramref name="key"/> just before (or, with <paramref name="after"/>, just after)
    /// <paramref name="target"/>, both among <paramref name="siblings"/> given in the order they are shown (drag and drop,
    /// SPEC FR-72). Like <see cref="Move"/>, all siblings are listed afterwards and unknown keys dropped. Returns
    /// <paramref name="order"/> unchanged when either key is not a sibling or nothing would move.
    /// </summary>
    public static List<string> MoveTo(IReadOnlyList<string> order, IReadOnlyList<string> siblings, string key, string target,
        bool after, IReadOnlySet<string>? known = null)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var index = siblings.ToList().FindIndex(s => comparer.Equals(s, key));
        var targetIndex = siblings.ToList().FindIndex(s => comparer.Equals(s, target));
        if (index < 0 || targetIndex < 0 || index == targetIndex) return [.. order];

        var moved = siblings.Where(s => !comparer.Equals(s, key)).ToList();
        var at = moved.FindIndex(s => comparer.Equals(s, target)) + (after ? 1 : 0);
        moved.Insert(at, siblings[index]);
        if (moved.SequenceEqual(siblings, comparer)) return [.. order];

        var inSiblings = new HashSet<string>(siblings, comparer);
        var result = order.Where(k => !inSiblings.Contains(k) && (known is null || known.Contains(k))).ToList();
        result.AddRange(moved);
        return result.Distinct(comparer).ToList();
    }

    /// <summary>
    /// The order with <paramref name="key"/> placed right after <paramref name="after"/> among
    /// <paramref name="siblings"/> (given in the order they are shown), e.g. a duplicated entry next to its original
    /// (SPEC FR-73). <paramref name="key"/> need not be among the siblings yet.
    /// </summary>
    public static List<string> PlaceAfter(IReadOnlyList<string> order, IReadOnlyList<string> siblings, string key, string after)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var placed = siblings.Where(s => !comparer.Equals(s, key)).ToList();
        var index = placed.FindIndex(s => comparer.Equals(s, after));
        placed.Insert(index < 0 ? placed.Count : index + 1, key);

        var inPlaced = new HashSet<string>(placed, comparer);
        var result = order.Where(k => !inPlaced.Contains(k)).ToList();
        result.AddRange(placed);
        return result;
    }
}
