using System.Collections.ObjectModel;
using System.Reactive.Concurrency;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class IdentityPreservingSelectionModelTests
{
    [Fact]
    public void Move_notifications_preserve_selected_item_identities()
    {
        ObservableCollection<TestRow> rows =
        [
            new(1),
            new(2),
            new(3),
            new(4)
        ];
        using IdentityPreservingSelectionModel<TestRow, int> model = new(
            static row => row.Id,
            ImmediateScheduler.Instance,
            ImmediateScheduler.Instance);
        model.SetIdentitySource(rows);
        model.SingleSelect = false;
        model.Select(1);
        model.Select(3);

        rows.Move(1, 3);

        Assert.Equal([2, 3], model.SelectedIndexes);
        Assert.Equal([4, 2], model.SelectedItems.Cast<TestRow>().Select(static row => row.Id));
    }

    private sealed record TestRow(int Id);
}
