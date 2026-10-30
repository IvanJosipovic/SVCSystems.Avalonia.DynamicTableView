using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Caches the realized column headers and their resize grips, and hit-tests dividers.</summary>
/// <remarks>
/// The grips live inside Avalonia's column-header template, so they have to be rediscovered
/// whenever the header visuals are recreated. A cached grip can also drop out of the visual tree
/// without the cache being invalidated, so a hit test reports that case through
/// <see cref="WasStale"/> and the caller refreshes once and retries.
/// </remarks>
internal sealed class ResizeHeaderCache
{
    private const double HitTestTolerance = 1;

    private readonly Visual _root;
    private readonly Func<string, bool> _isKnownColumn;
    private readonly List<(TableViewColumnHeader Header, Thumb Grip)> _entries = [];
    private bool _dirty = true;
    private bool _stale;

    /// <summary>Creates a cache rooted at <paramref name="root"/>.</summary>
    /// <param name="root">The control whose coordinate space hit-test points use.</param>
    /// <param name="isKnownColumn">Reports whether a column key still belongs to the current source.</param>
    public ResizeHeaderCache(Visual root, Func<string, bool> isKnownColumn)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _isKnownColumn = isKnownColumn ?? throw new ArgumentNullException(nameof(isKnownColumn));
    }

    /// <summary>Gets a value indicating whether the last hit test found a grip that had left the visual tree.</summary>
    public bool WasStale => _stale;

    /// <summary>Marks the cache as needing a refresh.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Drops every cached header. The cache refreshes on next use.</summary>
    public void Clear()
    {
        _entries.Clear();
        _dirty = true;
        _stale = false;
    }

    /// <summary>Refreshes the cache when it is marked dirty.</summary>
    public void RefreshIfDirty()
    {
        if (_dirty)
            Refresh();
    }

    /// <summary>Rebuilds the cache from the current visual tree.</summary>
    public void Refresh()
    {
        _dirty = false;
        _entries.Clear();
        foreach (TableViewColumnHeader header in _root.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            Thumb? grip = header.GetVisualDescendants().OfType<Thumb>()
                .FirstOrDefault(static candidate => candidate.Name == "PART_ColumnResizeGrip");
            if (grip is not null)
                _entries.Add((header, grip));
        }
    }

    /// <summary>Finds the realized header for a column key.</summary>
    public TableViewColumnHeader? Find(string key)
    {
        for (var index = 0; index < _entries.Count; index++)
        {
            TableViewColumnHeader header = _entries[index].Header;
            if (header.Column?.Header is DynamicTableViewColumn definition &&
                string.Equals(definition.Key, key, StringComparison.Ordinal))
            {
                return header;
            }
        }

        return null;
    }

    /// <summary>Finds the divider under <paramref name="position"/>, in the root's coordinate space.</summary>
    /// <remarks>
    /// Every candidate whose hit band contains the point is considered and the nearest divider
    /// wins. Two adjacent grips can overlap when a column is narrower than its grip, so returning
    /// the first match would resize whichever column happened to be cached first.
    /// </remarks>
    public DividerHit HitTest(Point position)
    {
        RefreshIfDirty();
        _stale = false;

        DividerHit best = default;
        double bestDistance = double.PositiveInfinity;

        for (var index = 0; index < _entries.Count; index++)
        {
            (TableViewColumnHeader header, Thumb grip) = _entries[index];
            if (header.Column?.Header is not DynamicTableViewColumn definition || !_isKnownColumn(definition.Key))
                continue;
            // A hidden column collapses its header, but the grip's own IsVisible stays true,
            // so the effective visibility is what removes it from the resize hit test.
            if (!grip.IsEffectivelyVisible)
                continue;

            if (grip.TranslatePoint(default, _root) is not { } gripOrigin)
            {
                // The grip is no longer part of the visual tree, so the cache is stale.
                _stale = true;
                continue;
            }

            double halfGripWidth = grip.Bounds.Width / 2;
            if (halfGripWidth <= 0)
                continue;

            // The divider center is at the center of the grip. The lower bound is checked
            // without tolerance so a pointer below the header cannot start a resize.
            double dividerCenter = gripOrigin.X + halfGripWidth;
            double distance = Math.Abs(position.X - dividerCenter);
            if (distance > halfGripWidth + HitTestTolerance ||
                position.Y < gripOrigin.Y ||
                position.Y > gripOrigin.Y + grip.Bounds.Height + HitTestTolerance)
            {
                continue;
            }

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = new DividerHit(definition.Key, position.X >= dividerCenter);
        }

        return best;
    }
}
