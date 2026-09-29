using System.ComponentModel;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;
using Microsoft.Reactive.Testing;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewSelectionMatrixTests
{
    public static IEnumerable<object?[]> IdentityAndChangeCases()
    {
        foreach (DynamicTableViewSelectionIdentityMode? mode in new DynamicTableViewSelectionIdentityMode?[]
                 {
                     null,
                     DynamicTableViewSelectionIdentityMode.Key,
                     DynamicTableViewSelectionIdentityMode.Reference
                 })
        {
            foreach (SourceChange change in Enum.GetValues<SourceChange>())
            {
                foreach (SelectionSet selectionSet in Enum.GetValues<SelectionSet>())
                {
                    string[] expectedIds = GetExpectedIds(mode, change, selectionSet);
                    yield return [mode, change, selectionSet, expectedIds];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(IdentityAndChangeCases))]
    public void Selection_tracks_configured_identity_across_dynamic_data_changes(
        DynamicTableViewSelectionIdentityMode? mode,
        SourceChange change,
        SelectionSet selectionSet,
        string[] expectedIds)
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        TestScheduler uiScheduler = new();
        DynamicTableViewSourceOptions? options = mode is { } identityMode
            ? new() { SelectionIdentityMode = identityMode }
            : null;
        using var source = DynamicTableViewTestData.CreateSource(
            cache,
            uiScheduler: uiScheduler,
            options: options);
        var originalRows = DynamicTableViewTestData.CreateRows();
        cache.AddOrUpdate(originalRows);
        source.SetSort([new("age", ListSortDirection.Ascending)]);
        uiScheduler.AdvanceBy(100);

        DynamicTableViewTestRow selected = originalRows[1];
        if (selectionSet == SelectionSet.Single)
        {
            source.SelectionModel.Select(1);
        }
        else
        {
            source.SelectionModel.SelectAll();
        }

        switch (change)
        {
            case SourceChange.AddBeforeSelected:
                cache.AddOrUpdate(new DynamicTableViewTestRow(
                    "d", "Delta", 5, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
                break;
            case SourceChange.AddMultipleBeforeSelected:
                cache.Edit(updater =>
                {
                    updater.AddOrUpdate(new DynamicTableViewTestRow(
                        "d", "Delta", 5, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
                    updater.AddOrUpdate(new DynamicTableViewTestRow(
                        "e", "Epsilon", 7, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
                });
                break;
            case SourceChange.AddAfterSelected:
                cache.AddOrUpdate(new DynamicTableViewTestRow(
                    "d", "Delta", 40, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready));
                break;
            case SourceChange.RemoveBeforeSelected:
                cache.RemoveKey("a");
                break;
            case SourceChange.ReplaceEarlierRow:
                cache.AddOrUpdate(originalRows[0] with { Age = 40, Name = "Moved Alpha" });
                break;
            case SourceChange.ReplaceSelected:
                selected = selected with { Name = "Updated Beta" };
                cache.AddOrUpdate(selected);
                break;
            case SourceChange.RemoveSelected:
                cache.RemoveKey(selected.Id);
                break;
            case SourceChange.MoveSelected:
                selected = selected with { Age = 40, Name = "Moved Beta" };
                cache.AddOrUpdate(selected);
                break;
            case SourceChange.ChangeSortOrder:
                source.SetSort([new("age", ListSortDirection.Descending)]);
                break;
            case SourceChange.FilterSelectedOut:
                source.SetFilter(new("name", DynamicTableViewFilterOperator.Equals, "Alpha"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unsupported test source change.");
        }

        uiScheduler.AdvanceBy(100);
        if (change == SourceChange.FilterSelectedOut)
        {
            source.ClearFilters();
            uiScheduler.AdvanceBy(100);
        }

        Assert.Equal(expectedIds, GetSelectedIds(source.SelectionModel.SelectedItems));
        if (change == SourceChange.ReplaceSelected && mode != DynamicTableViewSelectionIdentityMode.Reference)
        {
            Assert.Contains(source.SelectionModel.SelectedItems, item => ReferenceEquals(item, selected));
        }
    }

    private static string[] GetExpectedIds(
        DynamicTableViewSelectionIdentityMode? mode,
        SourceChange change,
        SelectionSet selectionSet)
    {
        IEnumerable<string> selectedIds = selectionSet == SelectionSet.Single
            ? ["b"]
            : ["a", "b", "c"];
        if (change == SourceChange.RemoveBeforeSelected)
        {
            selectedIds = selectedIds.Where(static id => id != "a");
        }
        else if (change == SourceChange.ReplaceEarlierRow &&
                 mode == DynamicTableViewSelectionIdentityMode.Reference)
        {
            selectedIds = selectedIds.Where(static id => id != "a");
        }
        else if (change is SourceChange.ReplaceSelected or SourceChange.MoveSelected &&
                 mode == DynamicTableViewSelectionIdentityMode.Reference)
        {
            selectedIds = selectedIds.Where(static id => id != "b");
        }
        else if (change == SourceChange.RemoveSelected)
        {
            selectedIds = selectedIds.Where(static id => id != "b");
        }
        else if (change == SourceChange.FilterSelectedOut)
        {
            selectedIds = selectedIds.Where(static id => id == "a");
        }

        return selectedIds.Order(StringComparer.Ordinal).ToArray();
    }

    public enum SourceChange
    {
        AddBeforeSelected,
        AddMultipleBeforeSelected,
        AddAfterSelected,
        RemoveBeforeSelected,
        ReplaceEarlierRow,
        ReplaceSelected,
        RemoveSelected,
        MoveSelected,
        ChangeSortOrder,
        FilterSelectedOut
    }

    public enum SelectionSet
    {
        Single,
        Multiple
    }

    private static string[] GetSelectedIds(IEnumerable<object?> selectedItems)
        => selectedItems.Cast<DynamicTableViewTestRow>()
            .Select(static row => row.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
