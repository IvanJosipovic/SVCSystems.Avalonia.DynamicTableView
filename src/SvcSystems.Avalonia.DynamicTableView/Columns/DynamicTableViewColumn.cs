namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Base metadata for one dynamic table column.</summary>
/// <remarks>
/// Column definitions are mutable and can be shared. Changes observed by an attached table,
/// including header, cell template, cell text options, sizing, and filter configuration,
/// must be made on the UI thread.
/// </remarks>
public abstract class DynamicTableViewColumn
{
    private object _header;
    private IDataTemplate? _cellTemplate;
    private bool _isSearchable = true;
    private bool _canUserSort = true;
    private bool _canUserFilter = true;
    private TextWrapping? _cellTextWrapping;
    private TextTrimming? _cellTextTrimming;
    private int? _cellMaxLines;
    private DynamicTableViewWidthMode _widthMode = DynamicTableViewWidthMode.Auto;
    private double _width = 1;
    private double _minWidth = 40;

    /// <summary>Creates column metadata.</summary>
    protected DynamicTableViewColumn(string key, object header, Type valueType, IDataTemplate? cellTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(valueType);
        Key = key;
        _header = header;
        ValueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        _cellTemplate = cellTemplate;
    }

    /// <summary>Gets the stable column key.</summary>
    public string Key { get; }

    /// <summary>
    /// Gets the style classes applied to the native <see cref="TableViewColumn"/>.
    /// </summary>
    /// <remarks>
    /// Classes are copied to the native column before it attaches to the table, so styles that
    /// target them take effect. Use them to style individual columns, for example to set
    /// <see cref="DynamicTableViewColumnProperties.MinWidthProperty"/>.
    /// </remarks>
    public Classes Classes { get; } = [];

    /// <summary>Gets or sets header content.</summary>
    public object Header
    {
        get => _header;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_header, value))
                return;
            _header = value;
            HeaderChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? HeaderChanged;

    /// <summary>Gets the field value type.</summary>
    public Type ValueType { get; }

    /// <summary>Gets or sets the cell template. Changing it updates attached cells; null uses the default text template.</summary>
    public IDataTemplate? CellTemplate
    {
        get => _cellTemplate;
        set
        {
            if (ReferenceEquals(_cellTemplate, value))
                return;
            _cellTemplate = value;
            CellTemplateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? CellTemplateChanged;

    /// <summary>Gets or sets the default text wrapping override for this column. Null inherits from the table.</summary>
    public TextWrapping? CellTextWrapping
    {
        get => _cellTextWrapping;
        set
        {
            if (_cellTextWrapping == value)
                return;
            _cellTextWrapping = value;
            CellTextOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets the default text trimming override for this column. Null inherits from the table.</summary>
    public TextTrimming? CellTextTrimming
    {
        get => _cellTextTrimming;
        set
        {
            if (Equals(_cellTextTrimming, value))
                return;
            _cellTextTrimming = value;
            CellTextOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets the default maximum lines override for this column. Null inherits from the table; zero means unlimited.</summary>
    public int? CellMaxLines
    {
        get => _cellMaxLines;
        set
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "The maximum line count cannot be negative.");
            if (_cellMaxLines == value)
                return;
            _cellMaxLines = value;
            CellTextOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? CellTextOptionsChanged;

    /// <summary>Gets or sets width behavior. Changing it re-applies this column's width in attached tables.</summary>
    public DynamicTableViewWidthMode WidthMode
    {
        get => _widthMode;
        set
        {
            if (_widthMode == value)
                return;
            _widthMode = value;
            WidthOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets the fixed width or Star weight. Changing it re-applies this column's width in attached tables.</summary>
    public double Width
    {
        get => _width;
        set
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "The width must be a finite, non-negative number.");
            if (_width.Equals(value))
                return;
            _width = value;
            WidthOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets or sets minimum width. Changing it re-applies the sizing of this column and of attached Star columns.</summary>
    public double MinWidth
    {
        get => _minWidth;
        set
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "The minimum width must be a finite, non-negative number.");
            if (_minWidth.Equals(value))
                return;
            _minWidth = value;
            WidthOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? WidthOptionsChanged;

    /// <summary>Gets or sets whether this column participates in search.</summary>
    public bool IsSearchable
    {
        get => _isSearchable;
        set
        {
            if (_isSearchable == value) return;
            _isSearchable = value;
            SearchabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? SearchabilityChanged;

    /// <summary>Gets or sets whether users can sort this column.</summary>
    public bool CanUserSort
    {
        get => _canUserSort;
        set
        {
            if (_canUserSort == value) return;
            _canUserSort = value;
            SortCapabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? SortCapabilityChanged;

    /// <summary>Gets or sets whether this column has a filter flyout.</summary>
    public bool CanUserFilter
    {
        get => _canUserFilter;
        set
        {
            if (_canUserFilter == value) return;
            _canUserFilter = value;
            FilterCapabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? FilterCapabilityChanged;

    internal event EventHandler? FilterConfigurationChanged;

    /// <summary>Notifies attached headers that filter choices or the flyout factory changed.</summary>
    protected void OnFilterConfigurationChanged()
        => FilterConfigurationChanged?.Invoke(this, EventArgs.Empty);

    internal abstract object? GetValue(object item);

    internal virtual int CompareRows(object left, object right, Func<object?, object?, int> compare)
        => compare(GetValue(left), GetValue(right));

    internal virtual Func<object, bool> CreateFilter(
        DynamicTableViewFilterDescriptor descriptor,
        Func<object?, DynamicTableViewFilterDescriptor, bool> matches)
        => item => matches(GetValue(item), descriptor);

    internal abstract string GetDisplayValue(object item);

    internal virtual Func<DynamicTableViewFilterContext, Control>? GetFilterFlyoutFactory() => null;

    internal virtual IReadOnlyList<DynamicTableViewFilterChoice> GetFilterChoices() => [];
}
