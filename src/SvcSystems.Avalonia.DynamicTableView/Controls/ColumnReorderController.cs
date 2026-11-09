namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Owns the drag state and source-column move for a header reorder gesture.</summary>
internal sealed class ColumnReorderController
{
    private int _pressedColumn = -1;
    private Point _pressedPosition;
    private bool _dragStarted;

    /// <summary>Gets whether a valid column is currently pressed for a possible reorder.</summary>
    public bool HasPressedColumn => _pressedColumn >= 0;

    /// <summary>Begins tracking a possible reorder from a native column index.</summary>
    public void Begin(int columnIndex, Point position)
    {
        _pressedColumn = columnIndex;
        _pressedPosition = position;
        _dragStarted = false;
    }

    /// <summary>Starts the reorder after the pointer crosses the existing horizontal threshold.</summary>
    public bool TryStart(Point position)
    {
        if (!HasPressedColumn)
            return false;

        if (!_dragStarted && Math.Abs(position.X - _pressedPosition.X) < 5)
            return false;

        _dragStarted = true;
        return true;
    }

    /// <summary>Moves the pressed source column to <paramref name="targetIndex"/> when the target differs.</summary>
    public bool TryMoveTo(int targetIndex, ObservableCollection<DynamicTableViewColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (!HasPressedColumn || targetIndex < 0 || targetIndex == _pressedColumn)
            return false;

        columns.Move(_pressedColumn, targetIndex);
        _pressedColumn = targetIndex;
        return true;
    }

    /// <summary>Stops tracking the current pointer press.</summary>
    public void Reset()
    {
        _pressedColumn = -1;
        _dragStarted = false;
    }
}
