namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Saved display settings for one column.</summary>
/// <param name="Key">The stable column key.</param>
/// <param name="Order">The column's saved display order.</param>
/// <param name="Width">The saved native Avalonia width, including its GridUnitType.</param>
public sealed record DynamicTableViewColumnState(
    string Key,
    int Order,
    GridLength Width);
