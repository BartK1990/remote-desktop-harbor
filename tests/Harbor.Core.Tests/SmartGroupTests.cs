using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-62, ADR-0039: smart groups list the entries their rules match, like RDCMan's smart groups.
public class SmartGroupTests
{
    private static ConnectionEntry Entry(string name, string? folder = null, string? host = null, string? user = null,
        EntryKind kind = EntryKind.RdpConnection, string? notes = null, params ConnectionAccount[] accounts) => new()
    {
        Id = Guid.NewGuid(), Name = name, Folder = folder, Host = host, UserName = user, Domain = user is null ? null : "CORP", Kind = kind,
        Notes = notes, Accounts = accounts,
    };

    private static SmartGroupRule Rule(SmartGroupField field, SmartGroupOperator op, string value) => new() { Field = field, Operator = op, Value = value };

    private static SmartGroup Group(SmartGroupMatch match = SmartGroupMatch.All, string? folder = null, params SmartGroupRule[] rules) =>
        new() { Name = "Group", Match = match, Folder = folder, Rules = rules };

    [Theory]
    [InlineData(SmartGroupOperator.Contains, "QL-0", true)]
    [InlineData(SmartGroupOperator.Contains, "web", false)]
    [InlineData(SmartGroupOperator.DoesNotContain, "web", true)]
    [InlineData(SmartGroupOperator.DoesNotContain, "sql", false)]
    [InlineData(SmartGroupOperator.Is, " SQL-01.corp.local ", true)]
    [InlineData(SmartGroupOperator.Is, "sql-01", false)]
    [InlineData(SmartGroupOperator.IsNot, "sql-01", true)]
    [InlineData(SmartGroupOperator.StartsWith, "sql-", true)]
    [InlineData(SmartGroupOperator.StartsWith, "corp", false)]
    [InlineData(SmartGroupOperator.EndsWith, ".CORP.LOCAL", true)]
    [InlineData(SmartGroupOperator.Matches, @"^sql-\d+\.", true)]
    [InlineData(SmartGroupOperator.Matches, @"^web-\d+", false)]
    [InlineData(SmartGroupOperator.DoesNotMatch, @"^web-\d+", true)]
    public void Operators_compare_text_ignoring_case(SmartGroupOperator op, string value, bool expected)
    {
        var entry = Entry("Database", host: "sql-01.corp.local");
        Assert.Equal(expected, SmartGroups.Matches(Rule(SmartGroupField.Host, op, value), entry));
    }

    [Fact]
    public void All_needs_every_rule_and_any_needs_one()
    {
        var prodSql = Entry("sql-01", folder: "Production/DB", host: "sql-01");
        var testSql = Entry("sql-02", folder: "Test", host: "sql-02");
        var prodWeb = Entry("web-01", folder: "Production/Web", host: "web-01");
        var rules = new[]
        {
            Rule(SmartGroupField.Host, SmartGroupOperator.StartsWith, "sql-"),
            Rule(SmartGroupField.Environment, SmartGroupOperator.Is, "PROD"),
        };

        var all = Group(SmartGroupMatch.All, null, rules);
        var any = Group(SmartGroupMatch.Any, null, rules);

        Assert.Equal([prodSql], new[] { prodSql, testSql, prodWeb }.Where(e => SmartGroups.Matches(all, e)));
        Assert.Equal([prodSql, testSql, prodWeb], new[] { prodSql, testSql, prodWeb }.Where(e => SmartGroups.Matches(any, e)));
    }

    [Fact]
    public void A_group_in_a_folder_only_considers_entries_in_that_folder_and_below()
    {
        var group = Group(SmartGroupMatch.All, "Production", Rule(SmartGroupField.Name, SmartGroupOperator.Contains, "sql"));

        Assert.True(SmartGroups.Matches(group, Entry("sql-01", folder: "Production")));
        Assert.True(SmartGroups.Matches(group, Entry("sql-02", folder: @"production\DB")));
        Assert.False(SmartGroups.Matches(group, Entry("sql-03", folder: "Test")));
        Assert.False(SmartGroups.Matches(group, Entry("sql-04", folder: "Production-old")));
        Assert.False(SmartGroups.Matches(group, Entry("sql-05")));
    }

    [Fact]
    public void A_group_without_rules_lists_its_whole_folder()
    {
        Assert.True(SmartGroups.Matches(Group(), Entry("any")));
        Assert.True(SmartGroups.Matches(Group(folder: "Lab"), Entry("x", folder: "Lab/Sub")));
        Assert.False(SmartGroups.Matches(Group(folder: "Lab"), Entry("y", folder: "Prod")));
    }

    [Fact]
    public void User_name_rules_look_at_every_account()
    {
        var entry = Entry("dc01", user: "jdoe", accounts: new ConnectionAccount { Id = Guid.NewGuid(), UserName = "svc-admin" });

        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.UserName, SmartGroupOperator.Is, @"CORP\svc-admin"), entry));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.UserName, SmartGroupOperator.Contains, "jdoe"), entry));
        // "does not contain" holds only when no account has the text.
        Assert.False(SmartGroups.Matches(Rule(SmartGroupField.UserName, SmartGroupOperator.DoesNotContain, "admin"), entry));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.UserName, SmartGroupOperator.DoesNotContain, "root"), entry));
    }

    [Fact]
    public void Kind_folder_notes_and_empty_values_can_be_matched()
    {
        var other = Entry("vpn", kind: EntryKind.Other, notes: "Owner: network team");
        var rdp = Entry("box", folder: " Lab / Sub ");

        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Kind, SmartGroupOperator.Is, "other"), other));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Kind, SmartGroupOperator.Is, "RDP"), rdp));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Notes, SmartGroupOperator.Contains, "network"), other));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Folder, SmartGroupOperator.Is, "lab/sub"), rdp));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Folder, SmartGroupOperator.Is, ""), other));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Environment, SmartGroupOperator.Is, "LAB"), rdp));
        Assert.True(SmartGroups.Matches(Rule(SmartGroupField.Environment, SmartGroupOperator.IsNot, ""), rdp));
        Assert.False(SmartGroups.Matches(Rule(SmartGroupField.Environment, SmartGroupOperator.IsNot, ""), other));
    }

    [Fact]
    public void An_invalid_regex_never_matches_and_is_reported()
    {
        var entry = Entry("web-01", host: "web-01");
        Assert.False(SmartGroups.Matches(Rule(SmartGroupField.Host, SmartGroupOperator.Matches, "web-("), entry));
        Assert.False(SmartGroups.Matches(Rule(SmartGroupField.Host, SmartGroupOperator.DoesNotMatch, "web-("), entry));

        var problem = SmartGroups.Validate(Group(SmartGroupMatch.All, null, Rule(SmartGroupField.Name, SmartGroupOperator.Contains, "a"),
            Rule(SmartGroupField.Host, SmartGroupOperator.Matches, "web-(")));
        Assert.Equal("Rule 2: 'web-(' is not a valid regular expression.", problem);
    }

    [Fact]
    public void Validate_requires_a_name_and_a_value_except_for_is()
    {
        Assert.Equal("Enter a name for the smart group.", SmartGroups.Validate(Group() with { Name = " " }));
        Assert.NotNull(SmartGroups.Validate(Group() with { Name = new string('x', SmartGroups.MaxNameLength + 1) }));
        Assert.Equal("Rule 1: enter the text to look for.",
            SmartGroups.Validate(Group(SmartGroupMatch.All, null, Rule(SmartGroupField.Name, SmartGroupOperator.Contains, ""))));
        Assert.Null(SmartGroups.Validate(Group(SmartGroupMatch.All, null, Rule(SmartGroupField.Folder, SmartGroupOperator.Is, ""))));
        Assert.Null(SmartGroups.Validate(Group()));
    }

    [Fact]
    public void Describe_reads_like_the_rules()
    {
        var group = Group(SmartGroupMatch.Any, "Production",
            Rule(SmartGroupField.Host, SmartGroupOperator.StartsWith, "sql-"), Rule(SmartGroupField.Kind, SmartGroupOperator.Is, "RDP"));
        Assert.Equal("In Production: Host starts with \"sql-\" or Kind is \"RDP\"", SmartGroups.Describe(group));
        Assert.Equal("every entry", SmartGroups.Describe(Group()));
    }

    [Fact]
    public void Matching_ten_thousand_entries_with_a_regex_is_fast()
    {
        var entries = Enumerable.Range(0, 10_000).Select(i => Entry($"srv-{i}", folder: i % 2 == 0 ? "Production" : "Test", host: $"srv-{i}")).ToList();
        var group = Group(SmartGroupMatch.All, null, Rule(SmartGroupField.Host, SmartGroupOperator.Matches, @"^srv-\d*7$"),
            Rule(SmartGroupField.Environment, SmartGroupOperator.Is, "TEST"));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var count = SmartGroups.Filter(group, entries, e => e).Count();
        watch.Stop();

        Assert.Equal(1000, count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString()); // SPEC NFR-03
    }

    [Fact]
    public void Smart_groups_survive_a_json_round_trip_in_the_settings()
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var group = Group(SmartGroupMatch.Any, "Lab", Rule(SmartGroupField.Kind, SmartGroupOperator.IsNot, "Other")) with { Name = "Lab boxes" };

        var json = System.Text.Json.JsonSerializer.Serialize(new HarborSettings { SmartGroups = [group] }, options);
        var back = Assert.Single(System.Text.Json.JsonSerializer.Deserialize<HarborSettings>(json, options)!.SmartGroups);

        Assert.Equal((group.Id, group.Name, group.Folder, group.Match), (back.Id, back.Name, back.Folder, back.Match));
        Assert.Equal(group.Rules, back.Rules);
    }
}
