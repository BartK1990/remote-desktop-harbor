namespace Harbor.Core.Services;

/// <summary>What quick connect searches in one candidate (an entry, or one of its accounts). SPEC FR-48</summary>
public sealed record QuickSearchCandidate<T>(T Item, string Name, string? Host, string? User, string? Folder, bool Pinned, int? RecentRank);

/// <summary>
/// Ranks candidates for the quick connect palette (SPEC FR-48, ADR-0028). Every word of the query must match a field;
/// a match in the name counts most (exact &gt; prefix &gt; word start &gt; anywhere), then host, user and folder; pinned
/// and recently used entries come first among equals. An empty query lists pinned, then recent entries.
/// </summary>
public static class QuickSearch
{
    public const int MaxResults = 12;

    public static IReadOnlyList<T> Rank<T>(IEnumerable<QuickSearchCandidate<T>> candidates, string? query, int max = MaxResults)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scored = new List<(QuickSearchCandidate<T> C, int Score)>();
        foreach (var c in candidates)
        {
            var score = words.Length == 0 ? EmptyQueryScore(c) : Score(c, words);
            if (score > 0) scored.Add((c, score + Boost(c)));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.C.RecentRank ?? int.MaxValue)
            .ThenBy(s => s.C.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(s => s.C.Item)
            .ToList();
    }

    private static int EmptyQueryScore<T>(QuickSearchCandidate<T> c) => c.Pinned ? 100 : c.RecentRank is not null ? 50 : 0;

    private static int Boost<T>(QuickSearchCandidate<T> c) => (c.Pinned ? 6 : 0) + (c.RecentRank is not null ? 3 : 0);

    private static int Score<T>(QuickSearchCandidate<T> c, string[] words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var best = Math.Max(Math.Max(Field(c.Name, word, 100), Field(c.Host, word, 60)), Math.Max(Field(c.User, word, 40), Field(c.Folder, word, 30)));
            if (best == 0) return 0;
            total += best;
        }

        return total;
    }

    /// <summary>Score of <paramref name="word"/> in <paramref name="value"/>, scaled to <paramref name="weight"/>.</summary>
    private static int Field(string? value, string word, int weight)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var index = value.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) return 0;
        if (index == 0) return value.Length == word.Length ? weight : weight * 8 / 10;
        return char.IsLetterOrDigit(value[index - 1]) ? weight * 5 / 10 : weight * 7 / 10;
    }
}
