namespace SvcSystems.Avalonia.DynamicTableView;

using System.ComponentModel;

internal sealed partial class DynamicTableViewCell : ContentControl
{
    private readonly DynamicTableViewColumn _column;
    private INotifyPropertyChanged? _observedItem;

    public DynamicTableViewCell(DynamicTableViewColumn column)
    {
        _column = column ?? throw new ArgumentNullException(nameof(column));
        ContentTemplate = column.CellTemplate;
        Classes.Add("dynamic-table-view-cell");
        DataContextChanged += OnDataContextChanged;
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateObservedItem();
        UpdateText();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromObservedItem();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateText()
    {
        var item = DataContext;
        var text = item is not null ? _column.GetDisplayValue(item) : string.Empty;
        Content = _column.CellTemplate is null ? text : item;
        ToolTip.SetTip(this, text);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UpdateObservedItem();
        UpdateText();
    }

    private void UpdateObservedItem()
    {
        UnsubscribeFromObservedItem();
        if (VisualRoot is null)
            return;

        _observedItem = DataContext as INotifyPropertyChanged;
        if (_observedItem is not null)
            _observedItem.PropertyChanged += ObservedItemOnPropertyChanged;
    }

    private void UnsubscribeFromObservedItem()
    {
        if (_observedItem is not null)
            _observedItem.PropertyChanged -= ObservedItemOnPropertyChanged;
        _observedItem = null;
    }

    private void ObservedItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => UpdateText();
}
