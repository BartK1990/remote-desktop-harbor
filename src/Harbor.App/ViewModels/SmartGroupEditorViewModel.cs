using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.Core.Model;

namespace Harbor.App.ViewModels;

/// <summary>
/// Creates or edits a smart group (SPEC FR-62, ADR-0040): name, folder it is shown in, All / Any and the rules, with a
/// live count of the entries it would list.
/// </summary>
public sealed partial class SmartGroupEditorViewModel : ObservableObject
{
    private readonly SmartGroup _original;
    private readonly IReadOnlyList<ConnectionEntry> _entries;

    public SmartGroupEditorViewModel(SmartGroup original, bool isNew, IEnumerable<string> folders, IReadOnlyList<ConnectionEntry> entries)
    {
        _original = original;
        _entries = entries;
        IsNew = isNew;
        Folders = folders.ToList();
        _name = original.Name;
        _folder = original.Folder;
        _match = original.Match;
        Rules = new ObservableCollection<SmartGroupRuleViewModel>(original.Rules.Select(r => new SmartGroupRuleViewModel(r, UpdatePreview)));
        Rules.CollectionChanged += Rules_CollectionChanged;
        if (isNew && Rules.Count == 0) AddRule();
        UpdatePreview();
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "New smart group" : $"Edit smart group – {_original.Name}";
    public IReadOnlyList<string> Folders { get; }

    public static IReadOnlyList<Choice<SmartGroupMatch>> MatchChoices { get; } =
    [
        new("all rules", SmartGroupMatch.All),
        new("any rule", SmartGroupMatch.Any),
    ];

    [ObservableProperty] private string _name;

    partial void OnNameChanged(string value) => Error = null;

    /// <summary>Folder the group is shown in and limited to; empty = top level, every entry.</summary>
    [ObservableProperty] private string? _folder;

    partial void OnFolderChanged(string? value) => UpdatePreview();

    [ObservableProperty] private SmartGroupMatch _match;

    partial void OnMatchChanged(SmartGroupMatch value) => UpdatePreview();

    public ObservableCollection<SmartGroupRuleViewModel> Rules { get; }

    [ObservableProperty] private string? _error;

    /// <summary>"12 entries match", updated as rules are typed.</summary>
    [ObservableProperty] private string _preview = "";

    [RelayCommand]
    private void AddRule() => Rules.Add(new SmartGroupRuleViewModel(new SmartGroupRule(), UpdatePreview));

    [RelayCommand]
    private void RemoveRule(SmartGroupRuleViewModel? rule)
    {
        if (rule is not null) Rules.Remove(rule);
    }

    /// <summary>The group as edited; the id stays the same.</summary>
    public SmartGroup BuildGroup() => _original with
    {
        Name = Name.Trim(),
        Folder = FolderTree.Normalize(Folder),
        Match = Match,
        Rules = Rules.Select(r => r.Build()).ToList(),
    };

    /// <summary>Checks the group; on a problem sets <see cref="Error"/> and returns <c>false</c>.</summary>
    public bool Validate()
    {
        Error = SmartGroups.Validate(BuildGroup());
        return Error is null;
    }

    private void Rules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var group = BuildGroup();
        var invalid = group.Rules.Select(SmartGroups.ValidateRule).FirstOrDefault(p => p is not null);
        var count = _entries.Count(e => SmartGroups.Matches(group, e));
        Preview = (count == 1 ? "1 entry matches" : $"{count} entries match") + (invalid is null ? "" : $" (a rule is incomplete: {invalid})");
        Error = null;
    }
}

/// <summary>One rule row of the smart group editor: field, operator and value. SPEC FR-62</summary>
public sealed partial class SmartGroupRuleViewModel : ObservableObject
{
    private readonly Action _changed;

    public SmartGroupRuleViewModel(SmartGroupRule rule, Action changed)
    {
        _field = rule.Field;
        _operator = rule.Operator;
        _value = rule.Value;
        _changed = changed;
    }

    public static IReadOnlyList<Choice<SmartGroupField>> FieldChoices { get; } =
        Enum.GetValues<SmartGroupField>().Select(f => new Choice<SmartGroupField>(SmartGroups.Label(f), f)).ToList();

    public static IReadOnlyList<Choice<SmartGroupOperator>> OperatorChoices { get; } =
        Enum.GetValues<SmartGroupOperator>().Select(o => new Choice<SmartGroupOperator>(SmartGroups.Label(o), o)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private SmartGroupField _field;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private SmartGroupOperator _operator;

    [ObservableProperty] private string _value;

    /// <summary>What the value box expects, as its tooltip.</summary>
    public string ValueHint => Operator is SmartGroupOperator.Matches or SmartGroupOperator.DoesNotMatch
        ? "A .NET regular expression, e.g. ^sql-\\d+ (case is ignored)"
        : Field switch
        {
            SmartGroupField.Environment => "PROD, STG, TEST, DEV or LAB (empty = no environment)",
            SmartGroupField.Kind => "RDP, Credentials or Other",
            SmartGroupField.Folder => "A folder path, e.g. Production/Web (empty = no folder)",
            SmartGroupField.UserName => "DOMAIN\\user; any of the entry's accounts counts",
            _ => "Text to compare (case is ignored)",
        };

    public SmartGroupRule Build() => new() { Field = Field, Operator = Operator, Value = Value };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Field) or nameof(Operator) or nameof(Value)) _changed();
    }
}
