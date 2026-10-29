using System.ComponentModel;
using System.Reactive.Concurrency;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;
using Microsoft.Reactive.Testing;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewSourceQueryTests
{
    [Fact]
    public void Column_query_notifications_follow_source_membership_and_disposal()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        using var first = DynamicTableViewTestData.CreateSource(cache, [column]);
        using var second = DynamicTableViewTestData.CreateSource(cache, [column]);
        first.SetSort([new("name", ListSortDirection.Ascending)]);
        second.SetSort([new("name", ListSortDirection.Ascending)]);
        first.Columns.Clear();
        column.CanUserSort = false;
        Assert.Single(first.SortDescriptors);
        Assert.Empty(second.SortDescriptors);
        second.Dispose();
        column.CanUserSort = true;
        column.IsSearchable = false;
        column.CanUserFilter = false;
    }

    [Fact]
    public void Disabling_column_capabilities_removes_only_that_columns_active_query()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow>[] columns = DynamicTableViewTestData.CreateColumns();
        using var source = DynamicTableViewTestData.CreateSource(cache, columns);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        source.SetSort([new("name", ListSortDirection.Descending), new("age", ListSortDirection.Ascending)]);
        source.SetFilter(new("name", DynamicTableViewFilterOperator.Equals, "Alpha"));
        source.SetFilter(new("enabled", DynamicTableViewFilterOperator.IsTrue));
        source.SetCustomFilter("name", static _ => false);
        Assert.Empty(source.Items.Cast<DynamicTableViewTestRow>());

        columns[0].CanUserSort = false;
        columns[0].CanUserFilter = false;

        Assert.Equal("age", Assert.Single(source.SortDescriptors).ColumnKey);
        Assert.Equal("enabled", Assert.Single(source.FilterDescriptors).ColumnKey);
        Assert.Equal(2, source.Items.Cast<DynamicTableViewTestRow>().Count());
        columns[0].CanUserSort = true;
        columns[0].CanUserFilter = true;
        Assert.Equal("age", Assert.Single(source.SortDescriptors).ColumnKey);
        Assert.Equal("enabled", Assert.Single(source.FilterDescriptors).ColumnKey);
    }

    [Fact]
    public void Changing_searchability_refreshes_active_search()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        using var source = DynamicTableViewTestData.CreateSource(cache, [name]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        source.SearchText = "Alpha";
        Assert.Single(source.Items.Cast<DynamicTableViewTestRow>());
        name.IsSearchable = false;
        Assert.Empty(source.Items.Cast<DynamicTableViewTestRow>());
        name.IsSearchable = true;
        Assert.Single(source.Items.Cast<DynamicTableViewTestRow>());
    }

    [Fact]
    public void Multiple_sorts_filters_and_search_apply_in_sequence()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableViewTestRow[] rows =
        [
            new("a", "shared", 2, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready),
            new("b", "shared", 1, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready),
            new("c", "shared", 3, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready),
            new("d", "other", 0, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)
        ];
        cache.AddOrUpdate(rows);
        source.SetFilter(new("name", DynamicTableViewFilterOperator.Contains, "shared"));
        source.SearchText = "shared";
        source.SetSort(
        [
            new("name", ListSortDirection.Ascending),
            new("age", ListSortDirection.Descending)
        ]);

        Assert.Equal(["c", "a", "b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Search_matches_every_searchable_column_and_skips_disabled_columns()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var columns = DynamicTableViewTestData.CreateColumns();
        columns[1].IsSearchable = false;
        using var source = DynamicTableViewTestData.CreateSource(cache, columns);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SearchText = "failed";

        Assert.Equal(["c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Sorting_without_active_filters_restores_all_rows()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SetSort([new("age", ListSortDirection.Descending)]);

        Assert.Equal(["c", "b", "a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Sort_takes_descriptor_array_and_exposes_read_only_descriptors()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        var changed = 0;
        source.Changed += (_, _) => changed++;
        DynamicTableViewSortDescriptor[] descriptors = [new("age", ListSortDirection.Descending)];

        source.SetSort(descriptors);

        Assert.Equal(new DynamicTableViewSortDescriptor("age", ListSortDirection.Descending),
            Assert.Single(source.SortDescriptors));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DynamicTableViewSortDescriptor>)source.SortDescriptors)[0] =
                new("age", ListSortDirection.Ascending));
        Assert.Equal(["c", "b", "a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Search_uses_the_configured_debounce_scheduler()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler searchScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache,
            workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: ImmediateScheduler.Instance,
            searchDebounce: TimeSpan.FromMilliseconds(250),
            searchScheduler: searchScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SearchText = "beta";
        Assert.Equal(3, source.Items.Cast<DynamicTableViewTestRow>().Count());
        searchScheduler.AdvanceBy(TimeSpan.FromMilliseconds(249).Ticks);
        Assert.Equal(3, source.Items.Cast<DynamicTableViewTestRow>().Count());
        searchScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        Assert.Equal(["b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void A_new_search_text_cancels_the_previous_debounce()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler searchScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache,
            workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: ImmediateScheduler.Instance,
            searchDebounce: TimeSpan.FromMilliseconds(250),
            searchScheduler: searchScheduler);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SearchText = "Alpha";
        searchScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);
        source.SearchText = "Beta";
        searchScheduler.AdvanceBy(TimeSpan.FromMilliseconds(249).Ticks);
        Assert.Equal(3, source.Items.Cast<DynamicTableViewTestRow>().Count());
        searchScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        Assert.Equal(["b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

        source.SearchText = string.Empty;
        Assert.Equal(3, source.Items.Cast<DynamicTableViewTestRow>().Count());
    }
}
