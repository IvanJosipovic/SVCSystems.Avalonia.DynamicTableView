namespace SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

public sealed record DynamicTableViewTestRow(
    string Id,
    string Name,
    int Age,
    DateTimeOffset CreatedAt,
    bool Enabled,
    DynamicTableViewTestState State);
