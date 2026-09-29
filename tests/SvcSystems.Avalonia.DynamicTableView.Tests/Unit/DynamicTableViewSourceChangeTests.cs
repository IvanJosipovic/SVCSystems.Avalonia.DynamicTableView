using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;
using Microsoft.Reactive.Testing;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewSourceChangeTests
{
    [Fact]
    public void Observable_collection_factory_tracks_add_replace_remove_and_move()
    {
        var items = new ObservableCollection<DynamicTableViewTestRow>(DynamicTableViewTestData.CreateRows());
        using var source =
            DynamicTableViewSource<DynamicTableViewTestRow, string>.FromObservableCollection(
                items,
                static row => row.Id,
                DynamicTableViewTestData.CreateColumns(),
                workerScheduler: ImmediateScheduler.Instance,
                uiScheduler: ImmediateScheduler.Instance);

        Assert.Equal(["a", "b", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        items.Add(new("d", "Delta", 40, DateTimeOffset.UnixEpoch, false, DynamicTableViewTestState.Pending));
        Assert.Equal(["a", "b", "c", "d"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

        items[1] = items[1] with { Name = "Beta 2" };
        Assert.Equal("Beta 2", source.Items.Cast<DynamicTableViewTestRow>().Single(static row => row.Id == "b").Name);

        items.RemoveAt(0);
        Assert.Equal(["b", "c", "d"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        items.Move(2, 0);
        Assert.Equal(["b", "c", "d"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Connect_updates_bound_rows_on_add_update_and_remove()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        var original = DynamicTableViewTestData.CreateRows()[0];
        cache.AddOrUpdate(original);
        Assert.Equal(["a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

        var updated = original with { Name = "Updated" };
        cache.AddOrUpdate(updated);
        Assert.Equal("Updated", Assert.Single(source.Items.Cast<DynamicTableViewTestRow>()).Name);

        cache.RemoveKey(updated.Id);
        Assert.Empty(source.Items);
    }

    [Fact]
    public void User_selection_during_pending_restore_is_preserved_after_add_and_update()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Count() == 3);

        source.SelectionModel.Select(0);
        source.SelectionModel.Select(1);
        Assert.Equal(["a", "b"], GetSelectedIds(source.SelectionModel.SelectedItems));

        var updatedRow = DynamicTableViewTestData.CreateRows()[1] with { Name = "Updated Beta" };
        var addedRow = new DynamicTableViewTestRow(
            "d", "Delta", 40, DateTimeOffset.UnixEpoch, false, DynamicTableViewTestState.Pending);
        cache.Edit(updater =>
        {
            updater.AddOrUpdate(updatedRow);
            updater.AddOrUpdate(addedRow);
        });

        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Any(row => ReferenceEquals(row, addedRow)));
        var addedRowIndex = source.Items.Cast<DynamicTableViewTestRow>().ToList()
            .FindIndex(row => row.Id == addedRow.Id);
        source.SelectionModel.Select(addedRowIndex);
        Assert.Contains(source.SelectionModel.SelectedItems, item =>
            item is DynamicTableViewTestRow row && row.Id == addedRow.Id);

        uiScheduler.RunUntilIdle();

        Assert.Equal(["a", "b", "d"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Dynamic_columns_raise_changed_and_update_search_and_filter_behavior()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        var changes = 0;
        source.Changed += (_, _) => changes++;
        var idColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "id", "Identifier", static row => row.Id);

        source.Columns.Add(idColumn);
        source.SearchText = "c";

        Assert.True(changes >= 2);
        Assert.Equal(["c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Filtering_out_selected_row_clears_selection_and_clearing_filter_does_not_restore_it()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.AdvanceBy(100);
        source.SelectionModel.Select(1);
        Assert.Equal("b", (source.SelectionModel.SelectedItem as DynamicTableViewTestRow)?.Id);
        source.SetFilter(new("name", DynamicTableViewFilterOperator.Equals, "Alpha"));
        uiScheduler.AdvanceBy(100);
        Assert.Empty(source.SelectionModel.SelectedItems.Where(static item => item is not null));

        source.ClearFilters();
        uiScheduler.AdvanceBy(100);

        Assert.Equal(["a", "b", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Empty(source.SelectionModel.SelectedItems);
    }

    [Fact]
    public void Explicit_clear_discards_selection_hidden_by_filter()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.AdvanceBy(100);
        source.SelectionModel.Select(1);

        source.SetFilter(new("name", DynamicTableViewFilterOperator.Equals, "Alpha"));
        uiScheduler.AdvanceBy(1);
        source.SelectionModel.Clear();

        source.ClearFilters();
        uiScheduler.AdvanceBy(1);

        Assert.Empty(source.SelectionModel.SelectedItems);
    }

    [Fact]
    public void Filtering_some_multi_selected_rows_keeps_only_visible_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.AdvanceBy(100);
        source.SelectionModel.Select(1);
        source.SelectionModel.Select(2);

        source.SetFilter(new("name", DynamicTableViewFilterOperator.Equals, "Gamma"));
        uiScheduler.AdvanceBy(100);

        Assert.Equal("c", Assert.Single(source.SelectionModel.SelectedItems) is DynamicTableViewTestRow selected ? selected.Id : null);

        source.ClearFilters();
        uiScheduler.AdvanceBy(100);

        Assert.Equal("c", Assert.Single(source.SelectionModel.SelectedItems) is DynamicTableViewTestRow restored ? restored.Id : null);
    }

    [Fact]
    public void Selection_identity_survives_source_replacement_and_sort_movement()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.AdvanceBy(100);
        source.SelectionModel.Select(1);

        source.SetSort([new("age", ListSortDirection.Ascending)]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[1] with { Age = 5 });
        uiScheduler.AdvanceBy(100);

        Assert.Equal(["b", "a", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Equal("b", Assert.Single(source.SelectionModel.SelectedItems) is DynamicTableViewTestRow selected ? selected.Id : null);
    }

    [Fact]
    public void Multi_selection_survives_large_incremental_sorted_update()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        var rows = Enumerable.Range(0, 400)
            .Select(index => new DynamicTableViewTestRow($"row-{index:D3}", $"Row {index}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready))
            .ToArray();
        cache.AddOrUpdate(rows);
        uiScheduler.AdvanceBy(100);
        source.SetSort([new("age", ListSortDirection.Ascending)]);
        source.SelectionModel.SelectAll();

        cache.AddOrUpdate(rows[200] with { Age = -1, Name = "Moved to start" });
        uiScheduler.AdvanceBy(100);

        Assert.Equal("row-200", ((DynamicTableViewTestRow)source.Items.Cast<object>().First()).Id);
        Assert.Equal(400, source.SelectionModel.SelectedItems.Count);
        Assert.Equal(400, source.SelectionModel.SelectedIndexes.Count);
    }

    [Fact]
    public void Ten_selected_items_keep_identity_when_half_are_updated_and_moved_by_sort()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache,
            uiScheduler: uiScheduler);
        var rows = CreateIndexedRows(10);
        cache.AddOrUpdate(rows);
        uiScheduler.AdvanceBy(100);
        source.SetSort([new("age", ListSortDirection.Ascending)]);
        source.SelectionModel.SelectAll();

        cache.AddOrUpdate(rows.Take(5).Select(row => row with { Age = row.Age + 20, Name = $"Updated {row.Name}" }));
        uiScheduler.AdvanceBy(100);

        Assert.Equal(Enumerable.Range(5, 5).Select(static index => $"row-{index:D2}")
                .Concat(Enumerable.Range(0, 5).Select(static index => $"row-{index:D2}")),
            source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Equal(Enumerable.Range(0, 10).Select(static index => $"row-{index:D2}").Order(StringComparer.Ordinal),
            GetSelectedIds(source.SelectionModel.SelectedItems));
        Assert.Equal(10, source.SelectionModel.SelectedItems.Count);

        cache.AddOrUpdate(new DynamicTableViewTestRow("row-10", "Row 10", -1, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
        uiScheduler.AdvanceBy(100);
        Assert.Equal(10, source.SelectionModel.SelectedItems.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(static index => $"row-{index:D2}").Order(StringComparer.Ordinal),
            GetSelectedIds(source.SelectionModel.SelectedItems));

        cache.RemoveKey("row-07");
        uiScheduler.AdvanceBy(100);

        Assert.Equal(Enumerable.Range(0, 10).Where(static index => index != 7).Select(static index => $"row-{index:D2}").Order(StringComparer.Ordinal),
            GetSelectedIds(source.SelectionModel.SelectedItems));
        Assert.Equal(9, source.SelectionModel.SelectedItems.Count);
    }

    [Fact]
    public void Observable_collection_replace_move_add_and_remove_keep_only_existing_selected_identities()
    {
        var initialRows = CreateIndexedRows(10);
        ObservableCollection<DynamicTableViewTestRow> items = new(initialRows);
        using var source = DynamicTableViewSource<DynamicTableViewTestRow, string>.FromObservableCollection(
            items,
            static row => row.Id,
            DynamicTableViewTestData.CreateColumns(),
            workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: ImmediateScheduler.Instance);
        source.SelectionModel.Select(1);
        source.SelectionModel.Select(4);
        source.SelectionModel.Select(8);

        var replacement = initialRows[1] with { Name = "Replacement" };
        items[1] = replacement;
        Assert.Equal(["row-01", "row-04", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        Assert.Contains(source.SelectionModel.SelectedItems, selected => ReferenceEquals(selected, replacement));

        items.Move(8, 0);
        Assert.Equal(["row-01", "row-04", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        items.Add(new("row-10", "Row 10", 10, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
        Assert.Equal(["row-01", "row-04", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        items.Remove(items.Single(static row => row.Id == "row-04"));

        Assert.Equal(["row-01", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        Assert.Contains(source.SelectionModel.SelectedItems, selected => ReferenceEquals(selected, replacement));
        Assert.Equal(2, source.SelectionModel.SelectedItems.Count);
    }

    [Fact]
    public void Search_hiding_selected_rows_clears_them_and_clearing_search_does_not_restore_them()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        var rows = CreateIndexedRows(10);
        cache.AddOrUpdate(rows.Select((row, index) => row with { Name = index < 2 ? $"Needle {row.Name}" : row.Name }));
        source.SelectionModel.Select(0);
        source.SelectionModel.Select(2);
        source.SelectionModel.Select(4);

        source.SearchText = "Needle";

        Assert.Equal(["row-00", "row-01"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Equal(["row-00"], GetSelectedIds(source.SelectionModel.SelectedItems));

        source.SearchText = string.Empty;

        Assert.Equal(10, source.Items.Cast<DynamicTableViewTestRow>().Count());
        Assert.Equal(["row-00"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Search_filter_sort_and_item_update_clear_selection_when_selected_row_leaves_view()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        var rows = CreateIndexedRows(10);
        cache.AddOrUpdate(rows);
        uiScheduler.AdvanceBy(100);
        source.SetSort([new("age", ListSortDirection.Descending)]);
        uiScheduler.AdvanceBy(100);
        source.SelectionModel.SelectAll();

        source.SetFilter(new("enabled", DynamicTableViewFilterOperator.IsTrue));
        uiScheduler.AdvanceBy(100);
        Assert.Equal(5, source.Items.Cast<DynamicTableViewTestRow>().Count());
        Assert.Equal(5, source.SelectionModel.SelectedItems.Count);

        source.SearchText = "Row 08";
        uiScheduler.AdvanceBy(100);
        Assert.Equal(["row-08"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Equal(["row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));

        cache.AddOrUpdate(rows[8] with { Enabled = false, Age = -1 });
        uiScheduler.AdvanceBy(100);

        Assert.Empty(source.Items);
        Assert.Empty(source.SelectionModel.SelectedItems);

        source.ClearFilters();
        source.SearchText = string.Empty;
        uiScheduler.AdvanceBy(100);

        Assert.Equal(10, source.Items.Cast<DynamicTableViewTestRow>().Count());
        Assert.Empty(source.SelectionModel.SelectedItems);
    }

    [Fact]
    public void Disposing_source_stops_consuming_changes_and_disposes_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var source = DynamicTableViewTestData.CreateSource(cache);
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.SetSort([]));
    }

    private static DynamicTableViewTestRow[] CreateIndexedRows(int count)
        => Enumerable.Range(0, count)
            .Select(index => new DynamicTableViewTestRow(
                $"row-{index:D2}", $"Row {index:D2}", index, DateTimeOffset.UnixEpoch,
                index % 2 == 0, DynamicTableViewTestState.Ready))
            .ToArray();

    private static string[] GetSelectedIds(IEnumerable<object?> selectedItems)
        => selectedItems.Cast<DynamicTableViewTestRow>()
            .Select(static row => row.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed class ManualScheduler : IScheduler
    {
        private readonly Queue<Action> _actions = new();

        public DateTimeOffset Now => DateTimeOffset.UtcNow;

        public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public IDisposable Schedule<TState>(TState state, TimeSpan dueTime,
            Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime,
            Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public void RunUntil(Func<bool> condition)
        {
            var steps = 0;
            while (!condition())
            {
                Assert.True(_actions.Count > 0, "Scheduler queue emptied before expected source state arrived.");
                RunNext();
                Assert.True(++steps < 1000, "Scheduler exceeded expected work limit.");
            }
        }

        public void RunUntilIdle()
        {
            var steps = 0;
            while (_actions.Count > 0)
            {
                RunNext();
                Assert.True(++steps < 1000, "Scheduler exceeded expected work limit.");
            }
        }

        private IDisposable Enqueue(Func<IDisposable> action)
        {
            _actions.Enqueue(() => action().Dispose());
            return Disposable.Empty;
        }

        private void RunNext()
            => _actions.Dequeue()();
    }
}
