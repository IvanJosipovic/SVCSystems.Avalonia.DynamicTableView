using System.Collections.Specialized;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using DynamicData;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>DynamicData-backed source that owns filtering, search, sorting, and selection.</summary>
public sealed class DynamicTableViewSource<T, TKey> : IDynamicTableViewSource
    where T : notnull
    where TKey : notnull
{
    private readonly Func<T, TKey> _keySelector;
    private readonly DynamicTableViewSelectionIdentityMode _selectionIdentityMode;
    private readonly IScheduler _workerScheduler;
    private readonly IScheduler _uiScheduler;
    private readonly IScheduler _searchScheduler;
    private readonly TimeSpan _searchDebounce;
    private readonly Subject<string> _searchChanges = new();
    private readonly BehaviorSubject<Func<T, bool>> _filterSubject = new(static _ => true);
    private readonly BehaviorSubject<IComparer<T>> _sortSubject = new(Comparer<T>.Create(static (_, _) => 0));
    private readonly Dictionary<string, DynamicTableViewFilterDescriptor> _filters = new(StringComparer.Ordinal);
    private IReadOnlyList<DynamicTableViewFilterDescriptor> _filterDescriptors = Array.AsReadOnly(Array.Empty<DynamicTableViewFilterDescriptor>());
    private readonly Dictionary<string, Func<T, bool>> _customFilters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<T, bool>> _scopeFilters = new(StringComparer.Ordinal);
    private readonly IIdentityPreservingSelectionModel _selectionModel;
    private readonly IDisposable _searchSubscription;
    private readonly IDisposable _pipelineSubscription;
    private IReadOnlyList<DynamicTableViewSortDescriptor> _sortDescriptors = Array.AsReadOnly(Array.Empty<DynamicTableViewSortDescriptor>());
    private ReadOnlyObservableCollection<T> _items;
    private string _searchText = string.Empty;
    private bool _disposed;

    /// <summary>Creates a source from a caller-owned, observable DynamicData cache and stable row keys.</summary>
    /// <param name="keySelector">Returns each row's stable identity. It must be safe to call on the configured worker scheduler.</param>
    /// <param name="workerScheduler">Schedules query processing and selection identity matching.</param>
    /// <param name="uiScheduler">Schedules bound collection and selection updates.</param>
    public DynamicTableViewSource(
        IObservableCache<T, TKey> cache,
        Func<T, TKey> keySelector,
        IEnumerable<DynamicTableViewColumn<T>>? columns = null,
        IScheduler? workerScheduler = null,
        IScheduler? uiScheduler = null,
        TimeSpan? searchDebounce = null,
        IScheduler? searchScheduler = null,
        DynamicTableViewSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        options ??= new DynamicTableViewSourceOptions();
        _selectionIdentityMode = options.SelectionIdentityMode;
        _sortSubject.OnNext(BuildComparer([]));
        _workerScheduler = workerScheduler ?? TaskPoolScheduler.Default;
        _uiScheduler = uiScheduler ?? new AvaloniaDispatcherScheduler();
        _searchScheduler = searchScheduler ?? _workerScheduler;
        _searchDebounce = searchDebounce ?? TimeSpan.FromMilliseconds(250);
        if (_searchDebounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(searchDebounce));
        _selectionModel = _selectionIdentityMode switch
        {
            DynamicTableViewSelectionIdentityMode.Key =>
                new IdentityPreservingSelectionModel<T, TKey>(_keySelector, _workerScheduler, _uiScheduler),
            DynamicTableViewSelectionIdentityMode.Reference =>
                new IdentityPreservingSelectionModel<T, object>(static item => item, _workerScheduler, _uiScheduler, ReferenceEqualityComparer.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(options), _selectionIdentityMode, "Unsupported selection identity mode.")
        };
        _selectionModel.SingleSelect = false;

        Columns = [];
        if (columns is not null)
        {
            foreach (var column in columns)
            {
                Columns.Add(column);
            }
        }

        Columns.CollectionChanged += ColumnsOnCollectionChanged;
        _searchSubscription = _searchChanges
            .Select(query => _searchDebounce == TimeSpan.Zero || string.IsNullOrWhiteSpace(query)
                ? Observable.Return(query)
                : Observable.Timer(_searchDebounce, _searchScheduler).Select(_ => query))
            .Switch()
            .ObserveOn(_workerScheduler)
            .Subscribe(_ => UpdateFilterPredicate());

#pragma warning disable IL2091 // DynamicData 9.4.33 annotates SortAndBind<T> with All, but its implementation uses the supplied comparer and does not reflect over row members.
        _pipelineSubscription = cache.Connect()
            .ObserveOn(_workerScheduler)
            .Filter(_filterSubject.ObserveOn(_workerScheduler))
            .SortAndBind(out _items, _sortSubject.ObserveOn(_workerScheduler), new()
            {
                ResetOnFirstTimeLoad = true,
                Scheduler = _uiScheduler
            })
            .Subscribe(
                static _ => { },
                ex => SourceError?.Invoke(this, ex));
#pragma warning restore IL2091

        _selectionModel.SetIdentitySource(_items);
    }

    /// <inheritdoc />
    public IEnumerable Items => _items;

    /// <inheritdoc />
    public ObservableCollection<DynamicTableViewColumn> Columns { get; }

    /// <inheritdoc />
    public ISelectionModel SelectionModel => _selectionModel;

    /// <inheritdoc />
    public string SearchText
    {
        get => _searchText;
        set
        {
            ThrowIfDisposed();
            value ??= string.Empty;
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
                return;

            _searchText = value;
            _searchChanges.OnNext(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicTableViewSortDescriptor> SortDescriptors => _sortDescriptors;

    /// <inheritdoc />
    public IReadOnlyList<DynamicTableViewFilterDescriptor> FilterDescriptors => _filterDescriptors;

    /// <summary>Raised when DynamicData source pipeline fails.</summary>
    public event EventHandler<Exception>? SourceError;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void SetFilter(DynamicTableViewFilterDescriptor? descriptor, string? columnKey = null)
    {
        ThrowIfDisposed();
        var key = descriptor?.ColumnKey ?? columnKey ?? throw new ArgumentException("Column key required when clearing a filter.", nameof(columnKey));
        if (descriptor is null)
        {
            if (_filters.Remove(key))
                _filterDescriptors = Array.AsReadOnly(_filters.Values.ToArray());
        }
        else
        {
            _filters[key] = descriptor;
            _filterDescriptors = Array.AsReadOnly(_filters.Values.ToArray());
        }
        UpdateFilterPredicate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets or clears a custom predicate for one column.</summary>
    public void SetCustomFilter(string key, Func<object, bool>? predicate)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (predicate is null)
            _customFilters.Remove(key);
        else
            _customFilters[key] = item => predicate(item);
        UpdateFilterPredicate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ClearFilters()
    {
        ThrowIfDisposed();
        if (_filters.Count == 0 && _customFilters.Count == 0)
            return;
        if (_filters.Count > 0)
        {
            _filters.Clear();
            _filterDescriptors = Array.AsReadOnly(Array.Empty<DynamicTableViewFilterDescriptor>());
        }
        _customFilters.Clear();
        UpdateFilterPredicate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetScopeFilter(string key, Func<object, bool>? predicate)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (predicate is null)
            _scopeFilters.Remove(key);
        else
            _scopeFilters[key] = item => predicate(item);
        UpdateFilterPredicate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetSort(DynamicTableViewSortDescriptor[] descriptors)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(descriptors);
        _sortDescriptors = Array.AsReadOnly(descriptors);
        _sortSubject.OnNext(BuildComparer(_sortDescriptors));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool AreSameRows(object? first, object? second)
        => first is T typedFirst && second is T typedSecond &&
           (_selectionIdentityMode == DynamicTableViewSelectionIdentityMode.Reference
               ? ReferenceEquals(first, second)
               : EqualityComparer<TKey>.Default.Equals(_keySelector(typedFirst), _keySelector(typedSecond)));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Columns.CollectionChanged -= ColumnsOnCollectionChanged;
        _searchSubscription.Dispose();
        _pipelineSubscription.Dispose();
        _searchChanges.Dispose();
        _filterSubject.Dispose();
        _sortSubject.Dispose();
        _selectionModel.Dispose();
    }

    private void ColumnsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateFilterPredicate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFilterPredicate()
    {
        var predicates = new List<Func<T, bool>>(_filters.Count + _customFilters.Count + _scopeFilters.Count + 1);
        foreach (var descriptor in _filters.Values)
        {
            var column = FindColumn(descriptor.ColumnKey);
            if (column is not null)
            {
                var columnFilter = column.CreateFilter(descriptor, Matches);
                predicates.Add(item => columnFilter(item));
            }
        }
        foreach ((var key, var predicate) in _customFilters)
        {
            if (FindColumn(key) is not null)
                predicates.Add(predicate);
        }
        foreach (var predicate in _scopeFilters.Values)
            predicates.Add(predicate);

        var query = _searchText.Trim();
        if (query.Length > 0)
        {
            var searchableColumns = Columns.Where(static column => column.IsSearchable).ToArray();
            predicates.Add(item =>
            {
                for (var i = 0; i < searchableColumns.Length; i++)
                {
                    var text = searchableColumns[i].GetDisplayValue(item);
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            });
        }

        var filter = predicates.Count switch
        {
            0 => static _ => true,
            1 => predicates[0],
            _ => item =>
            {
                for (var i = 0; i < predicates.Count; i++)
                {
                    if (!predicates[i](item))
                        return false;
                }
                return true;
            }
        };
        _filterSubject.OnNext(filter);
    }

    private IComparer<T> BuildComparer(IReadOnlyList<DynamicTableViewSortDescriptor> descriptors)
    {
        if (descriptors.Count == 0)
            return Comparer<T>.Create((left, right) => CompareValues(_keySelector(left), _keySelector(right)));
        var entries = new List<(DynamicTableViewColumn Column, ListSortDirection Direction)>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var column = FindColumn(descriptor.ColumnKey);
            if (column is not null)
                entries.Add((column, descriptor.Direction));
        }
        if (entries.Count == 0)
            return Comparer<T>.Create((left, right) => CompareValues(_keySelector(left), _keySelector(right)));

        return Comparer<T>.Create((left, right) =>
        {
            for (var i = 0; i < entries.Count; i++)
            {
                (var column, var direction) = entries[i];
                var result = column.CompareRows(left, right, CompareValues);
                if (result != 0)
                    return direction == ListSortDirection.Ascending ? result : -result;
            }
            return CompareValues(_keySelector(left), _keySelector(right));
        });
    }

    private DynamicTableViewColumn? FindColumn(string key)
    {
        foreach (var column in Columns)
        {
            if (string.Equals(column.Key, key, StringComparison.Ordinal))
                return column;
        }
        return null;
    }

    private static bool Matches(object? candidate, DynamicTableViewFilterDescriptor descriptor)
    {
        var value = descriptor.Value;
        switch (descriptor.Operator)
        {
            case DynamicTableViewFilterOperator.IsNull: return candidate is null;
            case DynamicTableViewFilterOperator.IsNotNull: return candidate is not null;
            case DynamicTableViewFilterOperator.IsTrue: return candidate is true;
            case DynamicTableViewFilterOperator.IsFalse: return candidate is false;
            case DynamicTableViewFilterOperator.Contains: return candidate is string contains && value is string query && contains.Contains(query, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.DoesNotContain: return candidate is not string notContains || value is not string notQuery || !notContains.Contains(notQuery, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.StartsWith: return candidate is string starts && value is string start && starts.StartsWith(start, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.DoesNotStartWith: return candidate is not string notStarts || value is not string notStart || !notStarts.StartsWith(notStart, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.EndsWith: return candidate is string ends && value is string end && ends.EndsWith(end, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.DoesNotEndWith: return candidate is not string notEnds || value is not string notEnd || !notEnds.EndsWith(notEnd, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.Equals: return AreEqual(candidate, value, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.NotEquals: return !AreEqual(candidate, value, descriptor.StringComparison);
            case DynamicTableViewFilterOperator.GreaterThan: return CompareValues(candidate, value) > 0;
            case DynamicTableViewFilterOperator.GreaterThanOrEqual: return CompareValues(candidate, value) >= 0;
            case DynamicTableViewFilterOperator.LessThan: return CompareValues(candidate, value) < 0;
            case DynamicTableViewFilterOperator.LessThanOrEqual: return CompareValues(candidate, value) <= 0;
            case DynamicTableViewFilterOperator.Between: return CompareValues(candidate, value) >= 0 && CompareValues(candidate, descriptor.SecondValue) <= 0;
            case DynamicTableViewFilterOperator.NotBetween: return CompareValues(candidate, value) < 0 || CompareValues(candidate, descriptor.SecondValue) > 0;
            case DynamicTableViewFilterOperator.In:
                var values = descriptor.Values ?? [];
                for (var i = 0; i < values.Count; i++)
                    if (CompareValues(candidate, values[i]) == 0) return true;
                return false;
            default: return true;
        }
    }

    private static bool AreEqual(object? left, object? right, StringComparison stringComparison)
        => left is string leftString && right is string rightString
            ? string.Equals(leftString, rightString, stringComparison)
            : CompareValues(left, right) == 0;

    private static int CompareValues(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        if (left is DateTime leftDate && right is DateTimeOffset rightOffset)
            return new DateTimeOffset(leftDate.ToUniversalTime()).CompareTo(rightOffset);
        if (left is DateTimeOffset leftOffset && right is DateTime rightDate)
            return leftOffset.CompareTo(new DateTimeOffset(rightDate.ToUniversalTime()));
        if (IsNumber(left) && IsNumber(right))
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        if (left is IComparable comparable)
        {
            try { return comparable.CompareTo(right); }
            catch (ArgumentException) { }
        }
        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumber(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
