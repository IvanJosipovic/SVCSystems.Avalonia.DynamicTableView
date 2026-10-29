namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Converts between <see cref="DynamicTableViewState"/> and column metadata.</summary>
/// <remarks>
/// Every member is a pure function of its arguments. The control keeps the side effects
/// (moving columns, writing width metadata) so this type can be reasoned about without a visual tree.
/// </remarks>
internal static class DynamicTableViewStateSerializer
{
    /// <summary>
    /// Captures keyed column order and widths. <paramref name="actualWidthResolver"/> supplies the
    /// realized width for a key; it returns a non-positive value when the column has no realized width.
    /// </summary>
    public static IReadOnlyList<DynamicTableViewColumnState> CaptureColumns(
        IReadOnlyList<DynamicTableViewColumn> columns,
        Func<string, double>? actualWidthResolver)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var captured = new List<DynamicTableViewColumnState>(columns.Count);
        for (var index = 0; index < columns.Count; index++)
        {
            DynamicTableViewColumn definition = columns[index];
            var widthMode = definition.WidthMode;
            var width = definition.Width;
            if (widthMode != DynamicTableViewWidthMode.Star && actualWidthResolver is not null)
            {
                var actualWidth = actualWidthResolver(definition.Key);
                if (double.IsFinite(actualWidth) && actualWidth > 0)
                    width = actualWidth;
            }

            captured.Add(new(definition.Key, index, width, widthMode));
        }

        return captured;
    }

    /// <summary>
    /// Gets the saved columns that still exist in <paramref name="currentColumns"/>, in their saved order.
    /// Columns that are not present are dropped so a restore never targets a column the source no longer has.
    /// </summary>
    public static IReadOnlyList<DynamicTableViewColumnState> PlanColumnOrder(
        DynamicTableViewState state,
        IReadOnlyList<DynamicTableViewColumn> currentColumns)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(currentColumns);

        var ordered = new List<DynamicTableViewColumnState>(state.Columns.Count);
        foreach (DynamicTableViewColumnState saved in state.Columns)
        {
            for (var index = 0; index < currentColumns.Count; index++)
            {
                if (!string.Equals(currentColumns[index].Key, saved.Key, StringComparison.Ordinal))
                    continue;
                ordered.Add(saved);
                break;
            }
        }

        ordered.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        return ordered;
    }

    /// <summary>
    /// Resolves the width to restore for one column. A pixel width is clamped to the effective minimum,
    /// which may come from a style rather than the column metadata.
    /// </summary>
    public static double ResolveRestoredWidth(
        DynamicTableViewColumnState saved,
        DynamicTableViewColumn definition,
        TableViewColumn? native)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);

        return saved.WidthMode == DynamicTableViewWidthMode.Pixel
            ? Math.Max(ColumnWidthCalculator.GetEffectiveMinWidth(definition, native), saved.Width)
            : saved.Width;
    }
}
