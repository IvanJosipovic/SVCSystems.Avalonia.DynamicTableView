using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.UI;

public sealed class DynamicTableViewColumnLayoutTests
{
    [AvaloniaFact]
    public void Native_style_can_set_column_minimum_and_maximum_widths()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = new GridLength(80);
        column.MinWidth = 40;
        column.MaxWidth = 500;
        column.Classes.Add("wide-minimum");
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        // Native style values override the DynamicTableView column metadata.
        Style style = new(static x => x.OfType<TableViewColumn>().Class("wide-minimum"))
        {
            Setters =
            {
                new Setter(TableViewColumn.MinWidthProperty, 200.0),
                new Setter(TableViewColumn.MaxWidthProperty, 250.0)
            }
        };
        window.Styles.Add(style);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Direct native limits are styleable and override metadata fallbacks.
            Assert.Equal(200, table.Columns[0].MinWidth);
            Assert.Equal(250, table.Columns[0].MaxWidth);
            Assert.Equal(200, table.Columns[0].ActualWidth);
            Assert.Equal(200, table.CaptureState().Columns[0].Width.Value);

            // Both native limits clamp the custom resize interaction.
            table.ResizeColumnAtDivider("name", -500);
            Assert.Equal(200, table.Columns[0].Width.Value);
            table.ResizeColumnAtDivider("name", 500);
            Assert.Equal(250, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_classes_synchronize_while_attached_and_after_reattachment()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TableViewColumn native = table.Columns[0];

            column.Classes.Add("runtime-column-style");
            Assert.Contains("runtime-column-style", native.Classes);

            column.Classes.Remove("runtime-column-style");
            Assert.DoesNotContain("runtime-column-style", native.Classes);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            column.Classes.Add("changed-while-detached");
            window.Content = table;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("changed-while-detached", native.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resize_column_at_divider_grows_and_shrinks_the_column()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = new GridLength(100);
        column.MinWidth = 40;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            table.ResizeColumnAtDivider("name", 50);
            Assert.Equal(150, table.Columns[0].Width.Value);

            table.ResizeColumnAtDivider("name", -25);
            Assert.Equal(125, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resize_column_at_divider_ignores_unknown_key_and_disabled_resizing()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = new GridLength(100);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // An unknown key leaves every column untouched.
            table.ResizeColumnAtDivider("missing", 50);
            Assert.Equal(100, table.Columns[0].Width.Value);

            // Disabling resize makes the operation a no-op.
            table.CanUserResizeColumns = false;
            table.ResizeColumnAtDivider("name", 50);
            Assert.Equal(100, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Metadata_minimum_still_applies_when_no_style_sets_one()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = new GridLength(80);
        column.MinWidth = 150;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(150, table.Columns[0].ActualWidth);
            table.ResizeColumnAtDivider("name", 20);
            Assert.Equal(170, table.Columns[0].Width.Value);
            table.ResizeColumnAtDivider("name", -500);
            Assert.Equal(150, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Metadata_maximum_clamps_pixel_width_resize_and_restored_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = new GridLength(400);
        column.MinWidth = 100;
        column.MaxWidth = 180;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(180, table.Columns[0].ActualWidth);
            Assert.Equal(400, table.Columns[0].Width.Value);

            table.ResizeColumnAtDivider("name", 500);
            Assert.Equal(180, table.Columns[0].Width.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(180, table.Columns[0].ActualWidth);

            column.MaxWidth = 140;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(140, table.Columns[0].ActualWidth);

            table.RestoreState(new DynamicTableViewState(
                [new("name", 0, new GridLength(500))],
                [],
                []));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(140, table.Columns[0].ActualWidth);
            Assert.Equal(140, table.CaptureState().Columns[0].Width.Value);

            // Avalonia's documented conflict rule is minimum wins.
            column.MinWidth = 160;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(160, table.Columns[0].ActualWidth);
            Assert.Equal(160, table.Columns[0].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_star_layout_enforces_and_redistributes_maximum_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> capped = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "capped", "Capped", static row => row.Name);
        capped.Width = new GridLength(capped.Width.Value > 0 ? capped.Width.Value : 1, GridUnitType.Star);
        capped.MaxWidth = 180;
        DynamicTableViewColumn<DynamicTableViewTestRow> remaining = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "remaining", "Remaining", static row => row.Name);
        remaining.Width = new GridLength(remaining.Width.Value > 0 ? remaining.Width.Value : 1, GridUnitType.Star);
        using var source = DynamicTableViewTestData.CreateSource(cache, [capped, remaining]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
            Assert.Equal(180, table.Columns[0].ActualWidth);
            Assert.True(table.Columns[1].ActualWidth > table.Columns[0].ActualWidth);

            double previousRemainingWidth = table.Columns[1].ActualWidth;
            capped.MaxWidth = 120;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, table.Columns[0].ActualWidth);
            Assert.True(table.Columns[1].ActualWidth > previousRemainingWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Native_star_layout_leaves_unused_space_when_every_column_reaches_its_maximum()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.Width = new GridLength(first.Width.Value > 0 ? first.Width.Value : 1, GridUnitType.Star);
        first.MaxWidth = 120;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Name);
        second.Width = new GridLength(second.Width.Value > 0 ? second.Width.Value : 1, GridUnitType.Star);
        second.MaxWidth = 180;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, table.Columns[0].ActualWidth);
            Assert.Equal(180, table.Columns[1].ActualWidth);
            Assert.True(table.Columns.Sum(static column => column.ActualWidth) < window.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_header_updates_visual_without_replacing_native_auto_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        column.Width = GridLength.Auto;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 500, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            column.Header = "A much longer dynamically updated column header";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == column.Header.ToString());
            Assert.Equal(GridUnitType.Auto, table.Columns[0].Width.GridUnitType);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_width_metadata_updates_the_attached_column_while_visible()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        column.Width = new GridLength(120);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, table.Columns[0].Width.Value);

            column.Width = new GridLength(200);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(200, table.Columns[0].Width.Value);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);

            column.MinWidth = 260;
            column.Width = new GridLength(240, GridUnitType.Star);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(240, table.Columns[0].Width.Value);
            Assert.True(table.Columns[0].ActualWidth >= 260);

            column.Width = new GridLength(1, GridUnitType.Star);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Pending_width_metadata_change_is_used_when_the_table_attaches()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        column.Width = new GridLength(120);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            column.Width = new GridLength(210);
            column.MinWidth = 230;
            Dispatcher.UIThread.RunJobs();

            window.Content = table;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(230, table.Columns[0].ActualWidth);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_width_metadata_replaces_a_restored_pixel_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.Width = GridLength.Auto;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(new DynamicTableViewState([new("name", 0, new GridLength(260))], [], []));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(260, table.Columns[0].Width.Value);

            column.Width = GridLength.Auto;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Auto, table.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Auto, table.CaptureState().Columns[0].Width.GridUnitType);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Star_columns_never_apply_a_width_below_their_minimum_while_the_viewport_shrinks()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.Width = new GridLength(first.Width.Value > 0 ? first.Width.Value : 1, GridUnitType.Star);
        first.MinWidth = 120;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Name);
        second.Width = new GridLength(second.Width.Value > 0 ? second.Width.Value : 1, GridUnitType.Star);
        second.MinWidth = 120;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 300, Content = table };

        // Native Star widths stay as Star values; the TableView allocator applies constrained
        // ActualWidths directly, without the former custom pixel-width correction pass.
        var appliedBelowMinimum = new List<double>();
        foreach (TableViewColumn native in table.Columns)
        {
            DynamicTableViewColumn definition = (DynamicTableViewColumn)native.Header!;
            native.PropertyChanged += (_, args) =>
            {
                if (args.Property != TableViewColumn.ActualWidthProperty)
                    return;
                double actualWidth = args.GetNewValue<double>();
                if (double.IsFinite(actualWidth) && actualWidth < definition.MinWidth)
                    appliedBelowMinimum.Add(actualWidth);
            };
        }

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            for (int width = 900; width >= 200; width -= 100)
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
                Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
                Assert.True(table.Columns[0].ActualWidth >= first.MinWidth);
                Assert.True(table.Columns[1].ActualWidth >= second.MinWidth);
            }

            Assert.Empty(appliedBelowMinimum);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Star_column_recovers_when_fixed_width_shrinks_without_viewport_expansion()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.Width = new GridLength(star.Width.Value > 0 ? star.Width.Value : 1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Name);
        fixedColumn.Width = new GridLength(400);
        using var source = DynamicTableViewTestData.CreateSource(cache, [star, fixedColumn]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 300, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].ActualWidth >= star.MinWidth);

            table.Columns[1].Width = new GridLength(80);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.True(table.Columns[0].ActualWidth > star.MinWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Star_column_restores_its_native_star_width_after_an_external_width_change()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.Width = new GridLength(star.Width.Value > 0 ? star.Width.Value : 1, GridUnitType.Star);
        star.MinWidth = 300;
        using var source = DynamicTableViewTestData.CreateSource(cache, [star]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 200, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            table.Columns[0].Width = new GridLength(20);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(1, table.Columns[0].Width.Value);
            Assert.True(table.Columns[0].ActualWidth >= star.MinWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Visible_star_column_recomputes_when_another_star_column_is_hidden()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.Width = new GridLength(first.Width.Value > 0 ? first.Width.Value : 1, GridUnitType.Star);
        first.MinWidth = 400;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Name);
        second.Width = new GridLength(second.Width.Value > 0 ? second.Width.Value : 1, GridUnitType.Star);
        second.MinWidth = 400;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double constrainedWidth = table.Columns[0].ActualWidth;

            table.Columns[1].IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > constrainedWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Weighted_star_columns_reserve_minima_and_recover_across_repeated_resizes()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.Width = new GridLength(1, GridUnitType.Star);
        first.MinWidth = 120;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Name);
        second.Width = new GridLength(2, GridUnitType.Star);
        second.MinWidth = 100;
        DynamicTableViewColumn<DynamicTableViewTestRow> third = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "third", "Third", static row => row.Name);
        third.Width = new GridLength(3, GridUnitType.Star);
        third.MinWidth = 80;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second, third]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 220, Height = 300, Content = table };
        try
        {
            window.Show();
            for (int iteration = 0; iteration < 3; iteration++)
            {
                window.Width = 220;
                Dispatcher.UIThread.RunJobs();
                Assert.True(table.Columns[0].ActualWidth >= first.MinWidth);
                Assert.True(table.Columns[1].ActualWidth >= second.MinWidth);
                Assert.True(table.Columns[2].ActualWidth >= third.MinWidth);

                window.Width = 960;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
                Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
                Assert.InRange(table.Columns[1].ActualWidth / table.Columns[0].ActualWidth, 1.95, 2.05);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Star_columns_keep_their_minimum_width_when_other_columns_exceed_the_viewport()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewTestRow row = DynamicTableViewTestData.CreateRows()[0];
        cache.AddOrUpdate(row);
        DynamicTableViewColumn<DynamicTableViewTestRow> firstStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first-star", "First star", static item => item.Name);
        firstStar.Width = new GridLength(firstStar.Width.Value > 0 ? firstStar.Width.Value : 1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> secondStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second-star", "Second star", static item => item.Name);
        secondStar.Width = new GridLength(secondStar.Width.Value > 0 ? secondStar.Width.Value : 1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> header = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "A deliberately very wide header", static item => item.Name);
        header.Width = GridLength.Auto;
        DynamicTableViewColumn<DynamicTableViewTestRow> cells = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static item => item.Name);
        cells.Width = GridLength.Auto;
        using var source = DynamicTableViewTestData.CreateSource(cache, [firstStar, secondStar, header, cells]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 220, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth >= firstStar.MinWidth);
            Assert.True(table.Columns[1].ActualWidth >= secondStar.MinWidth);

            table.RestoreState(table.CaptureState());
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth >= firstStar.MinWidth);
            Assert.True(table.Columns[1].ActualWidth >= secondStar.MinWidth);

            window.Width = 960;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Star_column_expands_when_a_fixed_column_is_removed_without_resizing_the_window()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.Width = new GridLength(star.Width.Value > 0 ? star.Width.Value : 1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Name);
        fixedColumn.Width = new GridLength(400);
        using var source = DynamicTableViewTestData.CreateSource(cache, [star, fixedColumn]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 300, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].ActualWidth >= star.MinWidth);

            source.Columns.Remove(fixedColumn);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.True(table.Columns[0].ActualWidth > star.MinWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Star_columns_fill_remaining_width_in_the_table_viewport_by_weight()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> timestamp = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "timestamp", "Timestamp", static row => row.CreatedAt);
        timestamp.Width = GridLength.Auto;
        DynamicTableViewColumn<DynamicTableViewTestRow> sourceColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "source", "Source", static row => row.Name);
        sourceColumn.Width = new GridLength(1, GridUnitType.Star);
        DynamicTableViewColumn<DynamicTableViewTestRow> destinationColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "destination", "Destination", static row => row.Name);
        destinationColumn.Width = new GridLength(2, GridUnitType.Star);
        using var source = DynamicTableViewTestData.CreateSource(cache, [timestamp, sourceColumn, destinationColumn]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 960, Height = 400, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TableViewColumnHeader[] headers = table.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();
            double firstHeaderLeft = headers[0].TranslatePoint(default, table)?.X
                ?? throw new InvalidOperationException("First header has no table position.");
            double lastHeaderRight = headers[^1].TranslatePoint(
                new Point(headers[^1].Bounds.Width, 0), table)?.X
                ?? throw new InvalidOperationException("Last header has no table position.");

            Assert.InRange(Math.Abs(table.Bounds.Width - (lastHeaderRight - firstHeaderLeft)), 0, 2);
            Assert.InRange(table.Columns[2].ActualWidth / table.Columns[1].ActualWidth, 1.95, 2.05);
        }
        finally
        {
            window.Close();
        }
    }
}
