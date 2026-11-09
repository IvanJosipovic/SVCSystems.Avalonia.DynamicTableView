using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.UI;

public sealed class StarColumnResizeTests
{
    [AvaloniaFact]
    public void Auto_width_is_allocated_as_a_one_star_share()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> automatic = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "automatic", "Auto", static row => row.Name);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.Width = new GridLength(1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Age);
        fixedColumn.Width = new GridLength(120);

        using var source = DynamicTableViewTestData.CreateSource(cache, [automatic, star, fixedColumn]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 400, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Auto, table.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
            Assert.Equal(GridUnitType.Pixel, table.Columns[2].Width.GridUnitType);
            Assert.InRange(Math.Abs(table.Columns[0].ActualWidth - table.Columns[1].ActualWidth), 0, 1);
            Assert.Equal(120, table.Columns[2].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_auto_column_converts_it_to_pixel_and_preserves_other_widths()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> automatic = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "automatic", "Auto", static row => row.Name);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Id);
        star.Width = new GridLength(1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Age);
        fixedColumn.Width = new GridLength(120);

        using var source = DynamicTableViewTestData.CreateSource(cache, [automatic, star, fixedColumn]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double initialWidth = table.Columns[0].ActualWidth;

            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 30);

            Assert.InRange(table.Columns[0].ActualWidth - initialWidth, 29, 31);
            Assert.Equal(GridUnitType.Pixel, automatic.Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, star.Width.GridUnitType);
            Assert.Equal(GridUnitType.Pixel, fixedColumn.Width.GridUnitType);
            Assert.Equal(120, table.Columns[2].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_a_star_column_converts_only_that_column_to_pixels()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.Width = new GridLength(1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.Width = new GridLength(2, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Age);
        fixedColumn.Width = new GridLength(150);

        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second, fixedColumn]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double initialFirstWidth = table.Columns[0].ActualWidth;
            double initialSecondWidth = table.Columns[1].ActualWidth;
            double initialFixedWidth = table.Columns[2].ActualWidth;

            DragColumnDivider(table, window, leftColumnIndex: 1, deltaX: 50);

            Assert.True(table.Columns[1].ActualWidth > initialSecondWidth);
            Assert.True(table.Columns[0].ActualWidth < initialFirstWidth);
            Assert.Equal(GridUnitType.Star, first.Width.GridUnitType);
            Assert.Equal(1, first.Width.Value);
            Assert.Equal(GridUnitType.Pixel, second.Width.GridUnitType);
            Assert.Equal(GridUnitType.Pixel, fixedColumn.Width.GridUnitType);
            Assert.Equal(initialFixedWidth, table.Columns[2].ActualWidth, precision: 0);

            DynamicTableViewState state = table.CaptureState();
            Assert.Equal(GridUnitType.Star, state.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Pixel, state.Columns[1].Width.GridUnitType);
            double redistributedWidth = table.Columns[0].ActualWidth;
            window.Width += 200;
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].ActualWidth > redistributedWidth);
        }
        finally
        {
            window.Close();
        }
    }

    private static void DragColumnDivider(
        DynamicTableView table,
        Window window,
        int leftColumnIndex,
        double deltaX)
    {
        TableViewColumnHeader header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
            .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[leftColumnIndex]));
        Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
            thumb => thumb.Name == "PART_ColumnResizeGrip");
        Point divider = grip.TranslatePoint(
            new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Resize grip has no window position.");
        Point start = new(divider.X - 3, divider.Y);
        Point end = new(start.X + deltaX, start.Y);

        window.MouseDown(start, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
