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

public sealed class DynamicTableViewColumnResizeRestoreTests
{
    [AvaloniaFact]
    public void Divider_drag_resizes_the_column_on_its_left()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> latency = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "latency", "Latency", static row => row.Name);
        latency.WidthMode = DynamicTableViewWidthMode.Pixel;
        latency.Width = 140;
        DynamicTableViewColumn<DynamicTableViewTestRow> policy = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "policy", "Policy", static row => row.Id);
        policy.WidthMode = DynamicTableViewWidthMode.Pixel;
        policy.Width = 160;
        using var source = DynamicTableViewTestData.CreateSource(cache, [latency, policy]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            double initialLatencyWidth = table.Columns[0].ActualWidth;
            double initialPolicyWidth = table.Columns[1].ActualWidth;
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > initialLatencyWidth,
                "Dragging the Latency/Policy divider right should widen Latency.");
            Assert.Equal(initialPolicyWidth, table.Columns[1].ActualWidth, precision: 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_resizing_still_works_after_restoring_saved_state()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        using var source = DynamicTableViewTestData.CreateSource(cache, CreateColumns());
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            double resizedWidth = table.Columns[0].Width.Value;
            Assert.Equal(130, resizedWidth);

            DynamicTableViewState state = table.CaptureState();
            Assert.Equal(DynamicTableViewWidthMode.Pixel, state.Columns[0].WidthMode);
            Assert.Equal(resizedWidth, state.Columns[0].Width);

            // A reloaded view builds a new table and restores the previously saved widths.
            DynamicTableView restored = new() { Source = source, CanUserResizeColumns = true };
            Window restoredWindow = new() { Width = 900, Height = 400, Content = restored };
            try
            {
                restoredWindow.Show();
                Dispatcher.UIThread.RunJobs();

                restored.RestoreState(state);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(resizedWidth, restored.Columns[0].Width.Value);

                DragColumnDivider(restored, restoredWindow, leftColumnIndex: 0, deltaX: 40);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(resizedWidth + 40, restored.Columns[0].Width.Value);
            }
            finally
            {
                restoredWindow.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_resizing_still_works_after_the_table_is_detached_and_reattached()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        using var source = DynamicTableViewTestData.CreateSource(cache, CreateColumns());
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window host = new() { Width = 900, Height = 400, Content = new Panel() };
        Panel panel = (Panel)host.Content!;

        try
        {
            host.Show();
            panel.Children.Add(table);
            Dispatcher.UIThread.RunJobs();

            DragColumnDivider(table, host, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            double resizedWidth = table.Columns[0].Width.Value;
            Assert.Equal(130, resizedWidth);

            DynamicTableViewState state = table.CaptureState();

            panel.Children.Remove(table);
            Dispatcher.UIThread.RunJobs();

            panel.Children.Add(table);
            Dispatcher.UIThread.RunJobs();

            table.RestoreState(state);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(resizedWidth, table.Columns[0].Width.Value);

            DragColumnDivider(table, host, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(resizedWidth + 40, table.Columns[0].Width.Value);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void Restoring_state_keeps_the_column_resize_grip_usable_on_the_new_headers()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        using var source = DynamicTableViewTestData.CreateSource(cache, CreateColumns());
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TableViewColumnHeader[] headersBefore = table.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();
            Assert.NotEmpty(headersBefore);

            table.RestoreState(table.CaptureState());
            Dispatcher.UIThread.RunJobs();

            TableViewColumnHeader[] headersAfter = table.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();
            Assert.NotEmpty(headersAfter);

            // Restoring rebuilds the native columns, so the header visuals are replaced.
            Assert.False(headersBefore.SequenceEqual(headersAfter));

            // Every new header must expose a live resize grip that is connected to the table.
            // The last column has no right neighbour, so its grip stays hidden by design.
            foreach (TableViewColumnHeader header in headersAfter)
            {
                Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
                    thumb => thumb.Name == "PART_ColumnResizeGrip");
                Assert.NotNull(grip.TranslatePoint(default, table));

                bool isLastColumn = ReferenceEquals(header.Column, table.Columns[^1]);
                Assert.Equal(!isLastColumn, grip.IsVisible);
            }

            // The table must still recognise the divider of the rebuilt headers, which is what
            // drives the resize cursor and the resize gesture.
            TableViewColumnHeader leftHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            Thumb leftGrip = Assert.Single(leftHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Name == "PART_ColumnResizeGrip");
            Point divider = leftGrip.TranslatePoint(
                new Point(leftGrip.Bounds.Width / 2, leftGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Resize grip has no window position.");

            window.MouseMove(new Point(divider.X - 3, divider.Y));
            Dispatcher.UIThread.RunJobs();

            Assert.True(leftHeader.Classes.Contains("dynamic-table-view-resize-pointerover"),
                "The rebuilt column header should still report the resize pointer.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_resizing_recovers_when_the_column_headers_are_recreated()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        using var source = DynamicTableViewTestData.CreateSource(cache, CreateColumns());
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Rebuild the native columns without restoring state. This replaces the header
            // visuals, which is the same effect restoring saved state has.
            table.RestoreState(table.CaptureState());
            Dispatcher.UIThread.RunJobs();

            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(130, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    private static DynamicTableViewColumn<DynamicTableViewTestRow>[] CreateColumns()
    {
        var name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Auto;
        name.Width = 1;
        name.MinWidth = 90;

        var age = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        age.WidthMode = DynamicTableViewWidthMode.Pixel;
        age.Width = 150;
        age.MinWidth = 40;

        var created = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "created", "Created", static row => row.CreatedAt);
        created.WidthMode = DynamicTableViewWidthMode.Pixel;
        created.Width = 150;
        created.MinWidth = 40;

        return [name, age, created];
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
