using Avalonia.Input;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Owns the column-divider resize gesture: hit testing, hover feedback, and drag state.</summary>
/// <remarks>
/// <para>
/// The control delegates pointer handling here so the resize gesture is one cohesive unit. The
/// controller never mutates column metadata directly; it reports a delta through
/// <see cref="ResizeRequested"/> and the owner applies it.
/// </para>
/// <para>
/// Avalonia's native column resizer is not used because it cannot reverse out of a minimum width
/// once clamped, which the reverse-drag contract requires.
/// </para>
/// </remarks>
internal sealed class ColumnResizeController
{
    private readonly Func<bool> _canResize;
    private readonly ResizeHeaderCache _headers;
    private readonly Func<string, bool, string?> _resolveHoverKey;
    private readonly Action<TableViewColumnHeader?> _setHoverHeader;

    private IPointer? _pointer;
    private string? _columnKey;
    private double _lastX;
    private TableViewColumnHeader? _hoverHeader;

    /// <summary>Creates a controller.</summary>
    /// <param name="canResize">Reports whether a resize may start.</param>
    /// <param name="headers">The cache of realized headers and grips, used for hit testing.</param>
    /// <param name="resolveHoverKey">Maps a hit divider to the key of the header that should show the hover highlight.</param>
    /// <param name="setHoverHeader">Applies the resize-hover class to the given header, or clears it when null.</param>
    public ColumnResizeController(
        Func<bool> canResize,
        ResizeHeaderCache headers,
        Func<string, bool, string?> resolveHoverKey,
        Action<TableViewColumnHeader?> setHoverHeader)
    {
        _canResize = canResize ?? throw new ArgumentNullException(nameof(canResize));
        _headers = headers ?? throw new ArgumentNullException(nameof(headers));
        _resolveHoverKey = resolveHoverKey ?? throw new ArgumentNullException(nameof(resolveHoverKey));
        _setHoverHeader = setHoverHeader ?? throw new ArgumentNullException(nameof(setHoverHeader));
    }

    /// <summary>Raised while a resize gesture is in progress, with the horizontal delta since the last report.</summary>
    public event EventHandler<ColumnResizeRequestedEventArgs>? ResizeRequested;

    /// <summary>Raised when a resize gesture starts or ends so the owner can toggle a visual class.</summary>
    public event EventHandler<bool>? IsResizingChanged;

    /// <summary>Gets a value indicating whether a resize gesture is in progress.</summary>
    public bool IsResizing => _pointer is not null;

    /// <summary>Gets the key of the column being resized, or null when no gesture is in progress.</summary>
    public string? ResizingColumnKey => _columnKey;

    /// <summary>Attempts to begin a resize gesture at <paramref name="position"/>.</summary>
    /// <returns>True when a gesture started and the caller should capture the pointer.</returns>
    public bool TryBegin(Point position, IPointer pointer, out string? columnKey)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        columnKey = null;
        if (!_canResize())
            return false;

        // A second press while a gesture is active must not replace the column being resized
        // or fall through to header reorder and selection handling.
        if (IsResizing)
            return false;

        DividerHit hit = HitTest(position);
        if (hit.Key is null)
            return false;

        _pointer = pointer;
        _columnKey = hit.Key;
        _lastX = position.X;
        columnKey = hit.Key;
        IsResizingChanged?.Invoke(this, true);
        return true;
    }

    /// <summary>Reports a pointer move, emitting a resize delta when a gesture is active.</summary>
    /// <returns>True when the move was consumed by the resize gesture.</returns>
    public bool TryMove(IPointer pointer, Point position)
    {
        if (!ReferenceEquals(_pointer, pointer) || _columnKey is not { } key)
            return false;

        double currentX = position.X;
        ResizeRequested?.Invoke(this, new(key, currentX - _lastX));
        _lastX = currentX;
        return true;
    }

    /// <summary>Ends a gesture for <paramref name="pointer"/>.</summary>
    /// <returns>True when the gesture belonged to that pointer and was ended.</returns>
    public bool TryEnd(IPointer pointer)
    {
        if (!ReferenceEquals(_pointer, pointer))
            return false;

        Reset();
        return true;
    }

    /// <summary>Ends any gesture, for example when the pointer capture is lost.</summary>
    /// <returns>True when a gesture was active.</returns>
    public bool Cancel()
    {
        if (_pointer is null)
            return false;

        Reset();
        return true;
    }

    /// <summary>Updates the hover highlight for a pointer position over the table.</summary>
    public void UpdateHover(Point position)
    {
        DividerHit hit = HitTest(position);
        if (hit.Key is null)
        {
            SetHover(null);
            return;
        }

        string? hoverKey = _resolveHoverKey(hit.Key, hit.IsRightHalf);
        SetHover(hoverKey is null ? null : _headers.Find(hoverKey));
    }

    /// <summary>Clears the hover highlight when no gesture is active.</summary>
    public void ClearHoverWhenIdle()
    {
        if (_pointer is null)
            SetHover(null);
    }

    /// <summary>
    /// Hit-tests a divider, refreshing the header cache once when a cached grip has left the visual tree.
    /// </summary>
    private DividerHit HitTest(Point position)
    {
        DividerHit hit = _headers.HitTest(position);
        if (hit.Key is not null || !_headers.WasStale)
            return hit;

        // The cached headers can go stale when Avalonia recreates the column header visuals,
        // for example after the native columns are rebuilt. Refresh once and retry so the
        // resize gesture keeps working against the live headers.
        _headers.Refresh();
        return _headers.HitTest(position);
    }

    private void SetHover(TableViewColumnHeader? header)
    {
        if (ReferenceEquals(_hoverHeader, header))
            return;

        _hoverHeader = header;
        _setHoverHeader(header);
    }

    private void Reset()
    {
        _pointer = null;
        _columnKey = null;
        IsResizingChanged?.Invoke(this, false);
    }
}
