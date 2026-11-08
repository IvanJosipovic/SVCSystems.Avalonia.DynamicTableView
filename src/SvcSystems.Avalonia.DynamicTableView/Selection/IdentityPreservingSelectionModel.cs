using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>
/// Delegates selection operations and state to Avalonia's SelectionModel. Adds only stable-key restoration
/// around DynamicData view updates, and drops keys that are no longer visible.
/// </summary>
internal sealed class IdentityPreservingSelectionModel<T, TIdentity> : IIdentityPreservingSelectionModel<T>, INotifyPropertyChanged
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
    private readonly List<int> _selectedIndexScratch = [];
    private readonly List<int> _targetedRestoreIndexes = new(1);
    private readonly List<object?> _selectedItemScratch = [];
    private readonly List<(int Index, TIdentity Identity)> _replacementSelectionIndexes = [];
    private readonly ConcurrentBag<List<int>> _workerIndexListPool = [];
    private INotifyCollectionChanged? _sourceNotifications;
    private IEnumerable? _identitySource;
    private SelectionSnapshot? _selectionSnapshotCapture;
    private SelectionSnapshot? _pendingSelectionSnapshot;
    private bool _restoreCaptureScheduled;
    private bool _replacementRestoreScheduled;
    private bool _replacementRestoreCanComplete;
    private bool _selectionRestorePending;
    private bool _selectionRestoreInProgress;
    private bool _settingSource;
    private object? _boundChangeRemovedItem;
    private object? _boundChangeAddedItem;
    private int _boundChangeAddedIndex = -1;
    private byte _boundChangeRemoveCount;
    private byte _boundChangeAddCount;
    private bool _boundChangeIsAmbiguous;
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
            _selectionRestorePending = false;
            _replacementSelectionIndexes.Clear();
            _replacementRestoreCanComplete = false;
            ResetBoundChangeTracking();
            DetachSourceNotifications();
            _settingSource = true;
            try
            {
                AttachSourceNotifications(value as INotifyCollectionChanged);
                _inner.Source = value;
                CompleteSourceNotificationAttachment();
            }
            finally
            {
                _settingSource = false;
            }

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
        _selectionRestorePending = false;
        _replacementSelectionIndexes.Clear();
        _replacementRestoreCanComplete = false;
        ResetBoundChangeTracking();
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
        _selectionSnapshotCapture = null;
        _selectionRestorePending = false;
        _replacementSelectionIndexes.Clear();
        _replacementRestoreCanComplete = false;
        ResetBoundChangeTracking();
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
        _selectionRestorePending = false;
        _replacementSelectionIndexes.Clear();
        _replacementRestoreCanComplete = false;
        _inner.Clear();
        _selectionSnapshot.Clear();
        _selectionSnapshotCapture = null;
        _selectionIdentities.Clear();
    }

    private void InnerSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs e)
    {
        if (_replacementSelectionIndexes.Count > 0)
        {
            // The source notification temporarily invalidates the old selected item.
            // Suppress that intermediate state; the replacement is reconciled before
            // Avalonia renders the updated collection.
            return;
        }

        if (!_settingSource && !_selectionRestorePending && !_selectionRestoreInProgress)
        {
            UpdateSelectionSnapshot();
        }

        SelectionChanged?.Invoke(this, e);
    }

    private void AttachSourceNotifications(INotifyCollectionChanged? source)
    {
        _sourceNotifications = source;
        if (_sourceNotifications is not null)
        {
            _sourceNotifications.CollectionChanged += SourceOnCollectionChanging;
        }
    }

    private void CompleteSourceNotificationAttachment()
    {
        if (_sourceNotifications is not null)
        {
            _sourceNotifications.CollectionChanged += SourceOnCollectionChanged;
        }
    }

    private void DetachSourceNotifications()
    {
        if (_sourceNotifications is not null)
        {
            _sourceNotifications.CollectionChanged -= SourceOnCollectionChanging;
            _sourceNotifications.CollectionChanged -= SourceOnCollectionChanged;
            _sourceNotifications = null;
        }
    }

    private void SourceOnCollectionChanging(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectionSnapshot.Count == 0 && !_selectionRestorePending)
        {
            return;
        }

        TrackBoundCollectionChange(e);

        var restoreWasPending = _selectionRestorePending;
        if (!restoreWasPending)
        {
            _replacementRestoreCanComplete = false;
        }

        if (!restoreWasPending &&
            e.Action == NotifyCollectionChangedAction.Replace &&
            e.NewItems is not null &&
            e.NewStartingIndex >= 0 &&
            !ContainsSelectedIndex(e.NewStartingIndex, e.NewItems.Count))
        {
            // Replacing an unselected item cannot change selected identities or indexes.
            return;
        }

        Interlocked.Increment(ref _sourceChangeVersion);
        _selectionRestorePending = true;

        if (e.Action != NotifyCollectionChangedAction.Replace ||
            e.NewItems is null ||
            e.NewStartingIndex < 0)
        {
            _replacementRestoreCanComplete = false;
            return;
        }

        if (!restoreWasPending)
        {
            _replacementRestoreCanComplete = true;
        }

        if (e.OldItems is null || e.OldItems.Count != e.NewItems.Count)
        {
            _replacementRestoreCanComplete = false;
            return;
        }

        for (var itemIndex = 0; itemIndex < e.NewItems.Count; itemIndex++)
        {
            var replacementIndex = e.NewStartingIndex + itemIndex;
            if (!_inner.IsSelected(replacementIndex))
            {
                continue;
            }

            if (e.OldItems[itemIndex] is not T oldItem || e.NewItems[itemIndex] is not T newItem)
            {
                _replacementRestoreCanComplete = false;
                continue;
            }

            var oldIdentity = _identitySelector(oldItem);
            var identity = _identitySelector(newItem);
            if (!_identityComparer.Equals(oldIdentity, identity))
            {
                _replacementRestoreCanComplete = false;
                continue;
            }

            var existingIndex = -1;
            for (var index = 0; index < _replacementSelectionIndexes.Count; index++)
            {
                if (_replacementSelectionIndexes[index].Index == replacementIndex)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                _replacementSelectionIndexes[existingIndex] = (replacementIndex, identity);
            }
            else
            {
                _replacementSelectionIndexes.Add((replacementIndex, identity));
            }
        }
    }

    public void OnBoundChangeset(bool isSingleSameKeyUpdate, T previousItem, T currentItem)
    {
        try
        {
            // Bypass the source scan only when one selected key and one matching collection
            // Remove/Add pair identify the exact visible replacement index.
            if (!isSingleSameKeyUpdate ||
                !_selectionRestorePending ||
                _pendingSelectionSnapshot is not null ||
                _selectionSnapshot.Count != 1 ||
                _boundChangeIsAmbiguous ||
                _boundChangeRemoveCount != 1 ||
                _boundChangeAddCount != 1 ||
                !SameRow(_boundChangeRemovedItem, previousItem) ||
                !SameRow(_boundChangeAddedItem, currentItem))
            {
                return;
            }

            TIdentity previousIdentity = _identitySelector(previousItem);
            if (!_identityComparer.Equals(_selectionSnapshot[0], previousIdentity) ||
                !_identityComparer.Equals(previousIdentity, _identitySelector(currentItem)))
            {
                return;
            }

            var source = _identitySource ?? Source;
            if (!ReferenceEquals(Source, source) ||
                source is not IList list ||
                _boundChangeAddedIndex < 0 ||
                _boundChangeAddedIndex >= list.Count ||
                !SameRow(list[_boundChangeAddedIndex], currentItem))
            {
                return;
            }

            _targetedRestoreIndexes.Clear();
            _targetedRestoreIndexes.Add(_boundChangeAddedIndex);
            RestoreSelectionIndexes(_targetedRestoreIndexes);

            if (IsSingleSelectionAtRow(_boundChangeAddedIndex, currentItem))
            {
                CompleteSelectionRestore(updateSelectionSnapshot: false);
            }
        }
        finally
        {
            ResetBoundChangeTracking();
        }
    }

    private void TrackBoundCollectionChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Remove when e.OldItems is { Count: 1 } && e.OldStartingIndex >= 0:
                if (_boundChangeRemoveCount == 0)
                {
                    _boundChangeRemovedItem = e.OldItems[0];
                    _boundChangeRemoveCount = 1;
                }
                else
                {
                    _boundChangeRemoveCount = 2;
                }
                break;

            case NotifyCollectionChangedAction.Add when e.NewItems is { Count: 1 } && e.NewStartingIndex >= 0:
                if (_boundChangeAddCount == 0)
                {
                    _boundChangeAddedItem = e.NewItems[0];
                    _boundChangeAddedIndex = e.NewStartingIndex;
                    _boundChangeAddCount = 1;
                }
                else
                {
                    _boundChangeAddCount = 2;
                }
                break;

            default:
                _boundChangeIsAmbiguous = true;
                break;
        }
    }

    private static bool SameRow(object? candidate, T expected)
    {
        if (!typeof(T).IsValueType)
        {
            return ReferenceEquals(candidate, expected);
        }

        return candidate is T candidateItem && EqualityComparer<T>.Default.Equals(candidateItem, expected);
    }

    private bool IsSingleSelectionAtRow(int index, T item)
    {
        return _inner.SelectedIndexes.Count == 1 &&
            _inner.SelectedIndexes[0] == index &&
            _inner.SelectedItems.Count == 1 &&
            SameRow(_inner.SelectedItems[0], item);
    }

    private void ResetBoundChangeTracking()
    {
        _boundChangeRemovedItem = null;
        _boundChangeAddedItem = null;
        _boundChangeAddedIndex = -1;
        _boundChangeRemoveCount = 0;
        _boundChangeAddCount = 0;
        _boundChangeIsAmbiguous = false;
    }

    private bool ContainsSelectedIndex(int startIndex, int count)
    {
        for (var itemIndex = 0; itemIndex < count; itemIndex++)
        {
            if (_inner.IsSelected(startIndex + itemIndex))
            {
                return true;
            }
        }

        return false;
    }

    private void SourceOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ScheduleReplacementRestore())
        {
            ScheduleRestoreCapture();
        }
    }

    private bool ScheduleReplacementRestore()
    {
        if (_replacementSelectionIndexes.Count == 0)
        {
            return false;
        }

        if (_replacementRestoreScheduled)
        {
            return true;
        }

        // ImmediateScheduler executes inline, while Avalonia is still dispatching the
        // collection notification. Its normal identity scan already restores inline.
        if (_uiScheduler is ImmediateScheduler)
        {
            _replacementSelectionIndexes.Clear();
            return false;
        }

        _replacementRestoreScheduled = true;
        if (_uiScheduler is AvaloniaDispatcherScheduler dispatcherScheduler)
        {
            dispatcherScheduler.ScheduleAtPriority(this, DispatcherPriority.Send, static (_, model) =>
            {
                model.RestoreReplacementSelection();
                return Disposable.Empty;
            });
        }
        else
        {
            _uiScheduler.Schedule(this, TimeSpan.Zero, static (_, model) =>
            {
                model.RestoreReplacementSelection();
                return Disposable.Empty;
            });
        }

        return true;
    }

    private void RestoreReplacementSelection()
    {
        _replacementRestoreScheduled = false;
        if (!_selectionRestorePending || _replacementSelectionIndexes.Count == 0)
        {
            _replacementSelectionIndexes.Clear();
            return;
        }

        var source = _identitySource ?? Source;
        if (source is not IList list)
        {
            _replacementSelectionIndexes.Clear();
            ScheduleRestoreCapture();
            return;
        }

        IReadOnlyList<TIdentity> selectedIdentities = _pendingSelectionSnapshot is { } pendingSnapshot
            ? pendingSnapshot
            : _selectionSnapshot;
        var canCompleteWithoutIdentityScan = _replacementRestoreCanComplete;
        _inner.BeginBatchUpdate();
        try
        {
            foreach ((var index, var identity) in _replacementSelectionIndexes)
            {
                if (index >= 0 && index < list.Count &&
                    list[index] is T item &&
                    _identityComparer.Equals(_identitySelector(item), identity))
                {
                    _inner.Select(index);
                }
                else
                {
                    canCompleteWithoutIdentityScan = false;
                }
            }
        }
        finally
        {
            _replacementSelectionIndexes.Clear();
            _inner.EndBatchUpdate();
        }

        if (canCompleteWithoutIdentityScan &&
            _inner.SelectedIndexes.Count == selectedIdentities.Count &&
            _inner.SelectedItems.Count == selectedIdentities.Count)
        {
            CompleteSelectionRestore(updateSelectionSnapshot: false);
            return;
        }

        if (_selectionRestorePending && SelectionMatchesCurrentSource(selectedIdentities))
        {
            CompleteSelectionRestore();
            return;
        }

        ScheduleRestoreCapture();
    }

    private void ScheduleRestoreCapture()
    {
        if (_restoreCaptureScheduled || !_selectionRestorePending)
        {
            return;
        }

        _restoreCaptureScheduled = true;
        _uiScheduler.Schedule(this, TimeSpan.Zero, (_, model) =>
        {
            model._restoreCaptureScheduled = false;
            if (!model._selectionRestorePending)
            {
                return Disposable.Empty;
            }

            IReadOnlyList<TIdentity> selectedIdentities = model._pendingSelectionSnapshot is { } pendingSnapshot
                ? pendingSnapshot
                : model._selectionSnapshot;
            if (model.SelectionMatchesCurrentSource(selectedIdentities))
            {
                model.CompleteSelectionRestore();
                return Disposable.Empty;
            }

            SelectionSnapshot selectionSnapshot = model._pendingSelectionSnapshot ??=
                model.GetSelectionSnapshot();
            var sourceItems = model.CaptureIdentitySource(out var sourceItemCount, out var sourceItemsPooled);
            var version = Volatile.Read(ref model._sourceChangeVersion);
            model._workerScheduler.Schedule(
                (model, selectedIdentities: selectionSnapshot, sourceItems, sourceItemCount, sourceItemsPooled, version),
                TimeSpan.Zero,
                (_, work) =>
                {
                    List<int> indexes;
                    try
                    {
                        indexes = work.model.FindIndexes(
                            work.selectedIdentities,
                            work.sourceItems,
                            work.sourceItemCount);
                    }
                    finally
                    {
                        if (work.sourceItemsPooled)
                        {
                            if (work.sourceItems is T[] typedItems)
                            {
                                ArrayPool<T>.Shared.Return(typedItems, clearArray: true);
                            }
                            else
                            {
                                ArrayPool<object?>.Shared.Return((object?[])work.sourceItems, clearArray: true);
                            }
                        }
                    }

                    work.model._uiScheduler.Schedule(
                        (work.model, work.selectedIdentities, indexes, work.version),
                        TimeSpan.Zero,
                        (_, result) =>
                        {
                            var currentModel = result.model;
                            try
                            {
                                if (result.version != Volatile.Read(ref currentModel._sourceChangeVersion))
                                {
                                    currentModel.ScheduleRestoreCapture();
                                    return Disposable.Empty;
                                }

                                if (!currentModel._selectionRestorePending ||
                                    !ReferenceEquals(currentModel._pendingSelectionSnapshot, result.selectedIdentities))
                                {
                                    return Disposable.Empty;
                                }

                                currentModel._selectionRestorePending = false;
                                currentModel._pendingSelectionSnapshot = null;
                                currentModel._replacementSelectionIndexes.Clear();
                                currentModel.RestoreSelectionIndexes(result.indexes);
                                return Disposable.Empty;
                            }
                            finally
                            {
                                currentModel.ReturnWorkerIndexes(result.indexes);
                            }
                        });
                    return Disposable.Empty;
                });
            return Disposable.Empty;
        });
    }

    private Array CaptureIdentitySource(out int itemCount, out bool isPooled)
    {
        var source = _identitySource ?? Source;
        if (source is IList<T> typedList)
        {
            itemCount = typedList.Count;
            if (itemCount == 0)
            {
                isPooled = false;
                return Array.Empty<T>();
            }

            T[] items = ArrayPool<T>.Shared.Rent(itemCount);
            try
            {
                typedList.CopyTo(items, 0);
            }
            catch
            {
                ArrayPool<T>.Shared.Return(items, clearArray: true);
                throw;
            }

            isPooled = true;
            return items;
        }

        if (source is IList list)
        {
            itemCount = list.Count;
            if (itemCount == 0)
            {
                isPooled = false;
                return Array.Empty<object?>();
            }

            var items = ArrayPool<object?>.Shared.Rent(itemCount);
            try
            {
                list.CopyTo(items, 0);
            }
            catch
            {
                ArrayPool<object?>.Shared.Return(items, clearArray: true);
                throw;
            }

            isPooled = true;
            return items;
        }

        var result = new List<object?>();
        foreach (var item in source)
        {
            result.Add(item);
        }

        object?[] itemsArray = result.ToArray();
        itemCount = itemsArray.Length;
        isPooled = false;
        return itemsArray;
    }

    private void RestoreSelectionIndexes(List<int> indexes)
    {
        if (indexes.Count == 0)
        {
            _inner.Clear();
            UpdateSelectionSnapshot();
            return;
        }

        if (SelectionMatchesIndexes(indexes))
        {
            if (indexes.Count != _selectionSnapshot.Count)
            {
                UpdateSelectionSnapshot();
            }

            return;
        }

        var source = _identitySource ?? Source;
        if (source is IList sourceList)
        {
            RestoreSelectionIndexesByDiff(sourceList, indexes);
        }
        else
        {
            ResetSelectionIndexes(indexes);
        }

        if (indexes.Count != _selectionSnapshot.Count)
        {
            UpdateSelectionSnapshot();
        }
    }

    private void RestoreSelectionIndexesByDiff(IList source, List<int> indexes)
    {
        if (_selectionRestoreInProgress)
        {
            ResetSelectionIndexes(indexes);
            return;
        }

        _selectionRestoreInProgress = true;
        try
        {
            _selectedIndexScratch.Clear();
            foreach (var selectedIndex in _inner.SelectedIndexes)
            {
                _selectedIndexScratch.Add(selectedIndex);
            }

            _selectedItemScratch.Clear();
            foreach (var selectedItem in _inner.SelectedItems)
            {
                _selectedItemScratch.Add(selectedItem);
            }

            if (_selectedIndexScratch.Count == 0)
            {
                using (_inner.BatchUpdate())
                {
                    _inner.SelectedIndex = indexes[0];
                    for (var index = 1; index < indexes.Count; index++)
                    {
                        _inner.Select(indexes[index]);
                    }
                }

                return;
            }

            var selectedPosition = _selectedIndexScratch.Count - 1;
            var targetPosition = indexes.Count - 1;
            using (_inner.BatchUpdate())
            {
                while (selectedPosition >= 0 || targetPosition >= 0)
                {
                    if (selectedPosition < 0)
                    {
                        _inner.Select(indexes[targetPosition--]);
                        continue;
                    }

                    if (targetPosition < 0)
                    {
                        _inner.Deselect(_selectedIndexScratch[selectedPosition--]);
                        continue;
                    }

                    var selectedIndex = _selectedIndexScratch[selectedPosition];
                    var targetIndex = indexes[targetPosition];
                    if (selectedIndex > targetIndex)
                    {
                        _inner.Deselect(selectedIndex);
                        selectedPosition--;
                        continue;
                    }

                    if (targetIndex > selectedIndex)
                    {
                        _inner.Select(targetIndex);
                        targetPosition--;
                        continue;
                    }

                    var identitiesMatch = source[selectedIndex] is T sourceItem &&
                        selectedPosition < _selectedItemScratch.Count &&
                        TryGetIdentity(_selectedItemScratch[selectedPosition], out var selectedIdentity) &&
                        _identityComparer.Equals(selectedIdentity, _identitySelector(sourceItem));
                    if (!identitiesMatch)
                    {
                        _inner.Deselect(selectedIndex);
                        _inner.Select(targetIndex);
                    }

                    selectedPosition--;
                    targetPosition--;
                }
            }
        }
        finally
        {
            _selectedIndexScratch.Clear();
            _selectedItemScratch.Clear();
            _selectionRestoreInProgress = false;
        }
    }

    private void ResetSelectionIndexes(List<int> indexes)
    {
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
                {
                    _inner.Select(index);
                }
            }
        }
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
        if (!_selectionRestorePending)
        {
            return;
        }

        snapshot ??= GetSelectionSnapshot();
        _selectionRestorePending = false;
        _pendingSelectionSnapshot = null;
        _replacementSelectionIndexes.Clear();
        _replacementRestoreCanComplete = false;
        Interlocked.Increment(ref _sourceChangeVersion);
        RestoreSelectionSnapshot(snapshot);
    }

    private void CompleteSelectionRestore(bool updateSelectionSnapshot = true)
    {
        _selectionRestorePending = false;
        _pendingSelectionSnapshot = null;
        _replacementSelectionIndexes.Clear();
        _replacementRestoreCanComplete = false;
        if (updateSelectionSnapshot)
        {
            UpdateSelectionSnapshot();
        }
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

    private bool SelectionMatchesIndexes(List<int> indexes)
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

    private bool SelectionMatchesCurrentSource(IReadOnlyList<TIdentity> snapshot)
    {
        if (_inner.SelectedIndexes.Count != snapshot.Count ||
            _inner.SelectedItems.Count != snapshot.Count)
        {
            return false;
        }

        var source = _identitySource ?? Source;
        if (source is not IList sourceList)
        {
            return false;
        }

        _selectionIdentities.Clear();
        foreach (var identity in snapshot)
        {
            _selectionIdentities.Add(identity);
        }

        foreach (var index in _inner.SelectedIndexes)
        {
            if (index < 0 || index >= sourceList.Count ||
                sourceList[index] is not T item ||
                !_selectionIdentities.Remove(_identitySelector(item)))
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

    private List<int> FindIndexes(
        SelectionSnapshot snapshot,
        Array sourceItems,
        int sourceItemCount)
    {
        if (!_workerIndexListPool.TryTake(out var indexes))
        {
            indexes = new(snapshot.Count);
        }
        else
        {
            indexes.Clear();
            indexes.EnsureCapacity(snapshot.Count);
        }

        try
        {
            HashSet<TIdentity> identities = snapshot.GetIdentitySet();

            if (sourceItems is T[] typedItems)
            {
                for (var index = 0; index < sourceItemCount; index++)
                {
                    T item = typedItems[index];
                    if (item is not null && identities.Contains(_identitySelector(item)))
                    {
                        indexes.Add(index);
                    }
                }
            }
            else
            {
                object?[] untypedItems = (object?[])sourceItems;
                for (var index = 0; index < sourceItemCount; index++)
                {
                    if (untypedItems[index] is T item && identities.Contains(_identitySelector(item)))
                    {
                        indexes.Add(index);
                    }
                }
            }

            return indexes;
        }
        catch
        {
            ReturnWorkerIndexes(indexes);
            throw;
        }
    }

    private void ReturnWorkerIndexes(List<int> indexes)
    {
        indexes.Clear();
        _workerIndexListPool.Add(indexes);
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
        var previousSnapshot = _selectionSnapshotCapture;
        _selectionSnapshot.Clear();
        _selectionIdentities.Clear();

        if (Source is null)
        {
            _selectionSnapshotCapture = null;
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

        if (previousSnapshot is null || previousSnapshot.Count != _selectionSnapshot.Count)
        {
            _selectionSnapshotCapture = null;
            return;
        }

        foreach (var identity in previousSnapshot)
        {
            if (!_selectionIdentities.Contains(identity))
            {
                _selectionSnapshotCapture = null;
                return;
            }
        }
    }

    private SelectionSnapshot GetSelectionSnapshot()
    {
        return _selectionSnapshotCapture ??=
            new SelectionSnapshot(_selectionSnapshot.ToArray(), _identityComparer);
    }

    private sealed class SelectionSnapshot : IReadOnlyList<TIdentity>
    {
        private readonly TIdentity[] _identities;
        private readonly IEqualityComparer<TIdentity> _comparer;
        private HashSet<TIdentity>? _identitySet;

        public SelectionSnapshot(TIdentity[] identities, IEqualityComparer<TIdentity> comparer)
        {
            _identities = identities;
            _comparer = comparer;
        }

        public int Count => _identities.Length;

        public TIdentity this[int index] => _identities[index];

        public HashSet<TIdentity> GetIdentitySet()
        {
            HashSet<TIdentity>? identitySet = Volatile.Read(ref _identitySet);
            if (identitySet is not null)
            {
                return identitySet;
            }

            HashSet<TIdentity> newIdentitySet = new(_identities.Length, _comparer);
            foreach (var identity in _identities)
            {
                newIdentitySet.Add(identity);
            }

            return Interlocked.CompareExchange(ref _identitySet, newIdentitySet, null) ?? newIdentitySet;
        }

        public IEnumerator<TIdentity> GetEnumerator()
        {
            return ((IEnumerable<TIdentity>)_identities).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _identities.GetEnumerator();
        }
    }
}
