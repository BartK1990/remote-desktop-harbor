namespace Harbor.Core.Model;

/// <summary>The environment a folder stands for, shown as a tag (PROD, STG, …). SPEC FR-49, ADR-0028</summary>
public enum EnvironmentKind
{
    Production,
    Staging,
    Test,
    Development,
    Lab,
}

/// <summary>
/// Derives environment tags from folder names, so a team's existing folders (<c>Production/Web</c>,
/// <c>UAT</c>, <c>Dev servers</c>) are tagged without any configuration or schema change. SPEC FR-49, ADR-0028
/// </summary>
public static class EnvironmentTags
{
    private static readonly Dictionary<string, EnvironmentKind> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prod"] = EnvironmentKind.Production, ["production"] = EnvironmentKind.Production, ["prd"] = EnvironmentKind.Production,
        ["live"] = EnvironmentKind.Production,
        ["staging"] = EnvironmentKind.Staging, ["stage"] = EnvironmentKind.Staging, ["stg"] = EnvironmentKind.Staging,
        ["preprod"] = EnvironmentKind.Staging, ["uat"] = EnvironmentKind.Staging,
        ["test"] = EnvironmentKind.Test, ["testing"] = EnvironmentKind.Test, ["tst"] = EnvironmentKind.Test, ["qa"] = EnvironmentKind.Test,
        ["dev"] = EnvironmentKind.Development, ["development"] = EnvironmentKind.Development,
        ["lab"] = EnvironmentKind.Lab, ["sandbox"] = EnvironmentKind.Lab,
    };

    private static readonly char[] Separators = [' ', '-', '_', '.', '(', ')', '[', ']'];

    /// <summary>The environment one folder name stands for: a whole word of it, e.g. "Production" or "Dev servers".</summary>
    public static EnvironmentKind? FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var word in name.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Words.TryGetValue(word, out var kind)) return kind;
        }

        return null;
    }

    /// <summary>The environment of an entry in <paramref name="folder"/>: the outermost folder level that names one.</summary>
    public static EnvironmentKind? FromFolder(string? folder) =>
        FolderTree.Split(folder).Select(FromName).FirstOrDefault(k => k is not null);

    public static string Label(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Production => "PROD",
        EnvironmentKind.Staging => "STG",
        EnvironmentKind.Test => "TEST",
        EnvironmentKind.Development => "DEV",
        _ => "LAB",
    };
}
