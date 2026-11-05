using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Harbor.App.ViewModels;

/// <summary>A row of the entry tree: a <see cref="FolderViewModel"/> or an <see cref="EntryViewModel"/>. See SPEC FR-02.</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => OnExpandedChanged();

    protected virtual void OnExpandedChanged()
    {
    }
}

/// <summary>A folder of the entry tree. <see cref="Path"/> is its canonical path (see <c>FolderTree.Normalize</c>).</summary>
public sealed partial class FolderViewModel(string path, Action<FolderViewModel> expandedChanged) : TreeNodeViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Environment), nameof(EnvironmentLabel))]
    private string _name = "";

    /// <summary>The environment this folder's own name stands for (SPEC FR-49); sub-folders do not repeat the tag.</summary>
    public Harbor.Core.Model.EnvironmentKind? Environment => Harbor.Core.Model.EnvironmentTags.FromName(Name);

    public string? EnvironmentLabel => Environment is { } kind ? Harbor.Core.Model.EnvironmentTags.Label(kind) : null;

    /// <summary>Entries in this folder and its sub-folders that match the current search.</summary>
    [ObservableProperty] private int _count;

    public string Path { get; } = path;
    public TreeChildren Children { get; } = [];

    protected override void OnExpandedChanged() => expandedChanged(this);
}

/// <summary>
/// A smart group row (SPEC FR-62, ADR-0039): a virtual group listing the entries its rules match, as
/// <see cref="EntryLinkViewModel"/> rows. Shown in its folder before the sub-folders, or at the top level.
/// </summary>
public sealed partial class SmartGroupViewModel(Guid id, Action<SmartGroupViewModel> expandedChanged) : TreeNodeViewModel
{
    private Dictionary<Guid, EntryLinkViewModel> _links = [];

    public Guid Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private Harbor.Core.Model.SmartGroup _group = new();

    public string Name => Group.Name;

    /// <summary>The rules in words, for the tooltip.</summary>
    public string Description => $"Smart group: {Harbor.Core.Model.SmartGroups.Describe(Group)}";

    /// <summary>Entries in the group that match the current search and filter.</summary>
    [ObservableProperty] private int _count;

    public TreeChildren Children { get; } = [];

    partial void OnGroupChanged(Harbor.Core.Model.SmartGroup value) => OnPropertyChanged(nameof(Name));

    /// <summary>
    /// The link rows for <paramref name="entries"/>, reusing the rows of entries already listed so their containers and
    /// selection survive a rebuild.
    /// </summary>
    public List<TreeNodeViewModel> LinksFor(IEnumerable<EntryViewModel> entries)
    {
        var links = new Dictionary<Guid, EntryLinkViewModel>();
        var rows = new List<TreeNodeViewModel>();
        foreach (var entry in entries)
        {
            if (!_links.TryGetValue(entry.Model.Id, out var link) || !ReferenceEquals(link.Entry, entry))
            {
                link = new EntryLinkViewModel(this, entry);
            }

            links[entry.Model.Id] = link;
            rows.Add(link);
        }

        _links = links;
        return rows;
    }

    protected override void OnExpandedChanged() => expandedChanged(this);
}

/// <summary>
/// An entry listed in a smart group (SPEC FR-62). It is a reference, not a copy: selecting it selects
/// <see cref="Entry"/> for the details pane and every action, and the entry still has its row in its own folder.
/// </summary>
public sealed class EntryLinkViewModel(SmartGroupViewModel group, EntryViewModel entry) : TreeNodeViewModel
{
    public SmartGroupViewModel Group { get; } = group;
    public EntryViewModel Entry { get; } = entry;
}

/// <summary>
/// Child rows of the tree. Small changes are applied item by item, so the containers of unchanged rows (and the
/// TreeView's selection, expansion and scroll position) survive; large changes are applied with a single
/// notification, so filtering 10 000 entries stays fast (SPEC NFR-03).
/// </summary>
public sealed class TreeChildren : ObservableCollection<TreeNodeViewModel>
{
    private const int ResetThreshold = 500;

    /// <summary>
    /// Makes the contents equal to <paramref name="items"/>. <paramref name="holdsSelection"/> forces item-by-item
    /// updates: resetting a collection that holds the selected row (or one of its folders) would make the TreeView
    /// move the selection elsewhere.
    /// </summary>
    public void ReplaceWith(IReadOnlyList<TreeNodeViewModel> items, bool holdsSelection)
    {
        if (Items.SequenceEqual(items)) return;

        var wanted = new HashSet<TreeNodeViewModel>(items);
        var present = new HashSet<TreeNodeViewModel>(Items);
        var changes = Items.Count(i => !wanted.Contains(i)) + items.Count(i => !present.Contains(i));
        if (!holdsSelection && changes > ResetThreshold)
        {
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            return;
        }

        for (var i = Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(this[i])) RemoveAt(i);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (i < Count && ReferenceEquals(this[i], items[i])) continue;
            if (present.Contains(items[i])) Move(IndexOf(items[i]), i); // renamed: rare
            else Insert(i, items[i]);
        }
    }
}
