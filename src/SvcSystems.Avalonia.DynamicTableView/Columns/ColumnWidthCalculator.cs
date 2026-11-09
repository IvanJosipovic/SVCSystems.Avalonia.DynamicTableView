namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Computes widths implied by column sizing metadata and effective column limits.</summary>
/// <remarks>The rules are pure functions so they can be reasoned about without a visual tree.</remarks>
internal static class ColumnWidthCalculator
{
    /// <summary>Gets the smallest star weight a column can carry, so a star column never collapses to zero.</summary>
    public const double MinimumStarWeight = 0.1;

    /// <summary>Gets the smallest pixel width used by <see cref="DynamicTableViewWidthMode.Auto"/>.</summary>
    public const double MinimumAutoWidth = 80;

    /// <summary>Gets the horizontal cell padding included in measured text widths.</summary>
    public const double MeasuredTextPadding = 24;

    /// <summary>Resolves the initial width for <paramref name="definition"/>, honoring its limits.</summary>
    public static GridLength GetInitialWidth(DynamicTableViewColumn definition, double minWidth, double maxWidth)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.WidthMode == DynamicTableViewWidthMode.Star)
            return new GridLength(Math.Max(MinimumStarWeight, definition.Width), GridUnitType.Star);

        double width = definition.WidthMode == DynamicTableViewWidthMode.Pixel
            ? definition.Width
            : Math.Max(MinimumAutoWidth, definition.Width);
        return new GridLength(ClampWidth(width, minWidth, maxWidth));
    }

    /// <summary>Gets a column's effective minimum width.</summary>
    public static double GetEffectiveMinWidth(DynamicTableViewColumn definition, TableViewColumn? native)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return native?.MinWidth ?? definition.MinWidth;
    }

    /// <summary>Gets a column's effective maximum width.</summary>
    public static double GetEffectiveMaxWidth(DynamicTableViewColumn definition, TableViewColumn? native)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return native?.MaxWidth ?? definition.MaxWidth;
    }

    /// <summary>Clamps a pixel width, with the minimum taking precedence when limits conflict.</summary>
    public static double ClampWidth(double width, double minWidth, double maxWidth)
        => Math.Max(minWidth, Math.Min(maxWidth, width));
}
