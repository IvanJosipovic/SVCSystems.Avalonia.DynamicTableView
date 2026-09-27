using Avalonia.Metadata;

namespace SvcSystems.Avalonia.DynamicTableView.Sample;

public sealed class SampleColumnDefinitions
{
    [Content]
    public List<SampleColumnDefinition> Columns { get; } = [];

    public IEnumerable<DynamicTableViewColumn<SampleRow>> CreateColumns()
        => Columns.Select(static definition => definition.CreateColumn());
}
