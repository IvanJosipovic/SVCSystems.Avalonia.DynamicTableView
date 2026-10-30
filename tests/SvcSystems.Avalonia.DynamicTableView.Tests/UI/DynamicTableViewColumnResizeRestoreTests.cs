using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.UI;

public sealed class DynamicTableViewColumnResizeRestoreTests
{
    [AvaloniaFact]
    public void Native_resize_observer_recovers_after_pointer_crosses_clamped_divider()
    {
        DynamicTableViewColumn<DynamicTableViewTestRow> definition = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        definition.WidthMode = DynamicTableViewWidthMode.Star;
        definition.MinWidth = 100;
        TableViewColumn native = new() { Header = "Name", Width = new GridLength(1, GridUnitType.Star) };
        TableView table = new() { CanUserResizeColumns = true };
        table.Columns.Add(native);
        bool updating = false;
        native.PropertyChanged += (_, args) =>
        {
            if (updating || args.Property != TableViewColumn.WidthProperty || native.Width.GridUnitType != GridUnitType.Pixel)
                return;
            updating = true;
            try
            {
                double width = Math.Max(definition.MinWidth, native.Width.Value);
                if (native.Width.Value != width)
                    native.Width = new GridLength(width);
                definition.WidthMode = DynamicTableViewWidthMode.Pixel;
                definition.Width = width;
            }
            finally
            {
                updating = false;
            }
        };
        Window window = new() { Width = 400, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TableViewColumnHeader header = Assert.Single(table.GetVisualDescendants().OfType<TableViewColumnHeader>());
            Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(), candidate => candidate.Name == "PART_Resizer");
            List<double> deltas = [];
            List<IInputElement?> captures = [];
            grip.DragDelta += (_, args) => deltas.Add(args.Vector.X);
            grip.PointerMoved += (_, args) => captures.Add(args.Pointer.Captured);
            Point start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            Point end = new(start.X - 350, start.Y);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, native.Width.Value);
            Assert.Equal(100, definition.Width);
            Assert.Equal(DynamicTableViewWidthMode.Pixel, definition.WidthMode);
            Point reverse = new(end.X + 30, end.Y);
            window.MouseMove(reverse, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, native.Width.Value);
            Point pastMinimum = new(start.X - 270, start.Y);
            window.MouseMove(pastMinimum, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.True(native.Width.Value > 100);
            Assert.Equal(3, deltas.Count);
            Assert.True(deltas[0] < 0);
            Assert.True(deltas[1] < 0);
            Assert.True(deltas[2] > 0);
            Assert.NotNull(captures[0]);
            Assert.Contains(grip.GetVisualDescendants(), visual => ReferenceEquals(visual, captures[0]));
            Assert.All(captures, captured => Assert.Same(captures[0], captured));
            Assert.Equal(3, captures.Count);
            window.MouseUp(pastMinimum, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            DynamicTableViewColumnState state = new(definition.Key, 0, definition.Width, definition.WidthMode);
            Assert.Equal(native.Width.Value, state.Width);
            Assert.Equal(DynamicTableViewWidthMode.Pixel, state.WidthMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_resize_reverses_without_clamp_when_layout_tracks_pointer()
    {
        TableViewColumn column = new() { Header = "Name", Width = new GridLength(200) };
        TableView table = new() { CanUserResizeColumns = true };
        table.Columns.Add(column);
        Window window = new() { Width = 400, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TableViewColumnHeader header = Assert.Single(table.GetVisualDescendants().OfType<TableViewColumnHeader>());
            Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(), candidate => candidate.Name == "PART_Resizer");
            List<double> deltas = [];
            grip.DragDelta += (_, args) => deltas.Add(args.Vector.X);
            Point start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            Point left = new(start.X - 50, start.Y);
            Point reverse = new(start.X - 30, start.Y);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(reverse, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(reverse, MouseButton.Left);
            Assert.Equal(2, deltas.Count);
            Assert.InRange(deltas[0], -51, -49);
            Assert.InRange(deltas[1], 19, 21);
            Assert.InRange(column.Width.Value, 169, 171);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_table_resize_updates_width_and_preserves_other_star_columns()
    {
        TableViewColumn first = new() { Header = "First", Width = new GridLength(1, GridUnitType.Star) };
        TableViewColumn second = new() { Header = "Second", Width = new GridLength(1, GridUnitType.Star) };
        TableViewColumn third = new() { Header = "Third", Width = new GridLength(150) };
        TableView table = new() { CanUserResizeColumns = true };
        table.Columns.Add(first);
        table.Columns.Add(second);
        table.Columns.Add(third);
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double initialWidth = second.ActualWidth;
            int widthChanges = 0;
            second.PropertyChanged += (_, args) =>
            {
                if (args.Property == TableViewColumn.WidthProperty)
                    widthChanges++;
            };
            TableViewColumnHeader header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, second));
            Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
                candidate => candidate.Name == "PART_Resizer");
            Point start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            Point end = new(start.X + 30, start.Y);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(second.Width.Value - initialWidth, 29, 31);
            Assert.Equal(GridUnitType.Pixel, second.Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, first.Width.GridUnitType);
            Assert.True(widthChanges > 0);
        }
        finally
        {
            window.Close();
        }
    }

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
            foreach (TableViewColumnHeader header in headersAfter)
            {
                Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
                    thumb => thumb.Name == "PART_ColumnResizeGrip");
                Assert.NotNull(grip.TranslatePoint(default, table));

                Assert.True(grip.IsVisible);
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

    [AvaloniaFact]
    public void Divider_drag_clamps_to_the_column_minimum_and_recovers_within_the_same_gesture()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Pixel;
        name.Width = 150;
        name.MinWidth = 100;
        DynamicTableViewColumn<DynamicTableViewTestRow> age = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        age.WidthMode = DynamicTableViewWidthMode.Pixel;
        age.Width = 150;
        using var source = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(150, table.Columns[0].Width.Value);

            // Overshoot far past the minimum, then reverse inside the overshoot. The custom
            // handler tracks successive pointer positions, so the reverse applies immediately.
            DragColumnDividerGesture(table, window, leftColumnIndex: 0, [-200, 30]);

            Assert.Equal(130, table.Columns[0].Width.Value);
            Assert.Equal(DynamicTableViewWidthMode.Pixel, name.WidthMode);
            Assert.Equal(130, table.CaptureState().Columns[0].Width);
            Assert.Equal(150, table.Columns[1].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Losing_pointer_capture_ends_the_resize_gesture()
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

            IPointer? pointer = null;
            table.AddHandler(
                InputElement.PointerMovedEvent,
                (_, args) => pointer ??= args.Pointer,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            Point start = DividerPoint(table, window, leftColumnIndex: 0);
            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("dynamic-table-view-column-resizing", table.Classes);
            Assert.NotNull(pointer);
            Assert.Same(table, pointer.Captured);

            // The headless mouse device does not deliver PointerCaptureLost when capture is
            // released programmatically, so raise the routed event the platform would raise.
            // PointerCaptureLost is a Direct routed event, so it only reaches the raiser.
            table.RaiseEvent(new PointerCaptureLostEventArgs(table, pointer));
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("dynamic-table-view-column-resizing", table.Classes);
            double widthAfterCaptureLoss = table.Columns[0].Width.Value;
            window.MouseMove(new Point(start.X + 200, start.Y), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(widthAfterCaptureLoss, table.Columns[0].Width.Value);

            // A fresh gesture still works, so capture loss only ended the previous drag.
            window.MouseUp(new Point(start.X + 200, start.Y), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 30);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(widthAfterCaptureLoss + 30, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Divider_drag_after_horizontal_scroll_resizes_the_scrolled_column()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow>[] columns =
            Enumerable.Range(0, 8)
                .Select(index =>
                {
                    DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
                        $"column-{index}", $"Column {index}", static row => row.Name);
                    column.WidthMode = DynamicTableViewWidthMode.Pixel;
                    column.Width = 200;
                    column.MinWidth = 40;
                    return column;
                })
                .ToArray();
        using var source = DynamicTableViewTestData.CreateSource(cache, columns);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 700, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ScrollViewer scrollViewer = Assert.Single(table.GetVisualDescendants().OfType<ScrollViewer>());
            scrollViewer.Offset = new Vector(400, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(400, scrollViewer.Offset.X);

            int targetIndex = 4;
            double targetWidth = table.Columns[targetIndex].Width.Value;
            DragColumnDivider(table, window, leftColumnIndex: targetIndex, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(targetWidth + 40, table.Columns[targetIndex].Width.Value);
            Assert.Equal(200, table.Columns[targetIndex - 1].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_a_shared_column_definition_keeps_attached_tables_in_sync()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Pixel;
        name.Width = 150;
        name.MinWidth = 40;
        DynamicTableViewColumn<DynamicTableViewTestRow> age = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        age.WidthMode = DynamicTableViewWidthMode.Pixel;
        age.Width = 150;
        age.MinWidth = 40;
        using var first = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        using var second = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        DynamicTableView firstTable = new() { Source = first, CanUserResizeColumns = true };
        DynamicTableView secondTable = new() { Source = second, CanUserResizeColumns = true };
        Window window = new()
        {
            Width = 900,
            Height = 400,
            Content = new StackPanel { Children = { firstTable, secondTable } }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(150, firstTable.Columns[0].Width.Value);
            Assert.Equal(150, secondTable.Columns[0].Width.Value);

            DragColumnDivider(firstTable, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            // The definition is shared, so the resize propagates to the other attached table and
            // both agree with the metadata they would capture.
            Assert.Equal(190, name.Width);
            Assert.Equal(DynamicTableViewWidthMode.Pixel, name.WidthMode);
            Assert.Equal(190, firstTable.Columns[0].Width.Value);
            Assert.Equal(190, secondTable.Columns[0].Width.Value);
            Assert.Equal(190, secondTable.CaptureState().Columns[0].Width);
            Assert.Equal(150, firstTable.Columns[1].Width.Value);
            Assert.Equal(150, secondTable.Columns[1].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Header_change_remeasures_in_every_table_sharing_the_definition()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Header;
        name.MinWidth = 40;
        DynamicTableViewColumn<DynamicTableViewTestRow> age = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        age.WidthMode = DynamicTableViewWidthMode.Pixel;
        age.Width = 150;
        using var first = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        using var second = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        DynamicTableView firstTable = new() { Source = first, CanUserResizeColumns = true };
        DynamicTableView secondTable = new() { Source = second, CanUserResizeColumns = true };
        Window window = new()
        {
            Width = 900,
            Height = 400,
            Content = new StackPanel { Children = { firstTable, secondTable } }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            double firstBefore = firstTable.Columns[0].Width.Value;
            double secondBefore = secondTable.Columns[0].Width.Value;

            // A shared definition's header change must re-measure both tables that show it.
            name.Header = "A considerably wider header label";
            Dispatcher.UIThread.RunJobs();

            Assert.True(firstTable.Columns[0].Width.Value > firstBefore);
            Assert.True(secondTable.Columns[0].Width.Value > secondBefore);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Single_column_can_be_resized_and_respects_its_minimum()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> only = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        only.WidthMode = DynamicTableViewWidthMode.Pixel;
        only.Width = 150;
        only.MinWidth = 100;
        using var source = DynamicTableViewTestData.CreateSource(cache, [only]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(table.Columns);

            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(190, table.Columns[0].Width.Value);
            Assert.Equal(190, only.Width);

            // The last column clamps at its minimum like any other.
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: -400);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, table.Columns[0].Width.Value);
            Assert.Equal(100, table.CaptureState().Columns[0].Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Hidden_column_is_skipped_by_the_resize_hit_test()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Pixel;
        name.Width = 150;
        DynamicTableViewColumn<DynamicTableViewTestRow> age = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        age.WidthMode = DynamicTableViewWidthMode.Pixel;
        age.Width = 150;
        using var source = DynamicTableViewTestData.CreateSource(cache, [name, age]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 900, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TableViewColumnHeader hiddenHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[1]));
            Point hiddenDivider = DividerPoint(table, window, leftColumnIndex: 1);

            table.Columns[1].IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(hiddenHeader.IsVisible);

            // The hidden column keeps its width and does not take part in resizing.
            window.MouseMove(hiddenDivider);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("dynamic-table-view-resize-pointerover", hiddenHeader.Classes);

            double nameWidth = table.Columns[0].Width.Value;
            DragColumnDivider(table, window, leftColumnIndex: 0, deltaX: 40);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(nameWidth + 40, table.Columns[0].Width.Value);
            Assert.Equal(150, table.Columns[1].Width.Value);

            // Showing it again restores the divider without losing the width.
            table.Columns[1].IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(hiddenHeader.IsVisible);
            DragColumnDivider(table, window, leftColumnIndex: 1, deltaX: 30);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(180, table.Columns[1].Width.Value);
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

    /// <summary>Returns the window-space center of the divider grip on the right edge of a column.</summary>
    private static Point DividerPoint(DynamicTableView table, Window window, int leftColumnIndex)
    {
        TableViewColumnHeader header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
            .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[leftColumnIndex]));
        Thumb grip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
            thumb => thumb.Name == "PART_ColumnResizeGrip");
        Point divider = grip.TranslatePoint(
            new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Resize grip has no window position.");
        return new Point(divider.X - 3, divider.Y);
    }

    /// <summary>Presses one divider and moves through every horizontal step before releasing.</summary>
    private static void DragColumnDividerGesture(
        DynamicTableView table,
        Window window,
        int leftColumnIndex,
        double[] horizontalSteps)
    {
        Point start = DividerPoint(table, window, leftColumnIndex);
        window.MouseDown(start, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Point current = start;
        foreach (double step in horizontalSteps)
        {
            current = new Point(current.X + step, current.Y);
            window.MouseMove(current, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        window.MouseUp(current, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
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
        Point start = DividerPoint(table, window, leftColumnIndex);
        Point end = new(start.X + deltaX, start.Y);

        window.MouseDown(start, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
