namespace SvcSystems.Avalonia.DynamicTableView.Tests.Unit;

public sealed class DynamicTableViewColumnWidthTests
{
    [Fact]
    public void Maximum_width_defaults_to_infinity_and_notifies_only_when_changed()
    {
        DynamicTableViewColumn<int> column = DynamicTableViewColumn<int>.Create("value", "Value", static value => value);
        int changes = 0;
        column.WidthOptionsChanged += (_, _) => changes++;

        Assert.Equal(double.PositiveInfinity, column.MaxWidth);

        column.MaxWidth = 200;
        column.MaxWidth = 200;
        column.MaxWidth = double.PositiveInfinity;

        Assert.Equal(double.PositiveInfinity, column.MaxWidth);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Maximum_width_rejects_negative_and_nan_values(double value)
    {
        DynamicTableViewColumn<int> column = DynamicTableViewColumn<int>.Create("value", "Value", static item => item);

        Assert.Throws<ArgumentOutOfRangeException>(() => column.MaxWidth = value);
    }

    [Fact]
    public void Minimum_width_takes_precedence_when_limits_conflict()
    {
        DynamicTableViewColumn<int> column = DynamicTableViewColumn<int>.Create("value", "Value", static item => item);

        column.MinWidth = 150;
        column.MaxWidth = 100;

        Assert.Equal(150, column.MinWidth);
        Assert.Equal(100, column.MaxWidth);
    }
}
