using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;
using Microsoft.Reactive.Testing;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewSourceChangeTests
{
    [Fact]
    public void Source_cache_updates_bound_rows_without_modifying_the_cache()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        using var source = DynamicTableViewTestData.CreateSource(cache);

        Assert.Equal(["a", "b", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        var added = new DynamicTableViewTestRow("d", "Delta", 40, DateTimeOffset.UnixEpoch, false, DynamicTableViewTestState.Pending);
        cache.AddOrUpdate(added);
        Assert.Equal(["a", "b", "c", "d"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Same(added, cache.Lookup("d").Value);

        cache.AddOrUpdate(cache.Lookup("b").Value with { Name = "Beta 2" });
        Assert.Equal("Beta 2", source.Items.Cast<DynamicTableViewTestRow>().Single(static row => row.Id == "b").Name);
        cache.RemoveKey("a");
        Assert.Equal(["b", "c", "d"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Accepts_read_only_observable_cache_interface()
    {
        using SourceCache<DynamicTableViewTestRow, string> owner = new(static row => row.Id);
        IObservableCache<DynamicTableViewTestRow, string> cache = owner;
        using DynamicTableViewSource<DynamicTableViewTestRow, string> source = new(
            cache, static row => row.Id, workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: ImmediateScheduler.Instance);

        var row = DynamicTableViewTestData.CreateRows()[0];
        owner.AddOrUpdate(row);

        Assert.Same(row, Assert.Single(source.Items.Cast<DynamicTableViewTestRow>()));
    }

    [Fact]
    public void Disposing_source_leaves_caller_cache_usable()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using (var source = DynamicTableViewTestData.CreateSource(cache))
            cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);

        var next = DynamicTableViewTestData.CreateRows()[1];
        cache.AddOrUpdate(next);
        Assert.Same(next, cache.Lookup(next.Id).Value);
    }

    [Fact]
    public void Query_changes_wait_for_worker_before_recomputing_bound_rows()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler worker = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache, workerScheduler: worker, uiScheduler: ImmediateScheduler.Instance);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        worker.RunUntilIdle();

        source.SetFilter(new("age", DynamicTableViewFilterOperator.GreaterThan, 15));
        Assert.Equal(3, source.Items.Cast<DynamicTableViewTestRow>().Count());
        worker.RunUntilIdle();
        Assert.Equal(2, source.Items.Cast<DynamicTableViewTestRow>().Count());

        source.SetSort([new("age", ListSortDirection.Descending)]);
        Assert.Equal("b", source.Items.Cast<DynamicTableViewTestRow>().First().Id);
        worker.RunUntilIdle();
        Assert.Equal("c", source.Items.Cast<DynamicTableViewTestRow>().First().Id);
    }

    [Fact]
    public void Value_type_key_identity_restores_replaced_row_without_changing_selection()
    {
        using SourceCache<DynamicTableViewTestRow, int> cache = new(static row => row.Age);
        using DynamicTableViewSource<DynamicTableViewTestRow, int> source = new(
            cache, static row => row.Age, workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: ImmediateScheduler.Instance);
        var original = DynamicTableViewTestData.CreateRows()[1];
        cache.AddOrUpdate(original);
        source.SelectionModel.Select(0);

        var replacement = original with { Name = "Updated" };
        cache.AddOrUpdate(replacement);

        Assert.Same(replacement, Assert.Single(source.SelectionModel.SelectedItems));
        Assert.True(source.AreSameRows(original, replacement));
    }

    [Fact]
    public void Selection_identity_scan_runs_on_worker_after_ui_source_snapshot()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler worker = new();
        ManualScheduler ui = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache, workerScheduler: worker, uiScheduler: ui);
        var original = DynamicTableViewTestData.CreateRows()[1];
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        worker.RunUntilIdle();
        ui.RunUntilIdle();
        source.SelectionModel.Select(1);

        var replacement = original with { Name = "Updated" };
        cache.AddOrUpdate(replacement);
        worker.RunUntilIdle();
        ui.RunUntilIdle();

        Assert.True(worker.PendingCount > 0);
        worker.RunUntilIdle();
        ui.RunUntilIdle();

        Assert.Same(replacement, Assert.Single(source.SelectionModel.SelectedItems));
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
    public void Explicit_clear_during_pending_restore_does_not_restore_old_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);
        source.SelectionModel.Clear();

        uiScheduler.RunUntilIdle();

        Assert.Empty(source.SelectionModel.SelectedItems);
    }

    [Fact]
    public void Deselect_during_pending_restore_keeps_the_row_deselected()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);

        source.SelectionModel.Deselect(0);
        uiScheduler.RunUntilIdle();

        Assert.Empty(source.SelectionModel.SelectedItems);
    }

    [Fact]
    public void Deselect_range_during_pending_restore_preserves_other_selected_rows()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1, 2);

        source.SelectionModel.DeselectRange(0, 0);
        uiScheduler.RunUntilIdle();

        Assert.Equal(["c"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Select_range_during_pending_restore_replaces_it_with_the_requested_range()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);

        source.SelectionModel.SelectRange(0, 2);
        uiScheduler.RunUntilIdle();

        Assert.Equal(["a", "b", "c"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Select_all_during_pending_restore_replaces_it_with_all_visible_rows()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);

        source.SelectionModel.SelectAll();
        uiScheduler.RunUntilIdle();

        Assert.Equal(["a", "b", "c", "d"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Selected_index_during_pending_restore_replaces_the_previous_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);

        source.SelectionModel.SelectedIndex = 2;
        uiScheduler.RunUntilIdle();

        Assert.Equal(["c"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Selected_item_during_pending_restore_replaces_the_previous_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);
        var replacement = source.Items.Cast<DynamicTableViewTestRow>().Single(static row => row.Id == "c");

        source.SelectionModel.SelectedItem = replacement;
        uiScheduler.RunUntilIdle();

        Assert.Equal(["c"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Begin_batch_update_includes_pending_restore_in_selection_notifications()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        QueueMovedSelectionRestore(cache, source, uiScheduler, 1);
        var selectionChangedCount = 0;
        source.SelectionModel.SelectionChanged += (_, _) => selectionChangedCount++;

        source.SelectionModel.BeginBatchUpdate();
        try
        {
            Assert.Equal(0, selectionChangedCount);
        }
        finally
        {
            source.SelectionModel.EndBatchUpdate();
        }

        Assert.Equal(1, selectionChangedCount);
        Assert.Equal(["b"], GetSelectedIds(source.SelectionModel.SelectedItems));
        uiScheduler.RunUntilIdle();
        Assert.Equal(["b"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Changing_to_single_select_keeps_only_the_currently_selected_identity_after_updates()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Count() == 3);
        source.SelectionModel.Select(0);
        source.SelectionModel.Select(1);
        source.SelectionModel.SingleSelect = true;
        Assert.Equal(["a"], GetSelectedIds(source.SelectionModel.SelectedItems));

        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0] with { Age = 40, Name = "Moved Alpha" });
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Any(static row => row.Name == "Moved Alpha"));
        uiScheduler.RunUntilIdle();

        Assert.Equal(["a"], GetSelectedIds(source.SelectionModel.SelectedItems));
    }

    [Fact]
    public void Disposing_source_cancels_a_queued_selection_restore()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        DynamicTableViewSource<DynamicTableViewTestRow, string> source =
            DynamicTableViewTestData.CreateSource(cache, uiScheduler: uiScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Count() == 3);
        source.SelectionModel.Select(1);

        var updatedRow = DynamicTableViewTestData.CreateRows()[1] with { Age = 5, Name = "Moved Beta" };
        cache.AddOrUpdate(updatedRow);
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>()
            .Any(row => ReferenceEquals(row, updatedRow)));
        string[] selectionAtDisposal = GetSelectedIds(source.SelectionModel.SelectedItems);

        source.Dispose();
        uiScheduler.RunUntilIdle();

        Assert.Equal(selectionAtDisposal, GetSelectedIds(source.SelectionModel.SelectedItems));
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
    public void Cache_replace_add_and_remove_keep_only_existing_selected_identities()
    {
        var initialRows = CreateIndexedRows(10);
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(initialRows);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        source.SelectionModel.Select(1);
        source.SelectionModel.Select(4);
        source.SelectionModel.Select(8);

        var replacement = initialRows[1] with { Name = "Replacement" };
        cache.AddOrUpdate(replacement);
        Assert.Equal(["row-01", "row-04", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        Assert.Contains(source.SelectionModel.SelectedItems, selected => ReferenceEquals(selected, replacement));

        cache.AddOrUpdate(new DynamicTableViewTestRow("row-10", "Row 10", 10, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
        Assert.Equal(["row-01", "row-04", "row-08"], GetSelectedIds(source.SelectionModel.SelectedItems));
        cache.RemoveKey("row-04");

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

    private static void QueueMovedSelectionRestore(
        SourceCache<DynamicTableViewTestRow, string> cache,
        DynamicTableViewSource<DynamicTableViewTestRow, string> source,
        ManualScheduler uiScheduler,
        params int[] selectedIndexes)
    {
        source.SetSort([new("age", ListSortDirection.Ascending)]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Count() == 3);

        foreach (var index in selectedIndexes)
            source.SelectionModel.Select(index);

        var movedRow = DynamicTableViewTestData.CreateRows()[1] with { Age = 5, Name = "Moved Beta" };
        var addedRow = new DynamicTableViewTestRow(
            "d", "Delta", 40, DateTimeOffset.UnixEpoch, false, DynamicTableViewTestState.Pending);
        cache.Edit(updater =>
        {
            updater.AddOrUpdate(movedRow);
            updater.AddOrUpdate(addedRow);
        });
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>()
            .Any(row => ReferenceEquals(row, addedRow)));
    }

    private static string[] GetSelectedIds(IEnumerable<object?> selectedItems)
        => selectedItems.Cast<DynamicTableViewTestRow>()
            .Select(static row => row.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed class ManualScheduler : IScheduler
    {
        private readonly Queue<Action> _actions = new();

        public int PendingCount => _actions.Count;

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
