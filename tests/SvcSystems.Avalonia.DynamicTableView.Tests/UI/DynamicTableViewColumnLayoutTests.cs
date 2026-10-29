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
    public void Style_can_set_a_column_minimum_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 80;
        column.MinWidth = 40;
        column.Classes.Add("wide-minimum");
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        // A style, not the column metadata, supplies the minimum.
        Style style = new(static x => x.OfType<TableViewColumn>().Class("wide-minimum"))
        {
            Setters = { new Setter(DynamicTableViewColumnProperties.MinWidthProperty, 200.0) }
        };
        window.Styles.Add(style);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The style is applied when the column attaches, so the initial width already honours it.
            Assert.Equal(200, table.Columns[0].Width.Value);
            Assert.Equal(200, table.CaptureState().Columns[0].Width);

            // The styled minimum also clamps a resize, which is the behavior the metadata used to own.
            table.ResizeColumnAtDivider("name", -500);
            Assert.Equal(200, table.Columns[0].Width.Value);
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
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 100;
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
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 100;
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
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 80;
        column.MinWidth = 150;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 600, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(150, table.Columns[0].Width.Value);
            table.ResizeColumnAtDivider("name", -500);
            Assert.Equal(150, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_header_updates_visual_and_header_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Header;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 500, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double initialWidth = table.Columns[0].Width.Value;
            column.Header = "A much longer dynamically updated column header";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == column.Header.ToString());
            Assert.True(table.Columns[0].Width.Value > initialWidth);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Replacing_source_does_not_suppress_auto_sizing_for_reused_key()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> original = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        DynamicTableViewColumn<DynamicTableViewTestRow> replacement = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "A very long header for the replacement column", static row => row.Name);
        replacement.WidthMode = DynamicTableViewWidthMode.Header;
        using var first = DynamicTableViewTestData.CreateSource(cache, [original]);
        using var second = DynamicTableViewTestData.CreateSource(cache, [replacement]);
        DynamicTableView table = new() { Source = first };
        Window window = new() { Width = 500, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(new DynamicTableViewState([new("name", 0, 120)], [], []));
            table.Source = second;
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].Width.Value > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Removing_column_does_not_suppress_auto_sizing_for_reused_key()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> original = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        DynamicTableViewColumn<DynamicTableViewTestRow> replacement = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "A very long header for the replacement column", static row => row.Name);
        replacement.WidthMode = DynamicTableViewWidthMode.Header;
        using var source = DynamicTableViewTestData.CreateSource(cache, [original]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 500, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(new DynamicTableViewState([new("name", 0, 120)], [], []));
            source.Columns.Remove(original);
            source.Columns.Add(replacement);
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].Width.Value > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_width_metadata_updates_the_attached_column_while_visible()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 120;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, table.Columns[0].Width.Value);

            column.Width = 200;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(200, table.Columns[0].Width.Value);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);

            column.MinWidth = 260;
            column.Width = 240;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(260, table.Columns[0].Width.Value);
            Assert.Equal(260, table.CaptureState().Columns[0].Width);

            column.WidthMode = DynamicTableViewWidthMode.Star;
            column.Width = 1;
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
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 120;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            column.Width = 210;
            column.MinWidth = 230;
            Dispatcher.UIThread.RunJobs();

            window.Content = table;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(230, table.Columns[0].Width.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_width_metadata_overrides_a_restored_manual_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Header;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(new DynamicTableViewState([new("name", 0, 260)], [], []));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(260, table.Columns[0].Width.Value);

            column.Width = 150;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(150, table.Columns[0].Width.Value);
            Assert.Equal(150, table.CaptureState().Columns[0].Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Clearing_columns_does_not_suppress_auto_sizing_for_a_reused_key()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> original = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        DynamicTableViewColumn<DynamicTableViewTestRow> replacement = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "A very long header for the replacement column", static row => row.Name);
        replacement.WidthMode = DynamicTableViewWidthMode.Header;
        using var source = DynamicTableViewTestData.CreateSource(cache, [original]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 500, Height = 300, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(new DynamicTableViewState([new("name", 0, 120)], [], []));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, table.Columns[0].Width.Value);

            source.Columns.Clear();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(table.Columns);

            source.Columns.Add(replacement);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(table.Columns);
            Assert.True(table.Columns[0].Width.Value > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Star_column_recovers_when_fixed_width_shrinks_without_viewport_expansion()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.WidthMode = DynamicTableViewWidthMode.Star;
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Name);
        fixedColumn.WidthMode = DynamicTableViewWidthMode.Pixel;
        fixedColumn.Width = 400;
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
    public void Weighted_star_columns_reserve_minima_and_recover_across_repeated_resizes()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        first.MinWidth = 120;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Name);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 2;
        second.MinWidth = 100;
        DynamicTableViewColumn<DynamicTableViewTestRow> third = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "third", "Third", static row => row.Name);
        third.WidthMode = DynamicTableViewWidthMode.Star;
        third.Width = 3;
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
        firstStar.WidthMode = DynamicTableViewWidthMode.Star;
        DynamicTableViewColumn<DynamicTableViewTestRow> secondStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second-star", "Second star", static item => item.Name);
        secondStar.WidthMode = DynamicTableViewWidthMode.Star;
        DynamicTableViewColumn<DynamicTableViewTestRow> header = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "A deliberately very wide header", static item => item.Name);
        header.WidthMode = DynamicTableViewWidthMode.Header;
        DynamicTableViewColumn<DynamicTableViewTestRow> cells = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static item => item.Name);
        cells.WidthMode = DynamicTableViewWidthMode.Cells;
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
        star.WidthMode = DynamicTableViewWidthMode.Star;
        DynamicTableViewColumn<DynamicTableViewTestRow> fixedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fixed", "Fixed", static row => row.Name);
        fixedColumn.WidthMode = DynamicTableViewWidthMode.Pixel;
        fixedColumn.Width = 400;
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
        timestamp.WidthMode = DynamicTableViewWidthMode.Auto;
        DynamicTableViewColumn<DynamicTableViewTestRow> sourceColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "source", "Source", static row => row.Name);
        sourceColumn.WidthMode = DynamicTableViewWidthMode.Star;
        sourceColumn.Width = 1;
        DynamicTableViewColumn<DynamicTableViewTestRow> destinationColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "destination", "Destination", static row => row.Name);
        destinationColumn.WidthMode = DynamicTableViewWidthMode.Star;
        destinationColumn.Width = 2;
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
