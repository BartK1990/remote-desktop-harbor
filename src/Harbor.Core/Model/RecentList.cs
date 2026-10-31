namespace Harbor.Core.Model;

/// <summary>The "recently connected" list: most recent first, no duplicates, bounded. SPEC FR-50</summary>
public static class RecentList
{
    public const int Capacity = 10;

    public static List<Guid> Push(IEnumerable<Guid> recent, Guid id, int capacity = Capacity) =>
        recent.Where(r => r != id).Prepend(id).Take(capacity).ToList();
}
