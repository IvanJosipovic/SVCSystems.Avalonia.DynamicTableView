namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Distributes available width across star-sized columns.</summary>
/// <remarks>
/// A star column must not shrink below its minimum, so the space that a constrained column cannot
/// use has to be redistributed to the remaining star columns. The unit width therefore has to be
/// recomputed until no further column is constrained.
/// </remarks>
internal static class StarColumnWidths
{
    /// <summary>
    /// Computes the width of one star unit for <paramref name="columns"/>.
    /// </summary>
    /// <param name="availableWidth">The viewport width not already consumed by fixed columns.</param>
    /// <param name="columns">The native columns, in display order.</param>
    /// <returns>
    /// The unit width, or zero when there is no visible star column. Multiply by a column's star
    /// weight to get that column's share.
    /// </returns>
    public static double ComputeUnitWidth(double availableWidth, IReadOnlyList<TableViewColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        // Fixed columns take their realized width out of the space left for star columns.
        double weight = 0;
        for (var index = 0; index < columns.Count; index++)
        {
            TableViewColumn native = columns[index];
            if (!native.IsVisible || native.Header is not DynamicTableViewColumn definition)
                continue;
            if (definition.WidthMode == DynamicTableViewWidthMode.Star)
                weight += ColumnWidthCalculator.GetStarWeight(definition);
            else if (double.IsFinite(native.ActualWidth))
                availableWidth -= native.ActualWidth;
        }

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

        return unitWidth;
    }
}
