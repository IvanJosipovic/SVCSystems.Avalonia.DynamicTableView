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
    public void Sample_tabs_resize_each_mixed_width_column()
    {
        DynamicTableViewWidthMode[] expectedModes =
        [
            DynamicTableViewWidthMode.Star, DynamicTableViewWidthMode.Star,
            DynamicTableViewWidthMode.Header, DynamicTableViewWidthMode.Cells,
            DynamicTableViewWidthMode.Pixel, DynamicTableViewWidthMode.Cells
        ];
        for (int tabIndex = 0; tabIndex < 2; tabIndex++)
        {
            for (int target = 0; target < expectedModes.Length; target++)
            {
                using var window = new SvcSystems.Avalonia.DynamicTableView.Sample.MainWindow { Width = 1600 };
                try
                {
                    window.Show();
                    TabControl tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
                    tabs.SelectedIndex = tabIndex;
                    Dispatcher.UIThread.RunJobs();
                    DynamicTableView table = Assert.Single(window.GetVisualDescendants().OfType<DynamicTableView>());
                    Assert.Equal(expectedModes, table.Source!.Columns.Select(static column => column.WidthMode));
                    double[] widths = table.Columns.Select(static column => column.ActualWidth).ToArray();
                    DragColumnDivider(table, window, target, 30);

                    Assert.InRange(table.Columns[target].ActualWidth - widths[target], 29, 31);
                    Assert.Equal(DynamicTableViewWidthMode.Pixel, table.Source.Columns[target].WidthMode);
                    for (int index = 0; index < expectedModes.Length; index++)
                    {
                        if (index != target)
                            Assert.Equal(expectedModes[index], table.Source.Columns[index].WidthMode);
                        if (index != target && expectedModes[index] != DynamicTableViewWidthMode.Star)
                            Assert.InRange(Math.Abs(table.Columns[index].ActualWidth - widths[index]), 0, 1);
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        }
    }

    [AvaloniaFact]
    public void Mixed_width_columns_resize_consistently_at_each_divider()
    {
        DynamicTableViewWidthMode[][] layouts =
        [
            [DynamicTableViewWidthMode.Star, DynamicTableViewWidthMode.Star, DynamicTableViewWidthMode.Header, DynamicTableViewWidthMode.Cells],
            [DynamicTableViewWidthMode.Header, DynamicTableViewWidthMode.Star, DynamicTableViewWidthMode.Cells, DynamicTableViewWidthMode.Pixel],
            [DynamicTableViewWidthMode.Cells, DynamicTableViewWidthMode.Pixel, DynamicTableViewWidthMode.Star, DynamicTableViewWidthMode.Header],
            [DynamicTableViewWidthMode.Pixel, DynamicTableViewWidthMode.Header, DynamicTableViewWidthMode.Cells, DynamicTableViewWidthMode.Star]
        ];

        foreach (DynamicTableViewWidthMode[] layout in layouts)
        {
            for (int target = 0; target < layout.Length - 1; target++)
            {
                using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
                cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
                DynamicTableViewColumn<DynamicTableViewTestRow>[] columns = new DynamicTableViewColumn<DynamicTableViewTestRow>[layout.Length];
                for (int index = 0; index < columns.Length; index++)
                {
                    columns[index] = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
                        $"column-{index}", $"Column {index}", static row => row.Name);
                    columns[index].WidthMode = layout[index];
                    columns[index].Width = layout[index] == DynamicTableViewWidthMode.Star ? 1 : 120;
                    columns[index].MinWidth = 80;
                }

                using var source = DynamicTableViewTestData.CreateSource(cache, columns);
                DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
                Window window = new() { Width = 1000, Height = 400, Content = table };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    double[] widths = table.Columns.Select(static column => column.ActualWidth).ToArray();
                    DragColumnDivider(table, window, target, 30);

                    Assert.InRange(table.Columns[target].ActualWidth - widths[target], 29, 31);
                    Assert.Equal(DynamicTableViewWidthMode.Pixel, columns[target].WidthMode);
                    for (int index = 0; index < columns.Length; index++)
                    {
                        if (index != target)
                            Assert.Equal(layout[index], columns[index].WidthMode);
                        if (index != target && layout[index] != DynamicTableViewWidthMode.Star)
                            Assert.InRange(Math.Abs(table.Columns[index].ActualWidth - widths[index]), 0, 1);
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        }
    }

    [AvaloniaFact]
    public void Resizing_second_star_column_resizes_the_second_column_not_the_first()
    {
        // This test reproduces the bug where resizing the divider between
        // two Star columns incorrectly resizes the first column instead of the second.
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        first.MinWidth = 100;
        
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        second.MinWidth = 100;
        
        DynamicTableViewColumn<DynamicTableViewTestRow> third = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "third", "Third", static row => row.Age);
        third.WidthMode = DynamicTableViewWidthMode.Pixel;
        third.Width = 150;
        
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second, third]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Get initial widths
            double initialFirstWidth = table.Columns[0].ActualWidth;
            double initialSecondWidth = table.Columns[1].ActualWidth;
            double initialThirdWidth = table.Columns[2].ActualWidth;

            // Drag the divider between second and third columns (leftColumnIndex = 1)
            // This should resize the SECOND column (index 1), not the first
            DragColumnDivider(table, window, leftColumnIndex: 1, deltaX: 50);
            Dispatcher.UIThread.RunJobs();

            // The second column should have grown, first should stay the same
            Assert.True(table.Columns[1].ActualWidth > initialSecondWidth,
                "Dragging the Second/Third divider right should widen the Second column.");
            Assert.True(table.Columns[0].ActualWidth < initialFirstWidth);
            Assert.Equal(DynamicTableViewWidthMode.Star, first.WidthMode);
            Assert.Equal(1, first.Width);
            Assert.Equal(initialThirdWidth, table.Columns[2].ActualWidth, precision: 0);
            DynamicTableViewState state = table.CaptureState();
            Assert.Equal(DynamicTableViewWidthMode.Star, state.Columns[0].WidthMode);
            double redistributedWidth = table.Columns[0].ActualWidth;
            window.Width += 200;
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].ActualWidth > redistributedWidth);
            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_first_star_column_resizes_the_first_column()
    {
        // This test ensures the first column divider still works correctly
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        first.MinWidth = 100;
        
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        second.MinWidth = 100;
        
        DynamicTableViewColumn<DynamicTableViewTestRow> third = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "third", "Third", static row => row.Age);
        third.WidthMode = DynamicTableViewWidthMode.Pixel;
        third.Width = 150;
        
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second, third]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            double initialFirstWidth = table.Columns[0].ActualWidth;
            double initialSecondWidth = table.Columns[1].ActualWidth;

            // Drag the divider between first and second columns (leftColumnIndex = 0)
            // This should resize the FIRST column (index 0)
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 50);
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > initialFirstWidth,
                "Dragging the First/Second divider right should widen the First column.");
            Assert.True(table.Columns[1].ActualWidth < initialSecondWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_star_column_converts_it_to_pixel_mode()
    {
        // When a Star column is resized, it should convert to Pixel mode
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        first.MinWidth = 100;
        
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        second.MinWidth = 100;
        
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Resize the second column (divider between first and second)
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 50);
            Dispatcher.UIThread.RunJobs();

            // The first column should now be in Pixel mode
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
            
            // The second column should still be Star
            Assert.Equal(DynamicTableViewWidthMode.Star, source.Columns[1].WidthMode);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Drags the divider that sits on the right edge of <paramref name="leftColumnIndex"/>.
    /// That divider resizes the column to its left.
    /// </summary>
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