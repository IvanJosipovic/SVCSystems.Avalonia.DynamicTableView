namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Computes the widths of star-sized columns in one pass, without intermediate writes.</summary>
/// <remarks>
/// <para>
/// Avalonia's own star distribution is purely proportional and ignores minimum widths, so two star
/// columns with a minimum larger than their share would both render below it. A column that cannot
/// honour its minimum has to be removed from the star pool and the remaining space redistributed.
/// </para>
/// <para>
/// The whole result is computed before the caller writes anything. Writing widths incrementally
/// would let a column be laid out below its minimum for one frame and then snap back, which is
/// visible to the user as a flash.
/// </para>
/// </remarks>
internal static class StarColumnWidths
{
    /// <summary>
    /// Computes the width available to star columns after visible fixed columns take their realized width.
    /// </summary>
    /// <param name="availableWidth">The viewport width.</param>
    /// <param name="columns">The native columns, in display order.</param>
    /// <remarks>
    /// This is the value the star result depends on, so the caller can use it to skip a recompute
    /// when nothing that affects it has changed.
    /// </remarks>
    public static double ComputeStarBudget(double availableWidth, IReadOnlyList<TableViewColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        for (var index = 0; index < columns.Count; index++)
        {
            TableViewColumn native = columns[index];
            if (!native.IsVisible || native.Header is not DynamicTableViewColumn definition)
                continue;
            if (definition.WidthMode != DynamicTableViewWidthMode.Star && double.IsFinite(native.ActualWidth))
                availableWidth -= native.ActualWidth;
        }

        return availableWidth;
    }

    /// <summary>
    /// Computes the width of every visible star column into <paramref name="widths"/>.
    /// </summary>
    /// <param name="starBudget">The width available to star columns, from <see cref="ComputeStarBudget"/>.</param>
    /// <param name="columns">The native columns, in display order.</param>
    /// <param name="widths">
    /// Receives the computed width for each star column, indexed the same as
    /// <paramref name="columns"/>. Entries for non-star and hidden columns are left untouched.
    /// </param>
    public static void ComputeWidths(double starBudget, IReadOnlyList<TableViewColumn> columns, GridLength[] widths)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(widths);
        if (widths.Length < columns.Count)
            throw new ArgumentException("The width buffer must cover every column.", nameof(widths));

        double weight = 0;
        for (var index = 0; index < columns.Count; index++)
        {
            TableViewColumn native = columns[index];
            if (!native.IsVisible || native.Header is not DynamicTableViewColumn definition)
                continue;
            if (definition.WidthMode == DynamicTableViewWidthMode.Star)
                weight += ColumnWidthCalculator.GetStarWeight(definition);
        }

        double availableWidth = starBudget;
        double unitWidth = weight > 0 ? Math.Max(0, availableWidth) / weight : 0;

        // Reserve minima before distributing the remaining space by star weight.
        // Each pass removes at least one constrained column, without scratch storage.
        double previousUnitWidth = double.PositiveInfinity;
        while (weight > 0)
        {
            bool constrained = false;
            for (var index = 0; index < columns.Count; index++)
            {
                TableViewColumn native = columns[index];
                if (!native.IsVisible ||
                    native.Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star } definition)
                {
                    continue;
                }

                double columnWeight = ColumnWidthCalculator.GetStarWeight(definition);
                double minimum = ColumnWidthCalculator.GetEffectiveMinWidth(definition, native);
                double threshold = minimum / columnWeight;
                if (threshold > unitWidth && threshold <= previousUnitWidth)
                {
                    availableWidth -= minimum;
                    weight -= columnWeight;
                    constrained = true;
                }
            }

            if (!constrained)
                break;
            previousUnitWidth = unitWidth;
            unitWidth = weight > 0 ? Math.Max(0, availableWidth) / weight : 0;
        }

        for (var index = 0; index < columns.Count; index++)
        {
            TableViewColumn native = columns[index];
            if (!native.IsVisible ||
                native.Header is not DynamicTableViewColumn { WidthMode: DynamicTableViewWidthMode.Star } definition)
            {
                continue;
            }

            double minimum = ColumnWidthCalculator.GetEffectiveMinWidth(definition, native);
            widths[index] = ColumnWidthCalculator.GetStarWidth(definition, minimum, unitWidth);
        }
    }
}
