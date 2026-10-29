namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Reports a horizontal resize delta for one column.</summary>
internal sealed class ColumnResizeRequestedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="columnKey">The stable key of the column being resized.</param>
    /// <param name="deltaX">The horizontal change since the previous report.</param>
    public ColumnResizeRequestedEventArgs(string columnKey, double deltaX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        ColumnKey = columnKey;
        DeltaX = deltaX;
    }

    /// <summary>Gets the stable key of the column being resized.</summary>
    public string ColumnKey { get; }

    /// <summary>Gets the horizontal change since the previous report.</summary>
    public double DeltaX { get; }
}
