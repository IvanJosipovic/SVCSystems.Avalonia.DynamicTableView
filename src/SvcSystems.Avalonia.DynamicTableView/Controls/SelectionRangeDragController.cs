namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Owns the selection snapshot and range updates for a row-drag selection gesture.</summary>
internal sealed class SelectionRangeDragController
{
    private int _anchor = -1;
    private int _last = -1;
    private bool _select;
    private HashSet<int>? _initialSelection;

    /// <summary>Gets whether a valid row press is currently being tracked.</summary>
    public bool IsActive => _anchor >= 0;

    /// <summary>Begins tracking a row press and snapshots the current selection when available.</summary>
    public void Begin(int anchor, ISelectionModel? selection, bool controlModifierPressed)
    {
        _anchor = anchor;
        if (anchor < 0 || selection is null)
            return;

        _last = anchor;
        _initialSelection = [.. selection.SelectedIndexes];
        _select = !controlModifierPressed || !selection.IsSelected(anchor);
    }

    /// <summary>Updates the selected range and reports whether the pointer move was consumed.</summary>
    public bool TryUpdate(ISelectionModel? selection, int target)
    {
        if (_anchor < 0 || target < 0 || target == _last)
            return false;

        Update(selection, target);
        return true;
    }

    /// <summary>Ends the gesture and releases its selection snapshot.</summary>
    public void Reset()
    {
        _anchor = -1;
        _last = -1;
        _initialSelection = null;
    }

    private void Update(ISelectionModel? selection, int targetIndex)
    {
        if (selection is null || _initialSelection is not { } initialSelection)
            return;

        if (selection.SingleSelect)
        {
            selection.Select(targetIndex);
            _last = targetIndex;
            return;
        }

        int previousStart = Math.Min(_anchor, _last);
        int previousEnd = Math.Max(_anchor, _last);
        int targetStart = Math.Min(_anchor, targetIndex);
        int targetEnd = Math.Max(_anchor, targetIndex);

        selection.BeginBatchUpdate();
        try
        {
            SetRange(selection, previousStart, Math.Min(previousEnd, targetStart - 1), initialSelection, applyDragState: false);
            SetRange(selection, Math.Max(previousStart, targetEnd + 1), previousEnd, initialSelection, applyDragState: false);
            SetRange(selection, targetStart, targetEnd, initialSelection, applyDragState: true);
            _last = targetIndex;
        }
        finally
        {
            selection.EndBatchUpdate();
        }
    }

    private void SetRange(
        ISelectionModel selection,
        int start,
        int end,
        HashSet<int> initialSelection,
        bool applyDragState)
    {
        for (int index = start; index <= end; index++)
        {
            bool shouldBeSelected = applyDragState
                ? _select
                : initialSelection.Contains(index);
            if (selection.IsSelected(index) == shouldBeSelected)
                continue;
            if (shouldBeSelected)
                selection.Select(index);
            else
                selection.Deselect(index);
        }
    }
}
