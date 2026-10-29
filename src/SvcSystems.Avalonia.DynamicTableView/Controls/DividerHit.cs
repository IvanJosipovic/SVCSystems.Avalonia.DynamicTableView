namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Describes which divider, if any, sits under a point.</summary>
/// <param name="Key">The stable key of the column whose divider was hit, or null when no divider was hit.</param>
/// <param name="IsRightHalf">True when the point is on the right half of the divider, which belongs to the next column.</param>
internal readonly record struct DividerHit(string? Key, bool IsRightHalf);
