namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Computes the widths implied by column sizing metadata.</summary>
/// <remarks>
/// The rules are pure functions of the metadata and the styled minimum, so they are kept out of
/// <see cref="DynamicTableView"/> and can be reasoned about without a visual tree.
/// </remarks>
internal static class ColumnWidthCalculator
{
    /// <summary>Gets the smallest star weight a column can carry, so a star column never collapses to zero.</summary>
    public const double MinimumStarWeight = 0.1;

    /// <summary>Gets the smallest pixel width used by <see cref="DynamicTableViewWidthMode.Auto"/>.</summary>
    public const double MinimumAutoWidth = 80;

    /// <summary>Gets the horizontal cell padding included in measured text widths.</summary>
    public const double MeasuredTextPadding = 24;

    /// <summary>Resolves the width to apply for <paramref name="definition"/>, honouring <paramref name="minWidth"/>.</summary>
    public static GridLength GetInitialWidth(DynamicTableViewColumn definition, double minWidth)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.WidthMode switch
        {
            DynamicTableViewWidthMode.Pixel => new GridLength(Math.Max(minWidth, definition.Width)),
            DynamicTableViewWidthMode.Star => new GridLength(Math.Max(MinimumStarWeight, definition.Width), GridUnitType.Star),
            _ => new GridLength(Math.Max(minWidth, Math.Max(MinimumAutoWidth, definition.Width)))
        };
    }

    /// <summary>
    /// Resolves a column's minimum width, preferring the styleable attached value so a theme or
    /// application style can override the plain metadata on the definition. Falls back to the
    /// definition when the attached property is unset (NaN) or invalid.
    /// </summary>
    public static double GetEffectiveMinWidth(DynamicTableViewColumn definition, TableViewColumn? native)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (native is not null)
        {
            double styled = DynamicTableViewColumnProperties.GetMinWidth(native);
            if (!double.IsNaN(styled) && double.IsFinite(styled) && styled >= 0)
                return styled;
        }

        return definition.MinWidth;
    }

    /// <summary>Gets the weight a star column contributes to the available width.</summary>
    public static double GetStarWeight(DynamicTableViewColumn definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Math.Max(MinimumStarWeight, definition.Width);
    }

    /// <summary>
    /// Computes the pixel width for one star column. A column whose share of the available width
    /// would fall below its minimum takes the minimum instead.
    /// </summary>
    public static GridLength GetStarWidth(DynamicTableViewColumn definition, double minimum, double unitWidth)
        => GetStarWeight(definition) * unitWidth < minimum
            ? new GridLength(minimum)
            : GetInitialWidth(definition, minimum);
}
