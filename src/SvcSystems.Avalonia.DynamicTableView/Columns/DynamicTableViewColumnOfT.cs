namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Typed column definition using trim-safe delegates.</summary>
public sealed class DynamicTableViewColumn<T> : DynamicTableViewColumn
{
    private readonly Func<T, object?> _valueSelector;
    private readonly Func<T, string?> _displaySelector;
    private Func<T, T, int>? _typedCompare;
    private Func<DynamicTableViewFilterDescriptor, Func<T, bool>?>? _typedFilter;

    /// <summary>Creates a text or templated column.</summary>
    /// <param name="key">Stable column key.</param>
    /// <param name="header">Header content.</param>
    /// <param name="valueType">Type of the value returned by <paramref name="valueSelector"/>.</param>
    /// <param name="valueSelector">Gets the row value used by table operations.</param>
    /// <param name="displaySelector">Optionally formats the value for the fallback text cell.</param>
    /// <param name="cellTemplate">Optionally supplies custom cell content.</param>
    public DynamicTableViewColumn(
        string key,
        object header,
        Type valueType,
        Func<T, object?> valueSelector,
        Func<T, string?>? displaySelector = null,
        IDataTemplate? cellTemplate = null)
        : base(key, header, valueType, cellTemplate)
    {
        _valueSelector = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        _displaySelector = displaySelector ?? (item => _valueSelector(item)?.ToString());
    }

    /// <summary>Creates a column using a typed value selector.</summary>
    /// <param name="key">Stable column key.</param>
    /// <param name="header">Header content.</param>
    /// <param name="valueSelector">Gets the row value used by table operations.</param>
    /// <param name="displaySelector">Optionally formats the value for the fallback text cell.</param>
    /// <param name="cellTemplate">Optionally supplies custom cell content.</param>
    /// <typeparam name="TValue">Selected value type.</typeparam>
    public static DynamicTableViewColumn<T> Create<TValue>(
        string key,
        object header,
        Func<T, TValue> valueSelector,
        Func<T, string?>? displaySelector = null,
        IDataTemplate? cellTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(valueSelector);
        var column = new DynamicTableViewColumn<T>(key, header, typeof(TValue), item => valueSelector(item), displaySelector, cellTemplate);
        var numericType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (numericType == typeof(byte) || numericType == typeof(sbyte) ||
            numericType == typeof(short) || numericType == typeof(ushort) ||
            numericType == typeof(int) || numericType == typeof(uint) ||
            numericType == typeof(long) || numericType == typeof(ulong) ||
            numericType == typeof(decimal))
        {
            column._typedCompare = (left, right) =>
                Comparer<TValue>.Default.Compare(valueSelector(left), valueSelector(right));
            column._typedFilter = descriptor => CreateNumericFilter(valueSelector, descriptor);
        }
        return column;
    }

    /// <summary>Gets or sets a custom filter flyout factory.</summary>
    public Func<DynamicTableViewFilterContext, Control>? FilterFlyoutFactory { get; set; }

    /// <summary>Gets or sets choices for a standard enum or Boolean filter.</summary>
    public IReadOnlyList<DynamicTableViewFilterChoice> FilterChoices { get; set; } = [];

    /// <summary>Creates an enum column and its trim-safe choices.</summary>
    public static DynamicTableViewColumn<T> CreateEnum<TEnum>(
        string key,
        object header,
        Func<T, TEnum> valueSelector,
        Func<T, string?>? displaySelector = null)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(valueSelector);
        var column = Create(key, header, valueSelector, displaySelector);
        column.FilterChoices = Enum.GetValues<TEnum>()
            .Select(static value => new DynamicTableViewFilterChoice(value.ToString(), value))
            .ToArray();
        return column;
    }

    internal override object? GetValue(object item)
        => item is T typedItem ? _valueSelector(typedItem) : null;

    internal override int CompareRows(object left, object right, Func<object?, object?, int> compare)
        => _typedCompare is not null && left is T typedLeft && right is T typedRight
            ? _typedCompare(typedLeft, typedRight)
            : base.CompareRows(left, right, compare);

    internal override Func<object, bool> CreateFilter(
        DynamicTableViewFilterDescriptor descriptor,
        Func<object?, DynamicTableViewFilterDescriptor, bool> matches)
    {
        var typed = _typedFilter?.Invoke(descriptor);
        return typed is null ? base.CreateFilter(descriptor, matches) : item => item is T row && typed(row);
    }

    private static Func<T, bool>? CreateNumericFilter<TValue>(
        Func<T, TValue> selector,
        DynamicTableViewFilterDescriptor descriptor)
    {
        var operation = descriptor.Operator;
        if (operation is not (DynamicTableViewFilterOperator.Equals or DynamicTableViewFilterOperator.NotEquals or
            DynamicTableViewFilterOperator.GreaterThan or DynamicTableViewFilterOperator.GreaterThanOrEqual or
            DynamicTableViewFilterOperator.LessThan or DynamicTableViewFilterOperator.LessThanOrEqual or
            DynamicTableViewFilterOperator.Between or DynamicTableViewFilterOperator.NotBetween or
            DynamicTableViewFilterOperator.In))
            return null;

        if (operation == DynamicTableViewFilterOperator.In)
        {
            var values = descriptor.Values ?? [];
            var typedValues = new TValue[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is not TValue typedValue)
                    return null;
                typedValues[i] = typedValue;
            }
            return row =>
            {
                var candidate = selector(row);
                for (var i = 0; i < typedValues.Length; i++)
                    if (Comparer<TValue>.Default.Compare(candidate, typedValues[i]) == 0) return true;
                return false;
            };
        }

        if (descriptor.Value is not TValue first ||
            (operation is DynamicTableViewFilterOperator.Between or DynamicTableViewFilterOperator.NotBetween &&
             descriptor.SecondValue is not TValue))
            return null;
        var second = descriptor.SecondValue is TValue secondValue ? secondValue : default!;
        return row =>
        {
            var candidate = selector(row);
            var firstResult = Comparer<TValue>.Default.Compare(candidate, first);
            return operation switch
            {
                DynamicTableViewFilterOperator.Equals => firstResult == 0,
                DynamicTableViewFilterOperator.NotEquals => firstResult != 0,
                DynamicTableViewFilterOperator.GreaterThan => firstResult > 0,
                DynamicTableViewFilterOperator.GreaterThanOrEqual => firstResult >= 0,
                DynamicTableViewFilterOperator.LessThan => firstResult < 0,
                DynamicTableViewFilterOperator.LessThanOrEqual => firstResult <= 0,
                DynamicTableViewFilterOperator.Between => firstResult >= 0 && Comparer<TValue>.Default.Compare(candidate, second) <= 0,
                DynamicTableViewFilterOperator.NotBetween => firstResult < 0 || Comparer<TValue>.Default.Compare(candidate, second) > 0,
                _ => true
            };
        };
    }

    internal override string GetDisplayValue(object item)
        => item is T typedItem ? _displaySelector(typedItem) ?? string.Empty : string.Empty;

    internal override Func<DynamicTableViewFilterContext, Control>? GetFilterFlyoutFactory()
        => FilterFlyoutFactory;

    internal override IReadOnlyList<DynamicTableViewFilterChoice> GetFilterChoices()
        => FilterChoices;
}
