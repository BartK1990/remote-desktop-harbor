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
    [ObservableProperty] private string _name = "";

    /// <summary>Entries in this folder and its sub-folders that match the current search.</summary>
    [ObservableProperty] private int _count;

    public string Path { get; } = path;
    public TreeChildren Children { get; } = [];

    protected override void OnExpandedChanged() => expandedChanged(this);
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
