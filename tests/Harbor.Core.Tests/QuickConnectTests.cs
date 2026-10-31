using Harbor.Core.Model;
using Harbor.Core.Services;

namespace Harbor.Core.Tests;

// SPEC FR-48, FR-49, FR-50, ADR-0028
public class QuickConnectTests
{
    private static QuickSearchCandidate<string> C(string name, string? host = null, string? user = null, string? folder = null,
        bool pinned = false, int? recent = null) => new(name, name, host, user, folder, pinned, recent);

    [Fact]
    public void Name_matches_rank_above_host_user_and_folder_matches()
    {
        var results = QuickSearch.Rank(
        [
            C("Reporting", folder: "Databases/SQL"),
            C("Analysis", host: "sql02.contoso.local"),
            C("SQL01"),
            C("Jump host", user: @"CONTOSO\sql-admin"),
            C("Web"),
        ], "sql");

        Assert.Equal(["SQL01", "Analysis", "Jump host", "Reporting"], results);
    }

    [Fact]
    public void Every_word_must_match_somewhere()
    {
        var results = QuickSearch.Rank([C("SQL01", folder: "Production"), C("SQL02", folder: "Staging")], "prod sql");
        Assert.Equal(["SQL01"], results);
    }

    [Fact]
    public void Pinned_and_recent_entries_win_among_equals_and_fill_an_empty_query()
    {
        var candidates = new[] { C("APP01"), C("APP02", pinned: true), C("APP03", recent: 0), C("WEB01") };

        Assert.Equal(["APP02", "APP03", "APP01"], QuickSearch.Rank(candidates, "app"));
        Assert.Equal(["APP02", "APP03"], QuickSearch.Rank(candidates, "  "));
    }

    [Fact]
    public void Results_are_capped()
    {
        var many = Enumerable.Range(0, 40).Select(i => C($"srv{i:00}"));
        Assert.Equal(QuickSearch.MaxResults, QuickSearch.Rank(many, "srv").Count);
    }

    [Theory]
    [InlineData("Production/Web", EnvironmentKind.Production)]
    [InlineData("Customers/Contoso/UAT", EnvironmentKind.Staging)]
    [InlineData("Dev servers", EnvironmentKind.Development)]
    [InlineData("PreProd", EnvironmentKind.Staging)]
    [InlineData("Lab", EnvironmentKind.Lab)]
    [InlineData("QA-Team", EnvironmentKind.Test)]
    [InlineData("Staging/Prod mirror", EnvironmentKind.Staging)]
    public void Environment_comes_from_the_outermost_folder_that_names_one(string folder, EnvironmentKind expected) =>
        Assert.Equal(expected, EnvironmentTags.FromFolder(folder));

    [Theory]
    [InlineData(null)]
    [InlineData("Web")]
    [InlineData("Products")]
    [InlineData("Devices")]
    public void Folders_without_an_environment_word_have_no_tag(string? folder) =>
        Assert.Null(EnvironmentTags.FromFolder(folder));

    [Fact]
    public void Recent_list_puts_the_latest_first_without_duplicates_and_is_bounded()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal([b, a], RecentList.Push([a, b], b));

        var many = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();
        var pushed = RecentList.Push(many, a);
        Assert.Equal(RecentList.Capacity, pushed.Count);
        Assert.Equal(a, pushed[0]);
    }
}
