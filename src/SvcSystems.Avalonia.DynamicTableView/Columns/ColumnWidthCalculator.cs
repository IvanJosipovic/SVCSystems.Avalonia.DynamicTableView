namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Resolves effective limits and clamps custom pointer-resize widths.</summary>
/// <remarks>Native TableView remains responsible for allocating widths and enforcing limits.</remarks>
internal static class ColumnWidthCalculator
{
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
