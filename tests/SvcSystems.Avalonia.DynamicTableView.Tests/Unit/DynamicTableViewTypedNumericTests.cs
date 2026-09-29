using System.ComponentModel;
using System.Reactive.Concurrency;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewTypedNumericTests
{
    [Theory]
    [InlineData(DynamicTableViewFilterOperator.Equals, "b")]
    [InlineData(DynamicTableViewFilterOperator.NotEquals, "a,c")]
    [InlineData(DynamicTableViewFilterOperator.GreaterThan, "c")]
    [InlineData(DynamicTableViewFilterOperator.GreaterThanOrEqual, "b,c")]
    [InlineData(DynamicTableViewFilterOperator.LessThan, "a")]
    [InlineData(DynamicTableViewFilterOperator.LessThanOrEqual, "a,b")]
    [InlineData(DynamicTableViewFilterOperator.Between, "b")]
    [InlineData(DynamicTableViewFilterOperator.NotBetween, "a,c")]
    [InlineData(DynamicTableViewFilterOperator.In, "a,b")]
    public void Typed_integer_filter_preserves_operator_results(DynamicTableViewFilterOperator operation, string expected)
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SetFilter(new("age", operation, 20, 20, [10, 20]));

        Assert.Equal(expected.Split(','), source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Mixed_numeric_filter_values_keep_decimal_comparison_semantics()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SetFilter(new("age", DynamicTableViewFilterOperator.GreaterThan, 20.5m));
        Assert.Equal(["c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

        source.SetFilter(new("age", DynamicTableViewFilterOperator.In, Values: [10L, 20.5m]));
        Assert.Equal(["a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Typed_numeric_sort_handles_nullable_values_ties_and_descending_order()
    {
        using SourceCache<NumericRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<NumericRow>[] columns =
        [
            DynamicTableViewColumn<NumericRow>.Create("score", "Score", static row => row.Score),
            DynamicTableViewColumn<NumericRow>.Create("amount", "Amount", static row => row.Amount)
        ];
        using var source = new DynamicTableViewSource<NumericRow, string>(
            cache, static row => row.Id, columns,
            ImmediateScheduler.Instance, ImmediateScheduler.Instance, TimeSpan.Zero);
        cache.AddOrUpdate(
        [
            new("a", 2, 3.5m),
            new("b", null, 9m),
            new("c", 2, 1.5m),
            new("d", 1, 4m)
        ]);

        source.SetSort([new("score", ListSortDirection.Ascending), new("amount", ListSortDirection.Descending)]);
        Assert.Equal(["b", "d", "a", "c"], source.Items.Cast<NumericRow>().Select(static row => row.Id));

        source.SetSort([new("score", ListSortDirection.Descending), new("amount", ListSortDirection.Ascending)]);
        Assert.Equal(["c", "a", "d", "b"], source.Items.Cast<NumericRow>().Select(static row => row.Id));

        source.SetFilter(new("score", DynamicTableViewFilterOperator.LessThan, 2));
        Assert.Equal(["d", "b"], source.Items.Cast<NumericRow>().Select(static row => row.Id));

        source.SetFilter(new("score", DynamicTableViewFilterOperator.IsNull));
        Assert.Equal(["b"], source.Items.Cast<NumericRow>().Select(static row => row.Id));
    }

    [Fact]
    public void Object_selector_numeric_column_retains_sort_and_filter_behavior()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column =
            new("age", "Age", typeof(int), static row => row.Age);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());

        source.SetSort([new("age", ListSortDirection.Descending)]);
        source.SetFilter(new("age", DynamicTableViewFilterOperator.GreaterThanOrEqual, 20L));

        Assert.Equal(["c", "b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    private sealed record NumericRow(string Id, int? Score, decimal Amount);
}
