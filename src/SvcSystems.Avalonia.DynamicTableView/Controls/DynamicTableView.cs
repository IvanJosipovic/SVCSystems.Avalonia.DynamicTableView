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
    private readonly TextBlock _textMeasurer = new();
    private IReadOnlyList<DynamicTableViewSortDescriptor> _headerSorts = [];
    private IReadOnlyList<DynamicTableViewFilterDescriptor> _headerFilters = [];
    private IDynamicTableViewSource? _attachedSource;
    private bool _autoWidthPassQueued;
    private bool _starWidthUpdateQueued;
    private readonly Action _updateStarWidths;
    private ScrollViewer? _scrollViewer;
    private bool _headerRefreshQueued;
    private bool _restoringState;
    private Vector? _pendingScrollOffset;
    private bool _applyingScrollRestore;
    private int _pointerDownColumn = -1;
    private Point _pointerDownPosition;
    private IPointer? _resizePointer;
    private string? _resizeColumnKey;
    private double _resizeLastX;
    private TableViewColumnHeader? _resizeHoverHeader;
    private readonly List<(TableViewColumnHeader Header, Thumb Grip)> _resizeHeaders = [];
    private bool _resizeHeadersDirty = true;
    private bool _resizeHeadersStale;
    private bool _headerDragStarted;
    private int _selectionDragAnchor = -1;
    private int _selectionDragLast = -1;
    private bool _selectionDragSelect;
    private HashSet<int>? _selectionDragInitialSelection;
    private TableViewColumnHeader? _filterButtonPointerHeader;

    /// <summary>Creates an empty dynamic table.</summary>
    public DynamicTableView()
    {
        _updateStarWidths = UpdateStarColumnMinimumWidths;
        Classes.Add("dynamic-table-view");
        UpdateGridLinesClass();
        AutoScrollToSelectedItem = false;
        SelectionMode = SelectionMode.Multiple;
        CanUserResizeColumns = true;
        AddHandler(InputElement.PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerCaptureLostEvent, OnGridPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(InputElement.PointerExitedEvent, OnGridPointerExited, RoutingStrategies.Bubble, handledEventsToo: true);
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
        var columns = new List<DynamicTableViewColumnState>(source.Columns.Count);
        for (var i = 0; i < source.Columns.Count; i++)
        {
            var definition = source.Columns[i];
            var widthMode = definition.WidthMode;
            var width = definition.Width;
            if (widthMode != DynamicTableViewWidthMode.Star && _nativeColumns.TryGetValue(definition.Key, out var native))
            {
                var actualWidth = native.ActualWidth;
                if (double.IsFinite(actualWidth) && actualWidth > 0)
                    width = actualWidth;
            }
            columns.Add(new(definition.Key, i, width, widthMode));
        }
        var scrollOffset = _pendingScrollOffset ?? FindScrollViewer()?.Offset ?? default;
        return new(columns, source.SortDescriptors.ToArray(), source.FilterDescriptors.ToArray(), scrollOffset);
    }

    /// <summary>Restores column order, sizing modes and widths, sort descriptors, filters, and scroll offset by stable key.</summary>
    public void RestoreState(DynamicTableViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var source = Source ?? throw new InvalidOperationException("DynamicTableView has no source.");
        var ordered = state.Columns
            .Where(saved => source.Columns.Any(column => string.Equals(column.Key, saved.Key, StringComparison.Ordinal)))
            .OrderBy(static saved => saved.Order)
            .ToArray();

        _restoringState = true;
        try
        {
            for (var targetIndex = 0; targetIndex < ordered.Length; targetIndex++)
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
            definition.WidthMode = saved.WidthMode;
            definition.Width = saved.WidthMode == DynamicTableViewWidthMode.Pixel
                ? Math.Max(definition.MinWidth, saved.Width)
                : saved.Width;
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
        _resizeHeadersDirty = true;
        LayoutUpdated += OnLayoutUpdated;
        QueueStarColumnMinimumWidthUpdate();
        QueueHeaderRefresh();
        QueueAutoWidthPass();
        QueueScrollRestore();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        _resizeHeadersDirty = true;
        _resizeHeaders.Clear();
        _scrollViewer = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _scrollViewer = null;
        base.OnApplyTemplate(e);
        _resizeHeadersDirty = true;
    }

    private void AttachSource(IDynamicTableViewSource? source)
    {
        _resizeHeadersDirty = true;
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
            _nativeColumns.Clear();
            return;
        }

        ItemsSource = source.Items;
        Selection = source.SelectionModel;
        source.Changed += SourceOnChanged;
        source.Columns.CollectionChanged += ColumnsOnCollectionChanged;
        RebuildNativeColumns();
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
        _resizeHeadersDirty = true;

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
                    _nativeColumns.Remove(((DynamicTableViewColumn)column.Header!).Key);
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
        _resizeHeadersDirty = true;
        QueueAutoWidthPass();
    }

    private void RebuildNativeColumns()
    {
        _nativeColumns.Clear();
        Columns.Clear();
        // Rebuilding the native columns makes Avalonia recreate the column header visuals,
        // so the cached resize grips point at detached headers until the new ones are realized.
        _resizeHeadersDirty = true;
        _resizeHeaders.Clear();
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
        => new()
        {
            Header = definition,
            HeaderTemplate = CreateHeaderTemplate(),
            CanUserResize = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Width = GetInitialWidth(definition),
            CellTemplate = new FuncDataTemplate<object>((_, _) => new DynamicTableViewCell(this, definition), supportsRecycling: true)
        };

    private IDataTemplate CreateHeaderTemplate()
        => new FuncDataTemplate<DynamicTableViewColumn>(
            (definition, _) => definition is null ? null : new DynamicTableViewHeader(this, definition));

    private void QueueHeaderRefresh()
    {
        if (_headerRefreshQueued || Source is null)
            return;
        _resizeHeadersDirty = true;
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
            _resizeHeadersDirty = true;
        }, DispatcherPriority.Loaded);
    }

    private static GridLength GetInitialWidth(DynamicTableViewColumn definition)
        => definition.WidthMode switch
        {
            DynamicTableViewWidthMode.Pixel => new GridLength(Math.Max(definition.MinWidth, definition.Width)),
            DynamicTableViewWidthMode.Star => new GridLength(Math.Max(0.1, definition.Width), GridUnitType.Star),
            _ => new GridLength(Math.Max(definition.MinWidth, Math.Max(80, definition.Width)))
        };

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
        if (_resizeHeadersDirty)
            RefreshResizeHeaders();
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
        => _resizeHeadersDirty = true;

    internal void ResizeColumnAtDivider(string headerKey, double deltaX)
    {
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
        double newWidth = Math.Max(definition.MinWidth, currentWidth + deltaX);
        List<(DynamicTableViewColumn Definition, TableViewColumn Native, double Width)> precedingStars = [];
        for (int index = 0; index < resizeIndex; index++)
        {
            DynamicTableViewColumn preceding = Source.Columns[index];
            if (preceding.WidthMode != DynamicTableViewWidthMode.Star ||
                !_nativeColumns.TryGetValue(preceding.Key, out TableViewColumn? precedingNative) ||
                !precedingNative.IsVisible)
                continue;

            precedingStars.Add((preceding, precedingNative, precedingNative.ActualWidth));
        }
        foreach ((DynamicTableViewColumn preceding, TableViewColumn precedingNative, double precedingWidth) in precedingStars)
        {
            _manuallySizedColumns.Add(preceding.Key);
            preceding.WidthMode = DynamicTableViewWidthMode.Pixel;
            preceding.Width = precedingWidth;
            precedingNative.Width = new GridLength(precedingWidth);
        }
        _manuallySizedColumns.Add(definition.Key);
        definition.WidthMode = DynamicTableViewWidthMode.Pixel;
        definition.Width = newWidth;
        native.Width = new GridLength(newWidth);
    }

    internal TableViewColumn? GetNativeColumn(string key)
        => _nativeColumns.TryGetValue(key, out var column) ? column : null;

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        QueueStarColumnMinimumWidthUpdate();
        if (_resizeHeadersDirty)
            RefreshResizeHeaders();
        TryRestorePendingScrollOffset();
        QueueAutoWidthPass();
    }

    private void QueueStarColumnMinimumWidthUpdate()
    {
        if (_starWidthUpdateQueued || Source is null || VisualRoot is null)
            return;

        for (int index = 0; index < Columns.Count; index++)
        {
            if (Columns[index].Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star })
                continue;

            _starWidthUpdateQueued = true;
            Dispatcher.UIThread.Post(_updateStarWidths, DispatcherPriority.Background);
            return;
        }
    }

    private void UpdateStarColumnMinimumWidths()
    {
        _starWidthUpdateQueued = false;
        if (Source is null || VisualRoot is null)
            return;

        double availableWidth = FindScrollViewer()?.Viewport.Width ?? Bounds.Width;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            return;

        double weight = 0;
        for (int index = 0; index < Columns.Count; index++)
        {
            TableViewColumn native = Columns[index];
            if (!native.IsVisible)
                continue;
            if (native.Header is not DynamicTableViewColumn definition)
                continue;
            if (definition.WidthMode == DynamicTableViewWidthMode.Star)
                weight += Math.Max(0.1, definition.Width);
            else if (double.IsFinite(native.ActualWidth))
                availableWidth -= native.ActualWidth;
        }

        // Reserve minima before distributing the remaining space by star weight.
        // Each pass removes at least one constrained column, without scratch storage.
        double unitWidth = weight > 0 ? Math.Max(0, availableWidth) / weight : 0;
        double previousUnitWidth = double.PositiveInfinity;
        while (weight > 0)
        {
            bool constrained = false;
            for (int index = 0; index < Columns.Count; index++)
            {
                if (!Columns[index].IsVisible || Columns[index].Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star } definition)
                    continue;
                double columnWeight = Math.Max(0.1, definition.Width);
                double threshold = definition.MinWidth / columnWeight;
                if (threshold > unitWidth && threshold <= previousUnitWidth)
                {
                    availableWidth -= definition.MinWidth;
                    weight -= columnWeight;
                    constrained = true;
                }
            }
            if (!constrained)
                break;
            previousUnitWidth = unitWidth;
            unitWidth = weight > 0 ? Math.Max(0, availableWidth) / weight : 0;
        }

        for (int index = 0; index < Columns.Count; index++)
        {
            TableViewColumn native = Columns[index];
            if (!native.IsVisible || native.Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star } definition)
                continue;
            GridLength width = Math.Max(0.1, definition.Width) * unitWidth < definition.MinWidth
                ? new GridLength(definition.MinWidth)
                : GetInitialWidth(definition);
            if (native.Width != width)
                native.Width = width;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.X != 0)
            _resizeHeadersDirty = true;
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
        _cellSamples.Clear();
        foreach (var cell in this.GetVisualDescendants().OfType<TableViewCell>())
        {
            if (cell.Column?.Header is not DynamicTableViewColumn definition || cell.DataContext is not { } row)
                continue;
            if (definition.WidthMode is not (DynamicTableViewWidthMode.Auto or DynamicTableViewWidthMode.Cells) ||
                _manuallySizedColumns.Contains(definition.Key))
                continue;
            var measured = MeasureText(definition.GetDisplayValue(row)) + 24;
            if (!_cellSamples.TryGetValue(definition.Key, out var current) || measured > current)
                _cellSamples[definition.Key] = measured;
        }

        foreach (var definition in Source.Columns)
        {
            if (definition.WidthMode is DynamicTableViewWidthMode.Pixel or DynamicTableViewWidthMode.Star ||
                _manuallySizedColumns.Contains(definition.Key) || !_nativeColumns.TryGetValue(definition.Key, out var native))
                continue;

            var desired = definition.WidthMode == DynamicTableViewWidthMode.Cells ? definition.MinWidth : MeasureText(definition.Header.ToString()) + 24;
            if ((definition.WidthMode is DynamicTableViewWidthMode.Auto or DynamicTableViewWidthMode.Cells) && _cellSamples.TryGetValue(definition.Key, out var cells))
                desired = Math.Max(desired, cells);
            desired = Math.Max(definition.MinWidth, desired);
            var currentWidth = native.ActualWidth;
            if (!double.IsFinite(currentWidth) || desired > currentWidth + 1)
                native.Width = new GridLength(desired);
        }
    }

    private double MeasureText(string? text)
    {
        _textMeasurer.Text = text ?? string.Empty;
        _textMeasurer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _textMeasurer.DesiredSize.Width;
    }

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
        if (TryGetResizeColumnAtDivider(pressPosition, out string? resizeColumnKey, out _))
        {
            _resizePointer = e.Pointer;
            _resizeColumnKey = resizeColumnKey;
            _resizeLastX = pressPosition.X;
            _pointerDownColumn = -1;
            _headerDragStarted = false;
            Classes.Set(ColumnResizingClass, true);
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

        if (ReferenceEquals(_resizePointer, e.Pointer) && _resizeColumnKey is { } resizeColumnKey)
        {
            double currentX = e.GetPosition(this).X;
            ResizeColumnAtDivider(resizeColumnKey, currentX - _resizeLastX);
            _resizeLastX = currentX;
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed is false)
        {
            UpdateResizePointerOver(e.GetPosition(this));
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
        if (ReferenceEquals(_resizePointer, e.Pointer))
        {
            _resizePointer = null;
            _resizeColumnKey = null;
            Classes.Set(ColumnResizingClass, false);
            e.Pointer.Capture(null);
            e.Handled = true;
            UpdateResizePointerOver(e.GetPosition(this));
        }
        _pointerDownColumn = -1;
        _headerDragStarted = false;
        _selectionDragAnchor = -1;
        _selectionDragLast = -1;
        _selectionDragInitialSelection = null;
    }

    private void OnGridPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(_resizePointer, e.Pointer))
            return;

        _resizePointer = null;
        _resizeColumnKey = null;
        Classes.Set(ColumnResizingClass, false);
    }

    private void OnGridPointerExited(object? sender, PointerEventArgs e)
    {
        if (_resizePointer is null)
            SetResizeHoverHeader(null);
    }

    private void UpdateResizePointerOver(Point position)
    {
        TableViewColumnHeader? nextHoverHeader = null;
        if (TryGetResizeColumnAtDivider(position, out string? resizeColumnKey, out bool isRightHalf))
        {
            int columnIndex = IndexOfColumn(Source!.Columns, resizeColumnKey!);
            string hoveredKey = isRightHalf && columnIndex + 1 < Source.Columns.Count
                ? Source.Columns[columnIndex + 1].Key : resizeColumnKey!;
            nextHoverHeader = FindResizeHeader(hoveredKey);
        }
        SetResizeHoverHeader(nextHoverHeader);
    }

    private void SetResizeHoverHeader(TableViewColumnHeader? header)
    {
        if (ReferenceEquals(_resizeHoverHeader, header))
            return;

        _resizeHoverHeader?.Classes.Set(ResizePointerOverClass, false);
        _resizeHoverHeader = header;
        _resizeHoverHeader?.Classes.Set(ResizePointerOverClass, true);
    }

    private bool TryGetResizeColumnAtDivider(Point position, out string? columnKey, out bool isRightHalf)
    {
        columnKey = null;
        isRightHalf = false;
        if (!CanUserResizeColumns || Source is null)
            return false;
        if (_resizeHeadersDirty)
            RefreshResizeHeaders();

        if (TryHitTestDivider(position, out columnKey, out isRightHalf))
            return true;

        // The cached headers can go stale when Avalonia recreates the column header visuals,
        // for example after the native columns are rebuilt. Refresh once and retry so the
        // resize gesture keeps working against the live headers.
        if (_resizeHeadersStale)
        {
            RefreshResizeHeaders();
            return TryHitTestDivider(position, out columnKey, out isRightHalf);
        }

        return false;
    }

    private bool TryHitTestDivider(Point position, out string? columnKey, out bool isRightHalf)
    {
        columnKey = null;
        isRightHalf = false;
        _resizeHeadersStale = false;

        const double hitTestTolerance = 1;
        for (int i = 0; i < _resizeHeaders.Count; i++)
        {
            (TableViewColumnHeader header, Thumb grip) = _resizeHeaders[i];
            if (header.Column?.Header is not DynamicTableViewColumn definition)
                continue;
            int columnIndex = IndexOfColumn(Source!.Columns, definition.Key);
            if (columnIndex < 0 || !grip.IsVisible)
                continue;

            // Get the grip's actual position in the table's coordinate system
            if (grip.TranslatePoint(default, this) is not { } gripOrigin)
            {
                // The grip is no longer part of the visual tree, so the cache is stale.
                _resizeHeadersStale = true;
                continue;
            }

            double halfGripWidth = grip.Bounds.Width / 2;
            if (halfGripWidth <= 0)
                continue;

            // The divider center is at the center of the grip
            double dividerCenter = gripOrigin.X + halfGripWidth;
            
            if (Math.Abs(position.X - dividerCenter) > halfGripWidth + hitTestTolerance ||
                position.Y < gripOrigin.Y - hitTestTolerance ||
                position.Y > gripOrigin.Y + grip.Bounds.Height + hitTestTolerance)
                continue;

            columnKey = definition.Key;
            isRightHalf = position.X >= dividerCenter;
            return true;
        }

        return false;
    }

    private TableViewColumnHeader? FindResizeHeader(string key)
    {
        for (int i = 0; i < _resizeHeaders.Count; i++)
        {
            TableViewColumnHeader header = _resizeHeaders[i].Header;
            if (header.Column?.Header is DynamicTableViewColumn definition &&
                string.Equals(definition.Key, key, StringComparison.Ordinal))
                return header;
        }

        return null;
    }

    private void RefreshResizeHeaders()
    {
        _resizeHeadersDirty = false;
        _resizeHeaders.Clear();
        foreach (TableViewColumnHeader header in this.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            Thumb? grip = header.GetVisualDescendants().OfType<Thumb>()
                .FirstOrDefault(static candidate => candidate.Name == "PART_ColumnResizeGrip");
            if (grip is not null)
                _resizeHeaders.Add((header, grip));
        }
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
