using System.Text.RegularExpressions;

namespace Harbor.Core.Model;

/// <summary>The entry property a <see cref="SmartGroupRule"/> looks at. SPEC FR-62, ADR-0040</summary>
public enum SmartGroupField
{
    /// <summary>The entry's display name.</summary>
    Name = 0,

    /// <summary>The host name or IP address (without the port).</summary>
    Host = 1,

    /// <summary>The canonical folder path, e.g. <c>Production/Web</c>; empty without a folder.</summary>
    Folder = 2,

    /// <summary>Any account's user name, qualified (<c>DOMAIN\user</c>): the rule holds if one account matches.</summary>
    UserName = 3,

    /// <summary>The notes.</summary>
    Notes = 4,

    /// <summary>The environment tag derived from the folder (SPEC FR-49): PROD, STG, TEST, DEV, LAB; empty without one.</summary>
    Environment = 5,

    /// <summary>The entry kind (SPEC FR-03): RDP, Credentials or Other.</summary>
    Kind = 6,
}

/// <summary>How a <see cref="SmartGroupRule"/> compares the field with its value. Text comparisons ignore case.</summary>
public enum SmartGroupOperator
{
    Contains = 0,
    DoesNotContain = 1,
    Is = 2,
    IsNot = 3,
    StartsWith = 4,
    EndsWith = 5,

    /// <summary>The value is a .NET regular expression that must match somewhere in the field.</summary>
    Matches = 6,
    DoesNotMatch = 7,
}

/// <summary>Whether an entry must satisfy all rules of a smart group or any one of them.</summary>
public enum SmartGroupMatch
{
    All = 0,
    Any = 1,
}

/// <summary>One condition of a smart group, e.g. <em>Host starts with <c>sql-</c></em>. SPEC FR-62</summary>
public sealed record SmartGroupRule
{
    public SmartGroupField Field { get; init; }
    public SmartGroupOperator Operator { get; init; }
    public string Value { get; init; } = "";
}

/// <summary>
/// A virtual group of the entry tree, like a smart group in Remote Desktop Connection Manager: it holds no entries of
/// its own but lists every entry its rules match, and follows the catalog as entries are added, edited or deleted.
/// It is shown in <see cref="Folder"/> (top level when <c>null</c>) and then only considers entries in that folder and
/// its sub-folders. Smart groups are personal: kept in the user's settings, never in a shared database.
/// SPEC FR-62, ADR-0040
/// </summary>
public sealed record SmartGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";

    /// <summary>Folder the group is shown in and limited to (canonical path); <c>null</c> = top level, all entries.</summary>
    public string? Folder { get; init; }

    public SmartGroupMatch Match { get; init; } = SmartGroupMatch.All;
    public IReadOnlyList<SmartGroupRule> Rules { get; init; } = [];
}

/// <summary>Evaluates and checks <see cref="SmartGroup"/> rules. OS-neutral and in memory (SPEC FR-62, NFR-03).</summary>
public static class SmartGroups
{
    /// <summary>Longest a single regular expression may run on one value; a slower pattern counts as no match.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public const int MaxNameLength = 100;

    /// <summary>
    /// Whether <paramref name="entry"/> belongs to <paramref name="group"/>: it is in the group's folder (or below), and
    /// all (or any) rules hold. A group without rules matches every entry in its folder. An invalid regular expression
    /// never matches (and <see cref="Validate"/> reports it).
    /// </summary>
    public static bool Matches(SmartGroup group, ConnectionEntry entry)
    {
        if (!InScope(group.Folder, entry.Folder)) return false;
        if (group.Rules.Count == 0) return true;
        return group.Match == SmartGroupMatch.Any
            ? group.Rules.Any(r => Matches(r, entry))
            : group.Rules.All(r => Matches(r, entry));
    }

    /// <summary>Whether one rule holds for <paramref name="entry"/>.</summary>
    public static bool Matches(SmartGroupRule rule, ConnectionEntry entry)
    {
        var values = ValuesOf(rule.Field, entry);
        // A negative rule holds when no value has the text; a positive one when any value has it. An entry with
        // several accounts "does not contain admin" only if none of its accounts does.
        return rule.Operator switch
        {
            SmartGroupOperator.DoesNotContain => !values.Any(v => Test(SmartGroupOperator.Contains, v, rule.Value)),
            SmartGroupOperator.IsNot => !values.Any(v => Test(SmartGroupOperator.Is, v, rule.Value)),
            SmartGroupOperator.DoesNotMatch => IsValidPattern(rule.Value) && !values.Any(v => Test(SmartGroupOperator.Matches, v, rule.Value)),
            _ => values.Any(v => Test(rule.Operator, v, rule.Value)),
        };
    }

    /// <summary>The entries of <paramref name="entries"/> that belong to <paramref name="group"/>.</summary>
    public static IEnumerable<T> Filter<T>(SmartGroup group, IEnumerable<T> entries, Func<T, ConnectionEntry> modelOf) =>
        entries.Where(e => Matches(group, modelOf(e)));

    /// <summary>The first problem with <paramref name="group"/>, or <c>null</c> when it can be saved.</summary>
    public static string? Validate(SmartGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name)) return "Enter a name for the smart group.";
        if (group.Name.Trim().Length > MaxNameLength) return $"Use at most {MaxNameLength} characters for the name.";
        for (var i = 0; i < group.Rules.Count; i++)
        {
            if (ValidateRule(group.Rules[i]) is { } problem) return $"Rule {i + 1}: {problem}";
        }

        return null;
    }

    /// <summary>The problem with one rule, or <c>null</c>.</summary>
    public static string? ValidateRule(SmartGroupRule rule)
    {
        var isPattern = rule.Operator is SmartGroupOperator.Matches or SmartGroupOperator.DoesNotMatch;
        // "Folder is (empty)" / "Environment is not (empty)" are useful, so an empty value is allowed for Is / Is not.
        if (string.IsNullOrEmpty(rule.Value) && rule.Operator is not (SmartGroupOperator.Is or SmartGroupOperator.IsNot))
        {
            return isPattern ? "enter a regular expression." : "enter the text to look for.";
        }

        if (isPattern && !IsValidPattern(rule.Value)) return $"'{rule.Value}' is not a valid regular expression.";
        return null;
    }

    /// <summary>Whether an entry in <paramref name="entryFolder"/> is inside <paramref name="groupFolder"/> (or below it).</summary>
    public static bool InScope(string? groupFolder, string? entryFolder)
    {
        var scope = FolderTree.Split(groupFolder);
        if (scope.Count == 0) return true;
        var levels = FolderTree.Split(entryFolder);
        if (levels.Count < scope.Count) return false;
        for (var i = 0; i < scope.Count; i++)
        {
            if (!string.Equals(scope[i], levels[i], StringComparison.CurrentCultureIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>Label of a field, as shown in the editor.</summary>
    public static string Label(SmartGroupField field) => field switch
    {
        SmartGroupField.Name => "Name",
        SmartGroupField.Host => "Host",
        SmartGroupField.Folder => "Folder",
        SmartGroupField.UserName => "User name",
        SmartGroupField.Notes => "Notes",
        SmartGroupField.Environment => "Environment",
        _ => "Kind",
    };

    /// <summary>Label of an operator, as shown in the editor.</summary>
    public static string Label(SmartGroupOperator op) => op switch
    {
        SmartGroupOperator.Contains => "contains",
        SmartGroupOperator.DoesNotContain => "does not contain",
        SmartGroupOperator.Is => "is",
        SmartGroupOperator.IsNot => "is not",
        SmartGroupOperator.StartsWith => "starts with",
        SmartGroupOperator.EndsWith => "ends with",
        SmartGroupOperator.Matches => "matches regex",
        _ => "does not match regex",
    };

    /// <summary>"Host starts with sql- and Environment is PROD", for tooltips.</summary>
    public static string Describe(SmartGroup group)
    {
        var scope = group.Folder is { } f ? $"In {f}: " : "";
        if (group.Rules.Count == 0) return scope + "every entry";
        var joiner = group.Match == SmartGroupMatch.Any ? " or " : " and ";
        return scope + string.Join(joiner, group.Rules.Select(r => $"{Label(r.Field)} {Label(r.Operator)} \"{r.Value}\""));
    }

    /// <summary>The text of the kind as rules compare it (SPEC FR-03).</summary>
    public static string KindValue(EntryKind kind) => kind switch
    {
        EntryKind.RdpConnection => "RDP",
        EntryKind.CredentialOnly => "Credentials",
        _ => "Other",
    };

    private static IEnumerable<string> ValuesOf(SmartGroupField field, ConnectionEntry entry)
    {
        switch (field)
        {
            case SmartGroupField.Name:
                yield return entry.Name;
                break;
            case SmartGroupField.Host:
                yield return entry.Host?.Trim() ?? "";
                break;
            case SmartGroupField.Folder:
                yield return FolderTree.Normalize(entry.Folder) ?? "";
                break;
            case SmartGroupField.UserName:
                yield return entry.QualifiedUserName ?? "";
                foreach (var account in entry.Accounts) yield return entry.QualifiedUserNameFor(account) ?? "";
                break;
            case SmartGroupField.Notes:
                yield return entry.Notes ?? "";
                break;
            case SmartGroupField.Environment:
                yield return EnvironmentTags.FromFolder(entry.Folder) is { } env ? EnvironmentTags.Label(env) : "";
                break;
            default:
                yield return KindValue(entry.Kind);
                break;
        }
    }

    private static bool Test(SmartGroupOperator op, string value, string expected)
    {
        const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;
        switch (op)
        {
            case SmartGroupOperator.Contains:
                return value.Contains(expected, ignoreCase);
            case SmartGroupOperator.Is:
                return string.Equals(value, expected.Trim(), ignoreCase);
            case SmartGroupOperator.StartsWith:
                return value.StartsWith(expected, ignoreCase);
            case SmartGroupOperator.EndsWith:
                return value.EndsWith(expected, ignoreCase);
            case SmartGroupOperator.Matches:
                if (PatternOf(expected) is not { } regex) return false;
                try
                {
                    return regex.IsMatch(value);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            default:
                return false;
        }
    }

    private static bool IsValidPattern(string pattern) => PatternOf(pattern) is not null;

    // Patterns are parsed once, not once per entry, so a regex rule stays fast with 10 000 entries (SPEC NFR-03).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex?> Patterns = new(StringComparer.Ordinal);

    private static Regex? PatternOf(string pattern)
    {
        if (Patterns.TryGetValue(pattern, out var cached)) return cached;
        Regex? regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            regex = null;
        }

        if (Patterns.Count > 256) Patterns.Clear(); // patterns typed while editing a rule
        Patterns[pattern] = regex;
        return regex;
    }
}
