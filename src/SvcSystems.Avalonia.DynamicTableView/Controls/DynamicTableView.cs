using System.Collections.Specialized;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Read-only dynamic table view with DynamicData-backed query and row virtualization.</summary>
public sealed partial class DynamicTableView : TableView
{
    private const string ResizePointerOverClass = "dynamic-table-view-resize-pointerover";
    private const string ColumnResizingClass = "dynamic-table-view-column-resizing";

    [GeneratedDirectProperty]
    public partial IDynamicTableViewSource? Source { get; set; }

    /// <summary>Gets or sets which row and column separators are drawn. Defaults to no separators.</summary>
    [GeneratedStyledProperty(DynamicTableViewGridLinesVisibility.None)]
    public partial DynamicTableViewGridLinesVisibility GridLinesVisibility { get; set; }

    /// <summary>Gets or sets how text wraps in cells using the default text template. Defaults to <see cref="TextWrapping.NoWrap"/>.</summary>
    [GeneratedStyledProperty(TextWrapping.NoWrap)]
    public partial TextWrapping CellTextWrapping { get; set; }

    /// <summary>Gets or sets how text is trimmed in cells using the default text template. Defaults to no trimming.</summary>
    [GeneratedStyledProperty]
    public partial TextTrimming? CellTextTrimming { get; set; }

    /// <summary>Gets or sets the maximum lines shown in cells using the default text template. Defaults to one line; zero means unlimited.</summary>
    [GeneratedStyledProperty(1, Validate = nameof(ValidateCellMaxLines))]
    public partial int CellMaxLines { get; set; }

    private static bool ValidateCellMaxLines(int value)
        => value >= 0;

    /// <summary>Gets or sets whether clicking headers can add secondary sort columns. Defaults to false.</summary>
    [GeneratedDirectProperty]
    public partial bool AllowMultipleSorts { get; set; }

    private readonly Dictionary<string, TableViewColumn> _nativeColumns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manuallySizedColumns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cellSamples = new(StringComparer.Ordinal);
    private readonly HashSet<DynamicTableViewColumn> _observedWidthColumns = new(ReferenceEqualityComparer.Instance);
    private bool _applyingWidthMetadata;
    private readonly TextMeasurer _textMeasurer = new();
    private IReadOnlyList<DynamicTableViewSortDescriptor> _headerSorts = [];
    private IReadOnlyList<DynamicTableViewFilterDescriptor> _headerFilters = [];
    private IDynamicTableViewSource? _attachedSource;
    private bool _autoWidthPassQueued;
    private bool _starWidthPassQueued;
    private bool _columnSizingInvalidated = true;
    private readonly Action _runStarWidthPass;
    private GridLength[] _starWidthBuffer = [];
    private (double Viewport, int Columns, int Revision) _lastStarWidthKey;
    private int _widthRevision;
    private bool _applyingStarWidths;
    private ScrollViewer? _scrollViewer;
    private bool _headerRefreshQueued;
    private bool _restoringState;
    private Vector? _pendingScrollOffset;
    private bool _applyingScrollRestore;
    private int _pointerDownColumn = -1;
    private Point _pointerDownPosition;
    private readonly ResizeHeaderCache _resizeHeaders;
    private readonly ColumnResizeController _resize;
    private TableViewColumnHeader? _resizeHoverHeader;
    private bool _headerDragStarted;
    private int _selectionDragAnchor = -1;
    private int _selectionDragLast = -1;
    private bool _selectionDragSelect;
    private HashSet<int>? _selectionDragInitialSelection;
    private TableViewColumnHeader? _filterButtonPointerHeader;

    /// <summary>Creates an empty dynamic table.</summary>
    public DynamicTableView()
    {
        _runStarWidthPass = FlushStarColumnWidths;
        _resizeHeaders = new ResizeHeaderCache(this, IsKnownColumnKey);
        _resize = new ColumnResizeController(
            () => CanUserResizeColumns && Source is not null,
            _resizeHeaders,
            ResolveResizeHoverKey,
            SetResizeHoverHeader);
        _resize.ResizeRequested += OnResizeRequested;
        _resize.IsResizingChanged += (_, isResizing) => Classes.Set(ColumnResizingClass, isResizing);
        Classes.Add("dynamic-table-view");
        UpdateGridLinesClass();
        AutoScrollToSelectedItem = false;
        SelectionMode = SelectionMode.Multiple;
        CanUserResizeColumns = true;
        AddHandler(InputElement.PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // PointerCaptureLost and PointerExited are Direct routed events, so a Bubble
        // subscription would never be invoked.
        AddHandler(InputElement.PointerCaptureLostEvent, OnGridPointerCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
        AddHandler(InputElement.PointerExitedEvent, OnGridPointerExited, RoutingStrategies.Direct, handledEventsToo: true);
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble);
    }

    /// <summary>Gets or sets a provider for context menu items.</summary>
    public Func<IReadOnlyList<object>, IEnumerable?>? ContextMenuItemsFactory { get; set; }

    /// <summary>Raised when user right-clicks a realized row.</summary>
    public event EventHandler<DynamicTableViewContextMenuEventArgs>? ContextMenuRequested;

    protected override Type StyleKeyOverride => typeof(TableView);

    /// <summary>Captures keyed column order and widths, source sorts and filters, and scroll offset.</summary>
    public DynamicTableViewState CaptureState()
    {
        var source = Source ?? throw new InvalidOperationException("DynamicTableView has no source.");
        IReadOnlyList<DynamicTableViewColumnState> columns = DynamicTableViewStateSerializer.CaptureColumns(
            source.Columns,
            key => _nativeColumns.TryGetValue(key, out TableViewColumn? native) ? native.ActualWidth : 0);
        var scrollOffset = _pendingScrollOffset ?? FindScrollViewer()?.Offset ?? default;
        return new(columns, source.SortDescriptors.ToArray(), source.FilterDescriptors.ToArray(), scrollOffset);
    }

    /// <summary>Restores column order, sizing modes and widths, sort descriptors, filters, and scroll offset by stable key.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A saved width is not finite or is negative. The table is left unchanged.</exception>
    public void RestoreState(DynamicTableViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var source = Source ?? throw new InvalidOperationException("DynamicTableView has no source.");
        // Validate before moving anything, so a rejected state cannot leave a partial restore.
        DynamicTableViewStateSerializer.Validate(state);
        IReadOnlyList<DynamicTableViewColumnState> ordered =
            DynamicTableViewStateSerializer.PlanColumnOrder(state, source.Columns);

        _restoringState = true;
        try
        {
            for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var currentIndex = IndexOfColumn(source.Columns, ordered[targetIndex].Key);
                if (currentIndex < 0 || currentIndex == targetIndex)
                    continue;
                source.Columns.Move(currentIndex, targetIndex);
            }
        }
        finally
        {
            _restoringState = false;
        }

        foreach (var saved in state.Columns)
        {
            var definition = FindDefinition(saved.Key);
            if (definition is null)
                continue;
            double restoredWidth = DynamicTableViewStateSerializer.ResolveRestoredWidth(
                saved,
                definition,
                _nativeColumns.TryGetValue(definition.Key, out TableViewColumn? restoredNative) ? restoredNative : null);
            SetColumnWidthMetadata(definition, saved.WidthMode, restoredWidth);
            if (saved.WidthMode == DynamicTableViewWidthMode.Pixel)
                _manuallySizedColumns.Add(saved.Key);
            else
                _manuallySizedColumns.Remove(saved.Key);
        }

        if (!source.SortDescriptors.SequenceEqual(state.Sorts))
            source.SetSort(state.Sorts.ToArray());

        if (!source.FilterDescriptors.SequenceEqual(state.Filters))
        {
            source.ClearFilters();
            foreach (var filter in state.Filters)
                source.SetFilter(filter);
        }
        _pendingScrollOffset = state.ScrollOffset;
        RebuildNativeColumns();
        QueueAutoWidthPass();
        QueueScrollRestore();
    }

    partial void OnPropertyChangedOverride(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SourceProperty)
            AttachSource(change.GetNewValue<IDynamicTableViewSource?>());
        else if (change.Property == TableView.CanUserResizeColumnsProperty)
            UpdateHeaderStates();
        else if (change.Property == GridLinesVisibilityProperty)
            UpdateGridLinesClass();
        else if (change.Property == CellTextWrappingProperty ||
                 change.Property == CellTextTrimmingProperty ||
                 change.Property == CellMaxLinesProperty)
            UpdateDefaultCellTextOptions();
        else if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            QueueHeaderRefresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _resizeHeaders.Invalidate();
        SynchronizeWidthOptionsSubscriptions();
        // Metadata can change while detached, when there is no attached table to notify.
        ApplyWidthMetadataToNativeColumns();
        LayoutUpdated += OnLayoutUpdated;
        InvalidateColumnSizing();
        QueueHeaderRefresh();
        QueueAutoWidthPass();
        QueueScrollRestore();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        ClearWidthOptionsSubscriptions();
        _resizeHeaders.Invalidate();
        _resizeHeaders.Clear();
        _scrollViewer = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _scrollViewer = null;
        base.OnApplyTemplate(e);
        _resizeHeaders.Invalidate();
    }

    private void AttachSource(IDynamicTableViewSource? source)
    {
        _resizeHeaders.Invalidate();
        if (_attachedSource is not null)
        {
            _attachedSource.Changed -= SourceOnChanged;
            _attachedSource.Columns.CollectionChanged -= ColumnsOnCollectionChanged;
        }

        _attachedSource = source;
        _manuallySizedColumns.Clear();
        _headerSorts = [];
        _headerFilters = [];
        if (source is null)
        {
            ItemsSource = null;
            Selection = null;
            Columns.Clear();
            foreach (TableViewColumn discarded in _nativeColumns.Values)
                discarded.PropertyChanged -= NativeColumnOnPropertyChanged;
            _nativeColumns.Clear();
            SynchronizeWidthOptionsSubscriptions();
            return;
        }

        ItemsSource = source.Items;
        Selection = source.SelectionModel;
        source.Changed += SourceOnChanged;
        source.Columns.CollectionChanged += ColumnsOnCollectionChanged;
        SynchronizeWidthOptionsSubscriptions();
        RebuildNativeColumns();
    }

    /// <summary>Observes column sizing metadata for the attached source, so attached tables follow runtime changes.</summary>
    private void SynchronizeWidthOptionsSubscriptions()
    {
        foreach (DynamicTableViewColumn column in _observedWidthColumns.ToArray())
        {
            if (IsWidthObservedColumn(column))
                continue;
            column.WidthOptionsChanged -= ColumnOnWidthOptionsChanged;
            _observedWidthColumns.Remove(column);
        }

        if (Source is null)
            return;

        foreach (DynamicTableViewColumn column in Source.Columns)
        {
            if (!IsWidthObservedColumn(column) || !_observedWidthColumns.Add(column))
                continue;
            column.WidthOptionsChanged += ColumnOnWidthOptionsChanged;
        }
    }

    private bool IsWidthObservedColumn(DynamicTableViewColumn column)
        => VisualRoot is not null && Source is { } source && source.Columns.Contains(column);

    private void ClearWidthOptionsSubscriptions()
    {
        foreach (DynamicTableViewColumn column in _observedWidthColumns)
            column.WidthOptionsChanged -= ColumnOnWidthOptionsChanged;
        _observedWidthColumns.Clear();
    }

    private void ColumnOnWidthOptionsChanged(object? sender, EventArgs e)
    {
        if (sender is not DynamicTableViewColumn definition || _applyingWidthMetadata)
            return;
        ApplyWidthMetadata(definition);
        InvalidateColumnSizing();
        FlushColumnSizing();
        QueueAutoWidthPass();
    }

    /// <summary>Re-applies every observed column's width metadata, including metadata changed while detached.</summary>
    private void ApplyWidthMetadataToNativeColumns()
    {
        if (Source is null)
            return;
        foreach (var definition in Source.Columns)
            ApplyWidthMetadata(definition);
    }

    /// <summary>Applies one column's width metadata to its native column, overriding any captured manual width.</summary>
    private void ApplyWidthMetadata(DynamicTableViewColumn definition)
    {
        // An explicit metadata change supersedes any width captured by a resize gesture.
        _manuallySizedColumns.Remove(definition.Key);
        _widthRevision++;
        if (!_nativeColumns.TryGetValue(definition.Key, out TableViewColumn? native))
            return;
        GridLength width = GetInitialWidth(definition, GetEffectiveMinWidth(definition, native));
        if (native.Width != width)
            native.Width = width;
    }

    /// <summary>Applies width metadata without re-entering the change handler.</summary>
    private void SetColumnWidthMetadata(DynamicTableViewColumn definition, DynamicTableViewWidthMode widthMode, double width)
    {
        _applyingWidthMetadata = true;
        try
        {
            definition.WidthMode = widthMode;
            definition.Width = width;
        }
        finally
        {
            _applyingWidthMetadata = false;
        }
    }

    private void UpdateGridLinesClass()
    {
        Classes.Remove("grid-lines-horizontal");
        Classes.Remove("grid-lines-vertical");
        Classes.Remove("grid-lines-all");

        var className = GridLinesVisibility switch
        {
            DynamicTableViewGridLinesVisibility.Horizontal => "grid-lines-horizontal",
            DynamicTableViewGridLinesVisibility.Vertical => "grid-lines-vertical",
            DynamicTableViewGridLinesVisibility.All => "grid-lines-all",
            _ => null
        };

        if (className is not null)
            Classes.Add(className);
    }

    private void UpdateDefaultCellTextOptions()
    {
        foreach (var cell in this.GetVisualDescendants().OfType<DynamicTableViewCell>())
            cell.UpdateDefaultTextOptions();
    }

    private void SourceOnChanged(object? sender, EventArgs e)
    {
        if (Source is not { } source)
            return;
        if (!source.SortDescriptors.SequenceEqual(_headerSorts) ||
            !source.FilterDescriptors.SequenceEqual(_headerFilters))
            UpdateHeaderStates();
        QueueAutoWidthPass();
    }

    private void ColumnsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_restoringState)
            return;
        _resizeHeaders.Invalidate();
        _widthRevision++;

        if (Source is null || e.Action == NotifyCollectionChangedAction.Reset)
        {
            _manuallySizedColumns.Clear();
            RebuildNativeColumns();
        }
        else if (e.Action == NotifyCollectionChangedAction.Move)
        {
            Columns.Move(e.OldStartingIndex, e.NewStartingIndex);
        }
        else
        {
            if (e.OldItems is not null)
            {
                for (var i = 0; i < e.OldItems.Count; i++)
                {
                    var index = e.OldStartingIndex;
                    var column = Columns[index];
                    Columns.RemoveAt(index);
                    string key = ((DynamicTableViewColumn)column.Header!).Key;
                    if (column is TableViewColumn removed)
                        removed.PropertyChanged -= NativeColumnOnPropertyChanged;
                    _nativeColumns.Remove(key);
                    _manuallySizedColumns.Remove(key);
                }
            }
            if (e.NewItems is not null)
            {
                for (var i = 0; i < e.NewItems.Count; i++)
                {
                    var definition = (DynamicTableViewColumn)e.NewItems[i]!;
                    var native = CreateNativeColumn(definition);
                    _nativeColumns.Add(definition.Key, native);
                    Columns.Insert(e.NewStartingIndex + i, native);
                }
            }
            UpdateHeaderStates();
        }
        _resizeHeaders.Invalidate();
        SynchronizeWidthOptionsSubscriptions();
        InvalidateColumnSizing();
        QueueAutoWidthPass();
    }

    private void RebuildNativeColumns()
    {
        _widthRevision++;
        foreach (TableViewColumn discarded in _nativeColumns.Values)
            discarded.PropertyChanged -= NativeColumnOnPropertyChanged;
        _nativeColumns.Clear();
        Columns.Clear();
        // Rebuilding the native columns makes Avalonia recreate the column header visuals,
        // so the cached resize grips point at detached headers until the new ones are realized.
        _resizeHeaders.Invalidate();
        _resizeHeaders.Clear();
        SynchronizeWidthOptionsSubscriptions();
        if (Source is null)
            return;

        foreach (var definition in Source.Columns)
        {
            var native = CreateNativeColumn(definition);
            _nativeColumns.Add(definition.Key, native);
            Columns.Add(native);
        }

        UpdateHeaderStates();
    }

    private TableViewColumn CreateNativeColumn(DynamicTableViewColumn definition)
    {
        var native = new TableViewColumn
        {
            Header = definition,
            HeaderTemplate = CreateHeaderTemplate(),
            CanUserResize = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Width = ColumnWidthCalculator.GetInitialWidth(definition, definition.MinWidth),
            CellTemplate = new FuncDataTemplate<object>((_, _) => new DynamicTableViewCell(this, definition), supportsRecycling: true)
        };

        // Style classes are applied before the column attaches, so styles that target them
        // (for example to set DynamicTableViewColumnProperties.MinWidth) take effect.
        foreach (var styleClass in definition.Classes)
            native.Classes.Add(styleClass);
        // A style can set the minimum after the initial width was applied, so follow the property.
        native.PropertyChanged += NativeColumnOnPropertyChanged;
        return native;
    }

    /// <summary>Reapplies column sizing when a style or the caller changes a column's width or minimum.</summary>
    private void NativeColumnOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not TableViewColumn { Header: DynamicTableViewColumn definition })
            return;

        if (e.Property == DynamicTableViewColumnProperties.MinWidthProperty)
        {
            // A styled minimum changes the star result directly, so apply it before the next frame.
            ApplyWidthMetadata(definition);
            InvalidateColumnSizing();
            FlushColumnSizing();
            QueueAutoWidthPass();
            return;
        }

        if (e.Property != TableViewColumn.WidthProperty)
            return;

        // A fixed column's realized width is an input to the star budget. Only an external write
        // matters; the star pass's own writes are already reflected in the cached key, so ignoring
        // them here keeps the pass from re-queueing itself every frame.
        if (_applyingStarWidths)
            return;

        InvalidateColumnSizing();
    }

    private IDataTemplate CreateHeaderTemplate()
        => new FuncDataTemplate<DynamicTableViewColumn>(
            (definition, _) => definition is null ? null : new DynamicTableViewHeader(this, definition));

    private void QueueHeaderRefresh()
    {
        if (_headerRefreshQueued || Source is null)
            return;
        _resizeHeaders.Invalidate();
        _headerRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _headerRefreshQueued = false;
            if (VisualRoot is null || Source is null || !IsVisible)
                return;
            foreach (var definition in Source.Columns)
            {
                if (_nativeColumns.TryGetValue(definition.Key, out var native))
                    native.HeaderTemplate = CreateHeaderTemplate();
            }
            UpdateHeaderStates();
            _resizeHeaders.Invalidate();
        }, DispatcherPriority.Loaded);
    }

    private static GridLength GetInitialWidth(DynamicTableViewColumn definition, double minWidth)
        => ColumnWidthCalculator.GetInitialWidth(definition, minWidth);

    /// <summary>Resolves the minimum width, preferring a styled value over the column metadata.</summary>
    private static double GetEffectiveMinWidth(DynamicTableViewColumn definition, TableViewColumn? native)
        => ColumnWidthCalculator.GetEffectiveMinWidth(definition, native);

    private void UpdateHeaderStates()
    {
        if (Source is null)
            return;
        var sorts = Source.SortDescriptors;
        var filters = Source.FilterDescriptors;
        _headerSorts = sorts;
        _headerFilters = filters;
        foreach (var header in this.GetVisualDescendants().OfType<DynamicTableViewHeader>())
        {
            int columnIndex = IndexOfColumn(Source.Columns, header.Column.Key);
            header.UpdateState(
                sorts,
                filters,
                CanUserResizeColumns && columnIndex >= 0);
        }
        _resizeHeaders.RefreshIfDirty();
    }

    internal void UpdateHeaderState(DynamicTableViewHeader header)
    {
        if (Source is not null)
        {
            int columnIndex = IndexOfColumn(Source.Columns, header.Column.Key);
            header.UpdateState(
                Source.SortDescriptors,
                Source.FilterDescriptors,
                CanUserResizeColumns && columnIndex >= 0);
        }
    }

    /// <summary>Invalidates the cached resize targets when a header template is realized.</summary>
    internal void NotifyHeaderTemplateApplied()
        => _resizeHeaders.Invalidate();

    /// <summary>Reports whether <paramref name="key"/> still belongs to the attached source.</summary>
    private bool IsKnownColumnKey(string key)
        => Source is { } source && IndexOfColumn(source.Columns, key) >= 0;

    /// <summary>Resizes a column by <paramref name="deltaX"/>, clamped to its effective minimum width.</summary>
    /// <remarks>This is the same operation a divider drag performs.</remarks>
    internal void ResizeColumnAtDivider(string headerKey, double deltaX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerKey);
        if (Source is null || !CanUserResizeColumns || deltaX == 0)
            return;

        int resizeIndex = IndexOfColumn(Source.Columns, headerKey);
        if (resizeIndex < 0)
            return;

        DynamicTableViewColumn definition = Source.Columns[resizeIndex];
        if (!_nativeColumns.TryGetValue(definition.Key, out TableViewColumn? native))
            return;

        double currentWidth = native.Width.GridUnitType == GridUnitType.Pixel
            ? native.Width.Value
            : native.ActualWidth;
        double newWidth = Math.Max(GetEffectiveMinWidth(definition, native), currentWidth + deltaX);
        _manuallySizedColumns.Add(definition.Key);
        SetColumnWidthMetadata(definition, DynamicTableViewWidthMode.Pixel, newWidth);
        _widthRevision++;
        native.Width = new GridLength(newWidth);
    }

    internal TableViewColumn? GetNativeColumn(string key)
        => _nativeColumns.TryGetValue(key, out var column) ? column : null;

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        FlushColumnSizing();
        _resizeHeaders.RefreshIfDirty();
        TryRestorePendingScrollOffset();
        QueueAutoWidthPass();
    }

    /// <summary>Marks the star layout as needing a recompute on the next layout pass.</summary>
    /// <remarks>
    /// Every mutation that can change a star width or a minimum must call this. The pass itself is
    /// coalesced and skipped when its inputs are unchanged, so a scroll frame costs one comparison
    /// instead of a full redistribution.
    /// </remarks>
    private void InvalidateColumnSizing()
    {
        _columnSizingInvalidated = true;
        QueueColumnSizing();
    }

    /// <summary>Queues one coalesced star-width pass for the current frame.</summary>
    private void QueueColumnSizing()
    {
        if (_starWidthPassQueued || _columnSizingInvalidated is false || Source is null || VisualRoot is null)
            return;

        _starWidthPassQueued = true;
        Dispatcher.UIThread.Post(_runStarWidthPass, DispatcherPriority.Render);
    }

    /// <summary>
    /// Recomputes every star column width and applies the result in a single write pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is computed completely before any width is written. Writing as each column is
    /// resolved would let a column be arranged below its minimum for one frame and then snap back
    /// to the minimum, which the user sees as a flash.
    /// </para>
    /// <para>
    /// The key comparison happens on every layout pass, because the viewport and the realized
    /// width of fixed columns are inputs that change without any invalidation call. Only the
    /// redistribution and the writes are skipped, which is the expensive part.
    /// </para>
    /// </remarks>
    private void FlushStarColumnWidths()
    {
        _starWidthPassQueued = false;
        if (Source is null || VisualRoot is null)
            return;

        double availableWidth = FindScrollViewer()?.Viewport.Width ?? Bounds.Width;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            return;

        // The star result depends on the budget left after fixed columns take their realized
        // width, the column composition, and the width metadata revision. Scrolling changes
        // none of them, so a scroll frame only pays for this comparison.
        double starBudget = StarColumnWidths.ComputeStarBudget(availableWidth, Columns);
        var key = (starBudget, Columns.Count, _widthRevision);
        if (key == _lastStarWidthKey)
        {
            _columnSizingInvalidated = false;
            return;
        }

        if (_starWidthBuffer.Length < Columns.Count)
            _starWidthBuffer = new GridLength[Columns.Count];

        StarColumnWidths.ComputeWidths(starBudget, Columns, _starWidthBuffer);

        var changed = false;
        _applyingStarWidths = true;
        try
        {
            for (var index = 0; index < Columns.Count; index++)
            {
                TableViewColumn native = Columns[index];
                if (!native.IsVisible || native.Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star })
                    continue;

                GridLength width = _starWidthBuffer[index];
                if (native.Width == width)
                    continue;

                native.Width = width;
                changed = true;
            }
        }
        finally
        {
            _applyingStarWidths = false;
        }

        _lastStarWidthKey = key;
        _columnSizingInvalidated = false;
        if (changed)
        {
            // The writes above invalidate layout, so the remaining passes run against the
            // widths that were just applied rather than the stale ones.
            _resizeHeaders.Invalidate();
        }
    }

    /// <summary>Runs the pending star pass immediately instead of waiting for the next layout pass.</summary>
    private void FlushColumnSizing() => FlushStarColumnWidths();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.X != 0)
            _resizeHeaders.Invalidate();
        if (!_applyingScrollRestore && e.OffsetDelta != default)
            _pendingScrollOffset = null;
    }

    private void QueueScrollRestore()
    {
        if (_pendingScrollOffset is null || VisualRoot is null)
            return;
        Dispatcher.UIThread.Post(TryRestorePendingScrollOffset, DispatcherPriority.Loaded);
    }

    private void TryRestorePendingScrollOffset()
    {
        if (_pendingScrollOffset is not { } requested || FindScrollViewer() is not { } scrollViewer)
            return;

        Vector maximum = new(
            Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width),
            Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
        Vector restored = new(
            Math.Clamp(requested.X, 0, maximum.X),
            Math.Clamp(requested.Y, 0, maximum.Y));
        _applyingScrollRestore = true;
        try
        {
            scrollViewer.Offset = restored;
        }
        finally
        {
            _applyingScrollRestore = false;
        }

        if (restored == requested)
            _pendingScrollOffset = null;
    }

    private ScrollViewer? FindScrollViewer()
        => _scrollViewer ??= this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    internal void NotifyColumnHeaderChanged() => QueueAutoWidthPass();

    private void QueueAutoWidthPass()
    {
        if (_autoWidthPassQueued || Source is null || VisualRoot is null)
            return;
        var needsAutoWidth = false;
        foreach (var column in Source.Columns)
        {
            if (column.WidthMode is DynamicTableViewWidthMode.Auto or DynamicTableViewWidthMode.Cells or DynamicTableViewWidthMode.Header &&
                !_manuallySizedColumns.Contains(column.Key))
            {
                needsAutoWidth = true;
                break;
            }
        }
        if (!needsAutoWidth)
            return;
        _autoWidthPassQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _autoWidthPassQueued = false;
            UpdateAutoWidths();
        }, DispatcherPriority.Background);
    }

    private void UpdateAutoWidths()
    {
        if (Source is null || VisualRoot is null)
            return;

        // Only the widest realized cell in a column can raise its width, so the running maximum
        // for the pass is the only state needed. It is rebuilt per pass because the realized cell
        // set changes as rows scroll.
        _cellSamples.Clear();

        foreach (var cell in this.GetVisualDescendants().OfType<TableViewCell>())
        {
            if (cell.Column?.Header is not DynamicTableViewColumn definition || cell.DataContext is not { } row)
                continue;
            if (definition.WidthMode is not (DynamicTableViewWidthMode.Auto or DynamicTableViewWidthMode.Cells) ||
                _manuallySizedColumns.Contains(definition.Key))
                continue;

            // A cell that already fits the column cannot raise it, so measuring it is wasted work.
            // The text measurer lays the string out, which is the dominant cost of this pass, and
            // this skips it for every cell narrower than the widest one already measured.
            double available = cell.Bounds.Width - ColumnWidthCalculator.MeasuredTextPadding;
            if (available > 0 && _cellSamples.TryGetValue(definition.Key, out var known) && known <= available)
                continue;

            var measured = _textMeasurer.Measure(definition.GetDisplayValue(row)) + ColumnWidthCalculator.MeasuredTextPadding;
            if (!_cellSamples.TryGetValue(definition.Key, out var current) || measured > current)
                _cellSamples[definition.Key] = measured;
        }

        foreach (var definition in Source.Columns)
        {
            if (definition.WidthMode is DynamicTableViewWidthMode.Pixel or DynamicTableViewWidthMode.Star ||
                _manuallySizedColumns.Contains(definition.Key) || !_nativeColumns.TryGetValue(definition.Key, out var native))
                continue;

            var desired = definition.WidthMode == DynamicTableViewWidthMode.Cells
                ? GetEffectiveMinWidth(definition, native)
                : _textMeasurer.Measure(definition.Header.ToString()) + ColumnWidthCalculator.MeasuredTextPadding;
            if ((definition.WidthMode is DynamicTableViewWidthMode.Auto or DynamicTableViewWidthMode.Cells) && _cellSamples.TryGetValue(definition.Key, out var cells))
                desired = Math.Max(desired, cells);
            desired = Math.Max(GetEffectiveMinWidth(definition, native), desired);
            var currentWidth = native.ActualWidth;
            // Header mode tracks the header text, so it must be able to shrink when the label gets
            // shorter. Auto and Cells only ever grow, so the widest realized row is not lost as
            // rows scroll out of view.
            bool shouldApply = definition.WidthMode == DynamicTableViewWidthMode.Header
                ? !double.IsFinite(currentWidth) || Math.Abs(desired - currentWidth) > 1
                : !double.IsFinite(currentWidth) || desired > currentWidth + 1;
            if (shouldApply)
                native.Width = new GridLength(desired);
        }
    }

    private void OnResizeRequested(object? sender, ColumnResizeRequestedEventArgs e)
        => ResizeColumnAtDivider(e.ColumnKey, e.DeltaX);

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            HandleRightClick(e);
            return;
        }

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        Point pressPosition = e.GetPosition(this);
        if (_resize.TryBegin(pressPosition, e.Pointer, out _))
        {
            _pointerDownColumn = -1;
            _headerDragStarted = false;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var pressedVisual = this.GetVisualAt(e.GetPosition(this));
        if (FindRow(pressedVisual ?? e.Source) is { DataContext: not null } rowContainer)
        {
            _selectionDragAnchor = GetRowIndex(rowContainer);
            if (_selectionDragAnchor >= 0 && Selection is { } selection)
            {
                _selectionDragLast = _selectionDragAnchor;
                _selectionDragInitialSelection = [.. selection.SelectedIndexes];
                _selectionDragSelect = (e.KeyModifiers & KeyModifiers.Control) == 0 ||
                    !selection.IsSelected(_selectionDragAnchor);
            }
        }

        var dynamicHeader = FindHeader(e.Source);
        var columnHeader = FindColumnHeader(e.Source);
        var definition = dynamicHeader?.Column ?? columnHeader?.Column?.Header as DynamicTableViewColumn;
        if (definition is null)
            return;
        if (dynamicHeader is not null && IsWithinNamedControl(e.Source, "PART_FilterButton"))
        {
            _pointerDownColumn = -1;
            return;
        }

        var native = dynamicHeader?.NativeColumn ?? columnHeader?.Column;
        if (native is null)
            return;
        if (IsWithinClass(e.Source, "dynamic-table-view-column-resize-grip"))
        {
            _pointerDownColumn = -1;
            return;
        }
        _pointerDownColumn = Columns.IndexOf(native);
        _pointerDownPosition = e.GetPosition(this);
        _headerDragStarted = false;
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateFilterButtonPointerHeader(e.Source);

        if (_resize.TryMove(e.Pointer, e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed is false)
        {
            _resize.UpdateHover(e.GetPosition(this));
            return;
        }

        var pointerVisual = this.GetVisualAt(e.GetPosition(this));
        if (_selectionDragAnchor >= 0 && FindRow(pointerVisual ?? e.Source) is { } rowContainer)
        {
            var targetRowIndex = GetRowIndex(rowContainer);
            if (targetRowIndex >= 0 && targetRowIndex != _selectionDragLast)
            {
                UpdateRowSelectionDrag(targetRowIndex);
                e.Handled = true;
            }
        }

        if (_pointerDownColumn < 0)
            return;
        var current = e.GetPosition(this);
        if (!_headerDragStarted && Math.Abs(current.X - _pointerDownPosition.X) < 5)
            return;
        _headerDragStarted = true;
        // Pointer capture can keep the routed source on the pressed header.
        // Use the visual under the pointer, including native header padding.
        var targetColumn = FindHeader(pointerVisual)?.NativeColumn ?? FindColumnHeader(pointerVisual)?.Column;
        if (targetColumn is null || Source is null)
            return;
        var targetIndex = Columns.IndexOf(targetColumn);
        if (targetIndex < 0 || targetIndex == _pointerDownColumn)
            return;
        Source.Columns.Move(_pointerDownColumn, targetIndex);
        _pointerDownColumn = targetIndex;
        e.Handled = true;
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resize.TryEnd(e.Pointer))
        {
            e.Pointer.Capture(null);
            e.Handled = true;
            _resize.UpdateHover(e.GetPosition(this));
        }
        _pointerDownColumn = -1;
        _headerDragStarted = false;
        _selectionDragAnchor = -1;
        _selectionDragLast = -1;
        _selectionDragInitialSelection = null;
    }

    private void OnGridPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => _resize.Cancel();

    private void OnGridPointerExited(object? sender, PointerEventArgs e)
        => _resize.ClearHoverWhenIdle();

    /// <summary>Maps a divider hit to the header that should show the resize hover highlight.</summary>
    private string? ResolveResizeHoverKey(string columnKey, bool isRightHalf)
    {
        if (Source is null)
            return null;
        if (!isRightHalf)
            return columnKey;

        int columnIndex = IndexOfColumn(Source.Columns, columnKey);
        return columnIndex >= 0 && columnIndex + 1 < Source.Columns.Count
            ? Source.Columns[columnIndex + 1].Key
            : columnKey;
    }

    private void SetResizeHoverHeader(TableViewColumnHeader? header)
    {
        // ColumnResizeController already suppresses repeat calls for the same header,
        // so this only has to move the class off the previous header and onto the next.
        if (_resizeHoverHeader is not null && !ReferenceEquals(_resizeHoverHeader, header))
            _resizeHoverHeader.Classes.Set(ResizePointerOverClass, false);

        _resizeHoverHeader = header;
        header?.Classes.Set(ResizePointerOverClass, true);
    }

    private int GetRowIndex(TableViewRow row) => IndexFromContainer(row);

    private void UpdateRowSelectionDrag(int targetIndex)
    {
        if (Selection is not { } selection || _selectionDragInitialSelection is not { } initialSelection)
            return;

        if (selection.SingleSelect)
        {
            selection.Select(targetIndex);
            _selectionDragLast = targetIndex;
            return;
        }

        var anchor = _selectionDragAnchor;
        var previousStart = Math.Min(anchor, _selectionDragLast);
        var previousEnd = Math.Max(anchor, _selectionDragLast);
        var targetStart = Math.Min(anchor, targetIndex);
        var targetEnd = Math.Max(anchor, targetIndex);

        selection.BeginBatchUpdate();
        try
        {
            SetSelectionRange(selection, previousStart, Math.Min(previousEnd, targetStart - 1), initialSelection, applyDragState: false);
            SetSelectionRange(selection, Math.Max(previousStart, targetEnd + 1), previousEnd, initialSelection, applyDragState: false);
            SetSelectionRange(selection, targetStart, targetEnd, initialSelection, applyDragState: true);
            _selectionDragLast = targetIndex;
        }
        finally
        {
            selection.EndBatchUpdate();
        }
    }

    private void SetSelectionRange(
        ISelectionModel selection,
        int start,
        int end,
        HashSet<int> initialSelection,
        bool applyDragState)
    {
        for (var index = start; index <= end; index++)
        {
            var shouldBeSelected = applyDragState
                ? _selectionDragSelect
                : initialSelection.Contains(index);
            if (selection.IsSelected(index) == shouldBeSelected)
                continue;
            if (shouldBeSelected)
                selection.Select(index);
            else
                selection.Deselect(index);
        }
    }

    private void HandleRightClick(PointerPressedEventArgs e)
    {
        if (FindRow(e.Source) is not { DataContext: { } clicked } row)
            return;
        IReadOnlyList<object> targets;
        var rowIsSelected = Selection?.SelectedItems.Any(item =>
            Source?.AreSameRows(clicked, item) == true) == true;
        if (rowIsSelected)
            targets = Selection.SelectedItems.Where(static item => item is not null).Cast<object>().ToArray();
        else
            targets = [clicked];
        ContextMenuRequested?.Invoke(this, new(clicked, targets));
        if (ContextMenu is ContextMenu contextMenu && ContextMenuItemsFactory is not null)
            contextMenu.ItemsSource = ContextMenuItemsFactory(targets);
    }

    internal void SortColumn(string key)
    {
        if (Source is null)
            return;
        var previous = Source.SortDescriptors;
        var currentIndex = -1;
        for (var i = 0; i < previous.Count; i++)
        {
            if (string.Equals(previous[i].ColumnKey, key, StringComparison.Ordinal))
            {
                currentIndex = i;
                break;
            }
        }
        var current = currentIndex < 0 ? null : previous[currentIndex];
        if (!AllowMultipleSorts)
        {
            if (current is null)
                Source.SetSort([new(key, ListSortDirection.Ascending)]);
            else if (current.Direction == ListSortDirection.Ascending)
                Source.SetSort([current with { Direction = ListSortDirection.Descending }]);
            else
                Source.SetSort([]);
            return;
        }

        var sorts = new DynamicTableViewSortDescriptor[previous.Count + (current is null ? 1 : current.Direction == ListSortDirection.Descending ? -1 : 0)];
        var targetIndex = 0;
        for (var i = 0; i < previous.Count; i++)
        {
            if (i == currentIndex && current?.Direction == ListSortDirection.Descending)
                continue;
            sorts[targetIndex++] = i == currentIndex
                ? previous[i] with { Direction = ListSortDirection.Descending }
                : previous[i];
        }
        if (current is null)
            sorts[targetIndex] = new(key, ListSortDirection.Ascending);
        Source.SetSort(sorts);
    }

    internal FlyoutBase MakeFilterFlyout(DynamicTableViewColumn definition)
    {
        var flyout = new Flyout();
        flyout.Opening += (_, _) =>
        {
            if (flyout.Content is not null)
                return;
            var source = Source ?? throw new InvalidOperationException("DynamicTableView has no source.");
            var factory = definition.GetFilterFlyoutFactory();
            flyout.Content = factory is null
                ? new DynamicTableViewFilterFlyout(definition, source)
                : factory(new DynamicTableViewFilterContext(definition, source));
        };
        return flyout;
    }

    private DynamicTableViewColumn? FindDefinition(string key)
        => Source?.Columns.FirstOrDefault(column => string.Equals(column.Key, key, StringComparison.Ordinal));

    private static int IndexOfColumn(IList<DynamicTableViewColumn> columns, string key)
    {
        for (var i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Key, key, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static DynamicTableViewHeader? FindHeader(object? source)
    {
        if (source is not Visual visual)
            return null;

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is DynamicTableViewHeader header)
                return header;
        }

        return null;
    }

    private static TableViewColumnHeader? FindColumnHeader(object? source)
        => source is Visual visual
            ? visual.GetSelfAndVisualAncestors().OfType<TableViewColumnHeader>().FirstOrDefault()
            : null;

    private static bool IsWithinNamedControl(object? source, string name)
        => source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Name == name);

    private static bool IsWithinClass(object? source, string className)
        => source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Control>()
            .Any(control => control.Classes.Contains(className));

    private void UpdateFilterButtonPointerHeader(object? source)
    {
        TableViewColumnHeader? columnHeader = null;
        var isOverFilterButton = false;
        if (source is Visual visual)
        {
            foreach (var ancestor in visual.GetSelfAndVisualAncestors())
            {
                if (ancestor is Control control && control.Name == "PART_FilterButton")
                    isOverFilterButton = true;
                if (ancestor is TableViewColumnHeader header)
                {
                    columnHeader = header;
                    break;
                }
            }
        }

        if (_filterButtonPointerHeader is not null &&
            (!isOverFilterButton || !ReferenceEquals(_filterButtonPointerHeader, columnHeader)))
        {
            _filterButtonPointerHeader.Classes.Remove("filter-button-pointerover");
            _filterButtonPointerHeader = null;
        }

        if (isOverFilterButton && columnHeader is not null)
        {
            columnHeader.Classes.Add("filter-button-pointerover");
            _filterButtonPointerHeader = columnHeader;
        }
    }

    private static TableViewRow? FindRow(object? source)
        => source is Visual visual ? visual.GetSelfAndVisualAncestors().OfType<TableViewRow>().FirstOrDefault() : null;

}
