namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Saved display settings for one column.</summary>
/// <param name="Key">The stable column key.</param>
/// <param name="Order">The column's saved display order.</param>
/// <param name="Width">The saved pixel width, or the star weight when <paramref name="WidthMode"/> is <see cref="DynamicTableViewWidthMode.Star"/>.</param>
/// <param name="WidthMode">The sizing mode to restore. Defaults to pixels for older state creation code.</param>
public sealed record DynamicTableViewColumnState(
    string Key,
    int Order,
    double Width,
    DynamicTableViewWidthMode WidthMode = DynamicTableViewWidthMode.Pixel);
