using System.Collections.Specialized;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>
/// Delegates selection operations and state to Avalonia's SelectionModel. Adds only stable-key restoration
/// around DynamicData view updates, and drops keys that are no longer visible.
/// </summary>
internal sealed class IdentityPreservingSelectionModel<T, TIdentity> : IIdentityPreservingSelectionModel, INotifyPropertyChanged
    where T : notnull
    where TIdentity : notnull
{
    private readonly SelectionModel<object?> _inner = new();
    private readonly Func<T, TIdentity> _identitySelector;
    private readonly IScheduler _workerScheduler;
    private readonly IScheduler _uiScheduler;
    private readonly IEqualityComparer<TIdentity> _identityComparer;
    private readonly List<TIdentity> _selectionSnapshot = [];
    private readonly HashSet<TIdentity> _selectionIdentities;
    private readonly List<int> _restoredIndexes = [];
    private INotifyCollectionChanged? _sourceNotifications;
    private IEnumerable? _identitySource;
    private TIdentity[]? _pendingSelectionSnapshot;
    private bool _restoreCaptureScheduled;
    private int _sourceChangeVersion;

    public IdentityPreservingSelectionModel(
        Func<T, TIdentity> identitySelector,
        IScheduler workerScheduler,
        IScheduler uiScheduler,
        IEqualityComparer<TIdentity>? identityComparer = null)
    {
        _identitySelector = identitySelector ?? throw new ArgumentNullException(nameof(identitySelector));
        _workerScheduler = workerScheduler ?? throw new ArgumentNullException(nameof(workerScheduler));
        _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
        _identityComparer = identityComparer ?? EqualityComparer<TIdentity>.Default;
        _selectionIdentities = new(_identityComparer);

        _inner.SelectionChanged += InnerSelectionChanged;
        _inner.IndexesChanged += (_, args) => IndexesChanged?.Invoke(this, args);
        _inner.LostSelection += (_, args) => LostSelection?.Invoke(this, args);
        _inner.SourceReset += (_, args) => SourceReset?.Invoke(this, args);
        _inner.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
    }

    public IEnumerable Source
    {
        get => _inner.Source;
        set
        {
            if (ReferenceEquals(_inner.Source, value))
            {
                return;
            }

            Interlocked.Increment(ref _sourceChangeVersion);
            _pendingSelectionSnapshot = null;
            DetachSourceNotifications();
            _inner.Source = value;
            AttachSourceNotifications(value as INotifyCollectionChanged);
            ReconcileSelection();
        }
    }

    public void SetIdentitySource(IEnumerable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (ReferenceEquals(_identitySource, source))
        {
            ReconcileSelection();
            return;
        }

        Interlocked.Increment(ref _sourceChangeVersion);
        _pendingSelectionSnapshot = null;
        _identitySource = source;
        if (!ReferenceEquals(Source, source))
        {
            Source = source;
        }
        else
        {
            ReconcileSelection();
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _sourceChangeVersion);
        _pendingSelectionSnapshot = null;
        DetachSourceNotifications();

        _identitySource = null;
        _inner.SelectionChanged -= InnerSelectionChanged;
    }

    public bool SingleSelect
    {
        get => _inner.SingleSelect;
        set
        {
            if (_inner.SingleSelect == value)
            {
                return;
            }

            RestorePendingSelection();
            _inner.SingleSelect = value;
            UpdateSelectionSnapshot();
        }
    }

    public int SelectedIndex
    {
        get => _inner.SelectedIndex;
        set
        {
            if (value < 0)
            {
                Clear();
                return;
            }

            RestorePendingSelection();
            _inner.SelectedIndex = value;
            UpdateSelectionSnapshot();
        }
    }

    public IReadOnlyList<int> SelectedIndexes => _inner.SelectedIndexes;

    public object? SelectedItem
    {
        get => _inner.SelectedItem;
        set
        {
            if (value is null)
            {
                Clear();
                return;
            }

            RestorePendingSelection();
            _inner.SelectedItem = value;
            UpdateSelectionSnapshot();
        }
    }

    public IReadOnlyList<object?> SelectedItems => _inner.SelectedItems;

    public int AnchorIndex
    {
        get => _inner.AnchorIndex;
        set => _inner.AnchorIndex = value;
    }

    public int Count => _inner.Count;

    public event EventHandler<SelectionModelIndexesChangedEventArgs>? IndexesChanged;
    public event EventHandler<SelectionModelSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler? LostSelection;
    public event EventHandler? SourceReset;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void BeginBatchUpdate()
    {
        _inner.BeginBatchUpdate();
        RestorePendingSelection();
    }

    public void EndBatchUpdate()
    {
        _inner.EndBatchUpdate();
    }

    public bool IsSelected(int index)
    {
        return _inner.IsSelected(index);
    }

    public void Select(int index)
    {
        RestorePendingSelection();
        _inner.Select(index);
        UpdateSelectionSnapshot();
    }

    public void Deselect(int index)
    {
        RestorePendingSelection();
        _inner.Deselect(index);
        UpdateSelectionSnapshot();
    }

    public void SelectRange(int start, int end)
    {
        RestorePendingSelection();
        _inner.SelectRange(start, end);
        UpdateSelectionSnapshot();
    }

    public void DeselectRange(int start, int end)
    {
        RestorePendingSelection();
        _inner.DeselectRange(start, end);
        UpdateSelectionSnapshot();
    }

    public void SelectAll()
    {
        RestorePendingSelection();
        _inner.SelectAll();
        UpdateSelectionSnapshot();
    }

    public void Clear()
    {
        Interlocked.Increment(ref _sourceChangeVersion);
        _pendingSelectionSnapshot = null;
        _inner.Clear();
        _selectionSnapshot.Clear();
        _selectionIdentities.Clear();
    }

    private void InnerSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs e)
    {
        SelectionChanged?.Invoke(this, e);
    }

    private void AttachSourceNotifications(INotifyCollectionChanged? source)
    {
        _sourceNotifications = source;
        if (_sourceNotifications is not null)
        {
            _sourceNotifications.CollectionChanged += SourceOnCollectionChanged;
        }
    }

    private void DetachSourceNotifications()
    {
        if (_sourceNotifications is not null)
        {
            _sourceNotifications.CollectionChanged -= SourceOnCollectionChanged;
            _sourceNotifications = null;
        }
    }

    private void SourceOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectionSnapshot.Count == 0 && _pendingSelectionSnapshot is null)
        {
            return;
        }

        Interlocked.Increment(ref _sourceChangeVersion);
        _pendingSelectionSnapshot ??= _selectionSnapshot.ToArray();
        ScheduleRestoreCapture();
    }

    private void ScheduleRestoreCapture()
    {
        if (_restoreCaptureScheduled || _pendingSelectionSnapshot is null)
        {
            return;
        }

        _restoreCaptureScheduled = true;
        _uiScheduler.Schedule(this, TimeSpan.Zero, (_, model) =>
        {
            model._restoreCaptureScheduled = false;
            var selectedIdentities = model._pendingSelectionSnapshot;
            if (selectedIdentities is null)
            {
                return Disposable.Empty;
            }

            var sourceItems = model.CaptureIdentitySource();
            var version = Volatile.Read(ref model._sourceChangeVersion);
            model._workerScheduler.Schedule(
                (model, selectedIdentities, sourceItems, version),
                TimeSpan.Zero,
                (_, work) =>
                {
                    var indexes = work.model.FindIndexes(work.selectedIdentities, work.sourceItems);
                    work.model._uiScheduler.Schedule(
                        (work.model, work.selectedIdentities, indexes, work.version),
                        TimeSpan.Zero,
                        (_, result) =>
                        {
                            var currentModel = result.model;
                            if (result.version != Volatile.Read(ref currentModel._sourceChangeVersion))
                            {
                                currentModel.ScheduleRestoreCapture();
                                return Disposable.Empty;
                            }

                            if (!ReferenceEquals(currentModel._pendingSelectionSnapshot, result.selectedIdentities))
                            {
                                return Disposable.Empty;
                            }

                            currentModel._pendingSelectionSnapshot = null;
                            currentModel.RestoreSelectionIndexes(result.indexes);
                            return Disposable.Empty;
                        });
                    return Disposable.Empty;
                });
            return Disposable.Empty;
        });
    }

    private object?[] CaptureIdentitySource()
    {
        var source = _identitySource ?? Source;
        if (source is IList list)
        {
            var items = new object?[list.Count];
            list.CopyTo(items, 0);
            return items;
        }

        var result = new List<object?>();
        foreach (var item in source)
        {
            result.Add(item);
        }

        return result.ToArray();
    }

    private void RestoreSelectionIndexes(IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
        {
            _inner.Clear();
            UpdateSelectionSnapshot();
            return;
        }

        if (SelectionMatchesIndexes(indexes))
        {
            UpdateSelectionSnapshot();
            return;
        }

        using (_inner.BatchUpdate())
        {
            _inner.Clear();
            // FindIndexes walks the source in ascending order, so the first result is
            // already the minimum and avoids LINQ enumeration overhead.
            var selectedIndex = indexes[0];
            _inner.SelectedIndex = selectedIndex;

            foreach (var index in indexes)
            {
                if (index != selectedIndex)
                    _inner.Select(index);
            }
        }

        UpdateSelectionSnapshot();
    }

    private void RestoreSelectionSnapshot(IReadOnlyList<TIdentity> snapshot)
    {
        if (snapshot.Count == 0 || Source is null)
        {
            return;
        }

        RestoreSelectionIndexes(FindIndexes(snapshot));
    }

    private void RestorePendingSelection()
    {
        var snapshot = _pendingSelectionSnapshot;
        if (snapshot is null)
        {
            return;
        }

        _pendingSelectionSnapshot = null;
        Interlocked.Increment(ref _sourceChangeVersion);
        RestoreSelectionSnapshot(snapshot);
    }

    private void ReconcileSelection()
    {
        if (_selectionSnapshot.Count == 0)
        {
            UpdateSelectionSnapshot();
            return;
        }

        RestoreSelectionSnapshot(_selectionSnapshot);
    }

    private bool SelectionMatchesIndexes(IReadOnlyList<int> indexes)
    {
        if (_inner.SelectedIndexes.Count != indexes.Count)
        {
            return false;
        }

        for (var i = 0; i < indexes.Count; i++)
        {
            if (_inner.SelectedIndexes[i] != indexes[i])
            {
                return false;
            }
        }

        var source = _identitySource ?? Source;
        if (source is not IList sourceList)
        {
            return false;
        }

        _selectionIdentities.Clear();
        foreach (var index in indexes)
        {
            if (index < 0 || index >= sourceList.Count || sourceList[index] is not T item)
            {
                return false;
            }

            _selectionIdentities.Add(_identitySelector(item));
        }

        foreach (var selectedItem in _inner.SelectedItems)
        {
            if (!TryGetIdentity(selectedItem, out var identity) || !_selectionIdentities.Remove(identity))
            {
                return false;
            }
        }

        return _selectionIdentities.Count == 0;
    }

    private List<int> FindIndexes(IReadOnlyList<TIdentity> snapshot)
    {
        _selectionIdentities.Clear();
        foreach (var identity in snapshot)
        {
            _selectionIdentities.Add(identity);
        }

        _restoredIndexes.Clear();
        var source = _identitySource ?? Source;

        if (source is IList list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index] is not { } item)
                {
                    continue;
                }

                if (item is T typedItem && _selectionIdentities.Contains(_identitySelector(typedItem)))
                {
                    _restoredIndexes.Add(index);
                }
            }

            return _restoredIndexes;
        }

        var sourceIndex = 0;
        foreach (var item in source)
        {
            if (item is T typedItem && _selectionIdentities.Contains(_identitySelector(typedItem)))
            {
                _restoredIndexes.Add(sourceIndex);
            }

            sourceIndex++;
        }

        return _restoredIndexes;
    }

    private List<int> FindIndexes(IReadOnlyList<TIdentity> snapshot, IReadOnlyList<object?> sourceItems)
    {
        HashSet<TIdentity> identities = new(snapshot.Count, _identityComparer);
        foreach (var identity in snapshot)
        {
            identities.Add(identity);
        }

        List<int> indexes = new(snapshot.Count);
        for (var index = 0; index < sourceItems.Count; index++)
        {
            if (sourceItems[index] is T item && identities.Contains(_identitySelector(item)))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private bool TryGetIdentity(object? item, out TIdentity identity)
    {
        if (item is not T typedItem)
        {
            identity = default!;
            return false;
        }

        identity = _identitySelector(typedItem);
        return true;
    }

    private void UpdateSelectionSnapshot()
    {
        _selectionSnapshot.Clear();
        _selectionIdentities.Clear();

        if (Source is null)
        {
            return;
        }

        foreach (var selectedItem in _inner.SelectedItems)
        {
            if (TryGetIdentity(selectedItem, out var identity))
            {
                if (_selectionIdentities.Add(identity))
                    _selectionSnapshot.Add(identity);
            }
        }
    }
}
