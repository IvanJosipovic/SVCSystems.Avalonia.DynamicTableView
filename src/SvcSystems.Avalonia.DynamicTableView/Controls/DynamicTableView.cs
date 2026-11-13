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
    private readonly HashSet<DynamicTableViewColumn> _observedWidthColumns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DynamicTableViewColumn, NotifyCollectionChangedEventHandler> _observedClassHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DynamicTableViewColumn, HashSet<string>> _nativeDefinitionClasses = new(ReferenceEqualityComparer.Instance);
    private bool _applyingWidthMetadata;
    private bool _applyingNativeColumnValues;
    private IReadOnlyList<DynamicTableViewSortDescriptor> _headerSorts = [];
    private IReadOnlyList<DynamicTableViewFilterDescriptor> _headerFilters = [];
    private IDynamicTableViewSource? _attachedSource;
    private ScrollViewer? _scrollViewer;
    private bool _headerRefreshQueued;
    private bool _restoringState;
    private Vector? _pendingScrollOffset;
    private bool _applyingScrollRestore;
    private readonly ColumnReorderController _columnReorder = new();
    private readonly SelectionRangeDragController _selectionDrag = new();
    private readonly ResizeHeaderCache _resizeHeaders;
    private readonly ColumnResizeController _resize;
    private TableViewColumnHeader? _resizeHoverHeader;
    private TableViewColumnHeader? _filterButtonPointerHeader;

    /// <summary>Creates an empty dynamic table.</summary>
    public DynamicTableView()
    {
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

    /// <summary>Restores column order and widths, sort descriptors, filters, and scroll offset by stable key.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A saved width is invalid. The table is left unchanged.</exception>
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
            SetColumnWidthMetadata(definition, saved.Width);
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
        SynchronizeColumnSubscriptions();
        // Metadata can change while detached, when there is no attached table to notify.
        ApplyWidthMetadataToNativeColumns();
        LayoutUpdated += OnLayoutUpdated;
        QueueHeaderRefresh();
        QueueScrollRestore();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        ClearColumnSubscriptions();
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
            _nativeDefinitionClasses.Clear();
            SynchronizeColumnSubscriptions();
            return;
        }

        ItemsSource = source.Items;
        Selection = source.SelectionModel;
        source.Changed += SourceOnChanged;
        source.Columns.CollectionChanged += ColumnsOnCollectionChanged;
        SynchronizeColumnSubscriptions();
        RebuildNativeColumns();
    }

    /// <summary>Observes sizing metadata and style classes for the attached source.</summary>
    private void SynchronizeColumnSubscriptions()
    {
        foreach (DynamicTableViewColumn column in _observedWidthColumns.ToArray())
        {
            if (IsAttachedSourceColumn(column))
                continue;
            column.WidthOptionsChanged -= ColumnOnWidthOptionsChanged;
            _observedWidthColumns.Remove(column);
        }

        foreach ((DynamicTableViewColumn column, NotifyCollectionChangedEventHandler handler) in _observedClassHandlers.ToArray())
        {
            if (IsAttachedSourceColumn(column))
                continue;
            column.Classes.CollectionChanged -= handler;
            _observedClassHandlers.Remove(column);
        }

        if (Source is null)
            return;

        foreach (DynamicTableViewColumn column in Source.Columns)
        {
            if (!IsAttachedSourceColumn(column) || !_observedWidthColumns.Add(column))
                continue;
            column.WidthOptionsChanged += ColumnOnWidthOptionsChanged;
        }

        foreach (DynamicTableViewColumn column in Source.Columns)
        {
            if (!IsAttachedSourceColumn(column) || _observedClassHandlers.ContainsKey(column))
                continue;
            NotifyCollectionChangedEventHandler handler = (_, _) => SynchronizeNativeColumnClasses(column);
            _observedClassHandlers.Add(column, handler);
            column.Classes.CollectionChanged += handler;
        }
    }

    private bool IsAttachedSourceColumn(DynamicTableViewColumn column)
        => VisualRoot is not null && Source is { } source && source.Columns.Contains(column);

    private void ClearColumnSubscriptions()
    {
        foreach (DynamicTableViewColumn column in _observedWidthColumns)
            column.WidthOptionsChanged -= ColumnOnWidthOptionsChanged;
        _observedWidthColumns.Clear();
        foreach ((DynamicTableViewColumn column, NotifyCollectionChangedEventHandler handler) in _observedClassHandlers)
            column.Classes.CollectionChanged -= handler;
        _observedClassHandlers.Clear();
    }

    private void ColumnOnWidthOptionsChanged(object? sender, EventArgs e)
    {
        if (sender is not DynamicTableViewColumn definition || _applyingWidthMetadata)
            return;
        ApplyWidthMetadata(definition);
    }

    /// <summary>Re-applies every observed column's width metadata, including metadata changed while detached.</summary>
    private void ApplyWidthMetadataToNativeColumns()
    {
        if (Source is null)
            return;
        foreach (var definition in Source.Columns)
        {
            ApplyWidthMetadata(definition);
            SynchronizeNativeColumnClasses(definition);
        }
    }

    /// <summary>Applies one column's native width metadata and limits.</summary>
    private void ApplyWidthMetadata(DynamicTableViewColumn definition)
    {
        if (!_nativeColumns.TryGetValue(definition.Key, out TableViewColumn? native))
            return;

        _applyingNativeColumnValues = true;
        try
        {
            ApplyNativeColumnLimits(definition, native);
            if (native.Width != definition.Width)
                native.Width = definition.Width;
        }
        finally
        {
            _applyingNativeColumnValues = false;
        }
    }

    /// <summary>Sets metadata limits below application styles so native TableView owns the effective limits.</summary>
    private static void ApplyNativeColumnLimits(DynamicTableViewColumn definition, TableViewColumn native)
    {
        // Template priority is lower than Style priority. This supplies the metadata fallback while
        // allowing direct TableViewColumn MinWidth/MaxWidth setters to override it.
        native.SetValue(TableViewColumn.MinWidthProperty, definition.MinWidth, BindingPriority.Template);
        native.SetValue(TableViewColumn.MaxWidthProperty, definition.MaxWidth, BindingPriority.Template);
    }

    /// <summary>Applies width metadata without re-entering the change handler.</summary>
    private void SetColumnWidthMetadata(DynamicTableViewColumn definition, GridLength width)
    {
        _applyingWidthMetadata = true;
        try
        {
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
    }

    private void ColumnsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_restoringState)
            return;
        _resizeHeaders.Invalidate();

        if (Source is null || e.Action == NotifyCollectionChangedAction.Reset)
        {
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
                    if (column.Header is DynamicTableViewColumn removedDefinition)
                        _nativeDefinitionClasses.Remove(removedDefinition);
                    _nativeColumns.Remove(key);
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
        SynchronizeColumnSubscriptions();
    }

    private void RebuildNativeColumns()
    {
        foreach (TableViewColumn discarded in _nativeColumns.Values)
            discarded.PropertyChanged -= NativeColumnOnPropertyChanged;
        _nativeColumns.Clear();
        _nativeDefinitionClasses.Clear();
        Columns.Clear();
        // Rebuilding the native columns makes Avalonia recreate the column header visuals,
        // so the cached resize grips point at detached headers until the new ones are realized.
        _resizeHeaders.Invalidate();
        _resizeHeaders.Clear();
        SynchronizeColumnSubscriptions();
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
            Width = definition.Width,
            CellTemplate = new FuncDataTemplate<object>((_, _) => new DynamicTableViewCell(this, definition), supportsRecycling: true)
        };

        // Style classes are applied before the column attaches so native limit styles take effect.
        HashSet<string> appliedClasses = [];
        foreach (string styleClass in definition.Classes)
        {
            native.Classes.Add(styleClass);
            appliedClasses.Add(styleClass);
        }
        _nativeDefinitionClasses.Add(definition, appliedClasses);
        native.PropertyChanged += NativeColumnOnPropertyChanged;
        _applyingNativeColumnValues = true;
        try
        {
            ApplyNativeColumnLimits(definition, native);
            native.Width = definition.Width;
        }
        finally
        {
            _applyingNativeColumnValues = false;
        }
        return native;
    }

    /// <summary>Reapplies column sizing when a style or the caller changes a column's limits or width.</summary>
    private void NativeColumnOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_applyingNativeColumnValues || sender is not TableViewColumn { Header: DynamicTableViewColumn definition } native)
            return;

        if (e.Property == TableViewColumn.IsVisibleProperty && !native.IsVisible && !HasVisibleColumn())
        {
            // Keep the table usable when callers or the visibility context menu hide columns.
            _applyingNativeColumnValues = true;
            try
            {
                native.IsVisible = true;
            }
            finally
            {
                _applyingNativeColumnValues = false;
            }
            return;
        }

        if (e.Property == TableViewColumn.MinWidthProperty ||
            e.Property == TableViewColumn.MaxWidthProperty)
        {
            ApplyWidthMetadata(definition);
            return;
        }

        if (e.Property != TableViewColumn.WidthProperty)
            return;

        // Auto and Star metadata are allocation requests, not realized pixel widths. Keep them
        // authoritative if a native column is changed directly; the custom resize gesture updates
        // the metadata before setting a pixel width.
        if (definition.Width.GridUnitType is GridUnitType.Auto or GridUnitType.Star)
        {
            _applyingNativeColumnValues = true;
            try
            {
                native.Width = definition.Width;
            }
            finally
            {
                _applyingNativeColumnValues = false;
            }
        }
    }

    private bool HasVisibleColumn()
    {
        foreach (TableViewColumn column in Columns)
        {
            if (column.IsVisible)
                return true;
        }

        return false;
    }

    private void SynchronizeNativeColumnClasses(DynamicTableViewColumn definition)
    {
        if (!_nativeColumns.TryGetValue(definition.Key, out TableViewColumn? native) ||
            !_nativeDefinitionClasses.TryGetValue(definition, out HashSet<string>? appliedClasses))
            return;

        foreach (string appliedClass in appliedClasses.ToArray())
        {
            if (definition.Classes.Contains(appliedClass))
                continue;
            native.Classes.Remove(appliedClass);
            appliedClasses.Remove(appliedClass);
        }

        foreach (string styleClass in definition.Classes)
        {
            if (appliedClasses.Add(styleClass))
                native.Classes.Add(styleClass);
        }
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

    /// <summary>Resolves the minimum width, preferring a styled value over the column metadata.</summary>
    private static double GetEffectiveMinWidth(DynamicTableViewColumn definition, TableViewColumn? native)
        => ColumnWidthCalculator.GetEffectiveMinWidth(definition, native);

    private static double GetEffectiveMaxWidth(DynamicTableViewColumn definition, TableViewColumn? native)
        => ColumnWidthCalculator.GetEffectiveMaxWidth(definition, native);

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

    /// <summary>Resizes a column by <paramref name="deltaX"/>, clamped to its effective width limits.</summary>
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

        double minWidth = GetEffectiveMinWidth(definition, native);
        double maxWidth = GetEffectiveMaxWidth(definition, native);
        double currentWidth = native.Width.GridUnitType == GridUnitType.Pixel
            ? native.Width.Value
            : native.ActualWidth;
        if (native.Width.GridUnitType == GridUnitType.Pixel &&
            (currentWidth < minWidth || currentWidth > maxWidth) &&
            double.IsFinite(native.ActualWidth))
            currentWidth = native.ActualWidth;
        if (!double.IsFinite(currentWidth))
            currentWidth = minWidth;
        double newWidth = ColumnWidthCalculator.ClampWidth(
            currentWidth + deltaX,
            minWidth,
            maxWidth);
        GridLength pixelWidth = new(newWidth);
        SetColumnWidthMetadata(definition, pixelWidth);
        native.Width = pixelWidth;
    }

    internal TableViewColumn? GetNativeColumn(string key)
        => _nativeColumns.TryGetValue(key, out var column) ? column : null;

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        _resizeHeaders.RefreshIfDirty();
        TryRestorePendingScrollOffset();
    }

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
            _columnReorder.Reset();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var pressedVisual = this.GetVisualAt(e.GetPosition(this));
        if (ControlTreeHelpers.FindVisualAncestor<TableViewRow>(pressedVisual ?? e.Source) is { DataContext: not null } rowContainer)
            _selectionDrag.Begin(
                GetRowIndex(rowContainer),
                Selection,
                controlModifierPressed: (e.KeyModifiers & KeyModifiers.Control) != 0);

        var dynamicHeader = ControlTreeHelpers.FindVisualAncestor<DynamicTableViewHeader>(e.Source);
        var columnHeader = ControlTreeHelpers.FindVisualAncestor<TableViewColumnHeader>(e.Source);
        var definition = dynamicHeader?.Column ?? columnHeader?.Column?.Header as DynamicTableViewColumn;
        if (definition is null)
            return;
        if (dynamicHeader is not null && ControlTreeHelpers.IsWithinNamedControl(e.Source, "PART_FilterButton"))
        {
            _columnReorder.Reset();
            return;
        }

        var native = dynamicHeader?.NativeColumn ?? columnHeader?.Column;
        if (native is null)
            return;
        if (ControlTreeHelpers.IsWithinClass(e.Source, "dynamic-table-view-column-resize-grip"))
        {
            _columnReorder.Reset();
            return;
        }
        _columnReorder.Begin(Columns.IndexOf(native), e.GetPosition(this));
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
        if (_selectionDrag.IsActive &&
            ControlTreeHelpers.FindVisualAncestor<TableViewRow>(pointerVisual ?? e.Source) is { } rowContainer)
        {
            var targetRowIndex = GetRowIndex(rowContainer);
            if (_selectionDrag.TryUpdate(Selection, targetRowIndex))
                e.Handled = true;
        }

        if (!_columnReorder.HasPressedColumn)
            return;
        var current = e.GetPosition(this);
        if (!_columnReorder.TryStart(current))
            return;
        // Pointer capture can keep the routed source on the pressed header.
        // Use the visual under the pointer, including native header padding.
        var targetColumn = ControlTreeHelpers.FindVisualAncestor<DynamicTableViewHeader>(pointerVisual)?.NativeColumn ??
            ControlTreeHelpers.FindVisualAncestor<TableViewColumnHeader>(pointerVisual)?.Column;
        if (targetColumn is null || Source is null)
            return;
        var targetIndex = Columns.IndexOf(targetColumn);
        if (_columnReorder.TryMoveTo(targetIndex, Source.Columns))
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
        _columnReorder.Reset();
        _selectionDrag.Reset();
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

    private void HandleRightClick(PointerPressedEventArgs e)
    {
        TableViewColumnHeader? columnHeader = ControlTreeHelpers.FindVisualAncestor<TableViewColumnHeader>(e.Source);
        if (columnHeader?.Column?.Header is DynamicTableViewColumn)
        {
            ContextMenu menu = CreateColumnVisibilityContextMenu(columnHeader);
            columnHeader.ContextMenu = menu;
            menu.Open(columnHeader);
            e.Handled = true;
            return;
        }

        if (ControlTreeHelpers.FindVisualAncestor<TableViewRow>(e.Source) is not { DataContext: { } clicked } row)
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

    internal ContextMenu CreateColumnVisibilityContextMenu(TableViewColumnHeader placementTarget)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        ContextMenu menu = new()
        {
            PlacementTarget = placementTarget
        };

        if (Source is null)
            return menu;

        foreach (DynamicTableViewColumn definition in Source.Columns)
        {
            if (!_nativeColumns.TryGetValue(definition.Key, out TableViewColumn? native))
                continue;

            MenuItem item = new()
            {
                Header = definition.Header.ToString() ?? string.Empty,
                ToggleType = MenuItemToggleType.CheckBox
            };
            item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(TableViewColumn.IsVisible))
            {
                Source = native,
                Mode = BindingMode.TwoWay
            });
            menu.Items.Add(item);
        }

        return menu;
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

}
