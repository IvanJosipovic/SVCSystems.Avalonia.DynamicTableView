using System.Collections.Generic;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewFilterDescriptorSnapshotTests
{
    [Fact]
    public void Descriptor_reads_share_an_unchanging_snapshot_until_filters_change()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        var changed = 0;
        source.Changed += (_, _) => changed++;
        IReadOnlyList<DynamicTableViewFilterDescriptor> empty = source.FilterDescriptors;
        Assert.Same(empty, source.FilterDescriptors);

        DynamicTableViewFilterDescriptor first = new("age", DynamicTableViewFilterOperator.GreaterThan, 10);
        source.SetFilter(first);
        IReadOnlyList<DynamicTableViewFilterDescriptor> firstSnapshot = source.FilterDescriptors;
        Assert.Same(firstSnapshot, source.FilterDescriptors);
        Assert.Equal([first], firstSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<DynamicTableViewFilterDescriptor>)firstSnapshot)[0] = first);

        DynamicTableViewFilterDescriptor replacement = new("age", DynamicTableViewFilterOperator.GreaterThan, 20);
        source.SetFilter(replacement);
        Assert.Equal([first], firstSnapshot);
        Assert.Equal([replacement], source.FilterDescriptors);
        Assert.NotSame(firstSnapshot, source.FilterDescriptors);

        IReadOnlyList<DynamicTableViewFilterDescriptor> secondSnapshot = source.FilterDescriptors;
        source.SetFilter(null, "missing");
        Assert.Same(secondSnapshot, source.FilterDescriptors);
        Assert.Equal(3, changed);

        source.SetFilter(null, "age");
        Assert.Empty(source.FilterDescriptors);
        Assert.Equal([replacement], secondSnapshot);
        Assert.Equal(4, changed);
    }

    [Fact]
    public void Clearing_filters_only_replaces_snapshot_when_descriptors_were_present()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        var changed = 0;
        source.Changed += (_, _) => changed++;

        IReadOnlyList<DynamicTableViewFilterDescriptor> empty = source.FilterDescriptors;
        source.ClearFilters();
        Assert.Same(empty, source.FilterDescriptors);
        Assert.Equal(0, changed);

        source.SetCustomFilter("age", static _ => true);
        Assert.Same(empty, source.FilterDescriptors);
        source.ClearFilters();
        Assert.Same(empty, source.FilterDescriptors);
        Assert.Equal(2, changed);

        DynamicTableViewFilterDescriptor descriptor = new("age", DynamicTableViewFilterOperator.Equals, 20);
        source.SetFilter(descriptor);
        IReadOnlyList<DynamicTableViewFilterDescriptor> beforeClear = source.FilterDescriptors;
        source.ClearFilters();
        Assert.Equal([descriptor], beforeClear);
        Assert.Empty(source.FilterDescriptors);
        Assert.NotSame(beforeClear, source.FilterDescriptors);
        Assert.Equal(4, changed);

        IReadOnlyList<DynamicTableViewFilterDescriptor> afterClear = source.FilterDescriptors;
        source.ClearFilters();
        Assert.Same(afterClear, source.FilterDescriptors);
        Assert.Equal(4, changed);
    }
}
