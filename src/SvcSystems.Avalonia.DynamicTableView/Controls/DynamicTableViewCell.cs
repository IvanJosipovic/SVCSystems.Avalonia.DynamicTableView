namespace SvcSystems.Avalonia.DynamicTableView;

using System.ComponentModel;

internal sealed partial class DynamicTableViewCell : ContentControl
{
    private readonly DynamicTableView _table;
    private readonly DynamicTableViewColumn _column;
    private TextBlock? _defaultTextBlock;
    private INotifyPropertyChanged? _observedItem;

    public DynamicTableViewCell(DynamicTableView table, DynamicTableViewColumn column)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _column = column ?? throw new ArgumentNullException(nameof(column));
        ContentTemplate = column.CellTemplate ?? new FuncDataTemplate<string>(
            (text, _) =>
            {
                TextBlock textBlock = new() { Text = text };
                _defaultTextBlock = textBlock;
                ApplyDefaultTextOptions();
                return textBlock;
            },
            supportsRecycling: true);
        Classes.Add("dynamic-table-view-cell");
        DataContextChanged += OnDataContextChanged;
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _column.CellTextOptionsChanged += ColumnOnCellTextOptionsChanged;
        UpdateObservedItem();
        UpdateText();
        UpdateDefaultTextOptions();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromObservedItem();
        _column.CellTextOptionsChanged -= ColumnOnCellTextOptionsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateText()
    {
        var item = DataContext;
        var text = item is not null ? _column.GetDisplayValue(item) : string.Empty;
        Content = _column.CellTemplate is null ? text : item;
        if (_column.CellTemplate is null && _defaultTextBlock is not null)
            _defaultTextBlock.Text = text;
        ToolTip.SetTip(this, text);
    }

    internal void UpdateDefaultTextOptions()
    {
        if (_defaultTextBlock is not null)
            ApplyDefaultTextOptions();
    }

    private void ApplyDefaultTextOptions()
    {
        if (_column.CellTemplate is not null || _defaultTextBlock is null)
            return;
        _defaultTextBlock.TextWrapping = _column.CellTextWrapping ?? _table.CellTextWrapping;
        _defaultTextBlock.TextTrimming = _column.CellTextTrimming ?? _table.CellTextTrimming ?? TextTrimming.None;
        _defaultTextBlock.MaxLines = _column.CellMaxLines ?? _table.CellMaxLines;
    }

    private void ColumnOnCellTextOptionsChanged(object? sender, EventArgs e)
        => UpdateDefaultTextOptions();

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
