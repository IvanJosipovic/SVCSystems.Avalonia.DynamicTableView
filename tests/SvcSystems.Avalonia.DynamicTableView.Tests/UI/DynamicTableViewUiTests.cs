using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.UI;

public sealed class DynamicTableViewUiTests
{
    [AvaloniaFact]
    public void Default_cell_text_options_inherit_and_update_with_column_overrides()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow>[] columns =
        [
            DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name),
            DynamicTableViewColumn<DynamicTableViewTestRow>.Create("age", "Age", static row => row.Age)
        ];
        columns[1].CellTextWrapping = TextWrapping.Wrap;
        columns[1].CellTextTrimming = TextTrimming.CharacterEllipsis;
        columns[1].CellMaxLines = 2;
        using var source = DynamicTableViewTestData.CreateSource(cache, columns);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);

        DynamicTableView table = new()
        {
            Source = source,
            CellTextWrapping = TextWrapping.NoWrap,
            CellTextTrimming = TextTrimming.WordEllipsis,
            CellMaxLines = 3
        };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBlock nameText = table.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Alpha");
            TextBlock ageText = table.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "10");
            Assert.Equal(TextWrapping.NoWrap, nameText.TextWrapping);
            Assert.Equal(TextTrimming.WordEllipsis, nameText.TextTrimming);
            Assert.Equal(3, nameText.MaxLines);
            Assert.Equal(TextWrapping.Wrap, ageText.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, ageText.TextTrimming);
            Assert.Equal(2, ageText.MaxLines);

            table.CellTextWrapping = TextWrapping.Wrap;
            table.CellTextTrimming = TextTrimming.CharacterEllipsis;
            table.CellMaxLines = 4;
            Assert.Equal(TextWrapping.Wrap, nameText.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, nameText.TextTrimming);
            Assert.Equal(4, nameText.MaxLines);

            columns[0].CellTextWrapping = TextWrapping.NoWrap;
            columns[0].CellMaxLines = 0;
            Assert.Equal(TextWrapping.NoWrap, nameText.TextWrapping);
            Assert.Equal(0, nameText.MaxLines);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Cell_max_lines_rejects_negative_values()
    {
        DynamicTableView table = new();
        Assert.Throws<ArgumentException>(() => table.CellMaxLines = -1);

        DynamicTableViewColumn<DynamicTableViewTestRow> column =
            DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => column.CellMaxLines = -1);
    }

    [AvaloniaFact]
    public void Source_columns_and_selection_are_attached_to_the_control()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };

        Assert.Equal(source.Columns.Count, table.Columns.Count);
        Assert.Same(source.SelectionModel, table.Selection);
        source.Columns.Add(DynamicTableViewColumn<DynamicTableViewTestRow>.Create("new", "New", static row => row.Id));
        Assert.Equal(source.Columns.Count, table.Columns.Count);
    }

    [AvaloniaFact]
    public void Column_add_remove_and_move_keep_unaffected_native_columns()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        var first = table.Columns[0];
        var second = table.Columns[1];
        var added = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("extra", "Extra", static row => row.Name);

        source.Columns.Add(added);
        Assert.Same(first, table.Columns[0]);
        Assert.Same(second, table.Columns[1]);

        source.Columns.Move(0, 2);
        Assert.Same(second, table.Columns[0]);
        Assert.Same(first, table.Columns[2]);

        source.Columns.Remove(added);
        Assert.Same(second, table.Columns[0]);
        Assert.Same(first, table.Columns[2]);

        var replacement = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "replacement", "Replacement", static row => row.Name);
        source.Columns[1] = replacement;
        Assert.Same(second, table.Columns[0]);
        Assert.Same(first, table.Columns[2]);
        Assert.Same(replacement, table.Columns[1].Header);
        Assert.Equal(source.Columns.Count, table.Columns.Count);
    }

    [AvaloniaFact]
    public void Auto_width_pass_only_measures_auto_and_cells_columns()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var pixelCalls = 0;
        var starCalls = 0;
        var autoCalls = 0;
        var cellsCalls = 0;
        var pixel = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "pixel", "Pixel", static row => row.Name, row =>
            {
                pixelCalls++;
                return row.Name;
            });
        pixel.WidthMode = DynamicTableViewWidthMode.Pixel;
        pixel.Width = 120;
        var star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name, row =>
            {
                starCalls++;
                return row.Name;
            });
        star.WidthMode = DynamicTableViewWidthMode.Star;
        var auto = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "auto", "Auto", static row => row.Name, row =>
            {
                autoCalls++;
                return row.Name;
            });
        var cells = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static row => row.Name, row =>
            {
                cellsCalls++;
                return row.Name;
            });
        cells.WidthMode = DynamicTableViewWidthMode.Cells;
        using var source = DynamicTableViewTestData.CreateSource(cache, [pixel, star, auto, cells]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var pixelBefore = pixelCalls;
            var starBefore = starCalls;
            var autoBefore = autoCalls;
            var cellsBefore = cellsCalls;

            window.Height += 10;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(pixelBefore, pixelCalls);
            Assert.Equal(starBefore, starCalls);
            Assert.True(autoCalls > autoBefore);
            Assert.True(cellsCalls > cellsBefore);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Header_and_cell_padding_apply_to_their_content()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        table.Resources["DynamicTableView.CellPadding"] = new Thickness(0);
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columnHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>().First();
            var header = columnHeader.GetVisualDescendants().OfType<TemplatedControl>().First();
            var headerLabel = header.GetVisualDescendants().OfType<TextBlock>().First();
            var cell = table.GetVisualDescendants().OfType<TableViewCell>().First();
            cell.MinHeight = 60;
            Dispatcher.UIThread.RunJobs();
            var cellText = cell.GetVisualDescendants().OfType<TextBlock>().First();
            var row = cell.GetSelfAndVisualAncestors().OfType<TableViewRow>().First();

            Assert.Equal(default, columnHeader.Padding);
            Assert.True(header.Padding.Top > 0);
            Assert.Equal(default, row.Padding);
            Assert.Equal(default, cell.Padding);
            Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, cell.VerticalContentAlignment);
            Assert.Equal(default, header.TranslatePoint(default, columnHeader)!.Value);
            Assert.Equal(new Point(header.Padding.Left, header.Padding.Top), headerLabel.TranslatePoint(default, header)!.Value);
            var cellTextOrigin = cellText.TranslatePoint(default, cell)!.Value;
            Assert.Equal(0, cellTextOrigin.X);
            Assert.Equal((cell.Bounds.Height - cellText.Bounds.Height) / 2, cellTextOrigin.Y, 2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Header_and_cell_padding_use_the_documented_defaults()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.Styles.TryGetResource(
                "DynamicTableView.HeaderPadding", table.ActualThemeVariant, out object? headerPadding));
            Assert.True(Application.Current.Styles.TryGetResource(
                "DynamicTableView.CellPadding", table.ActualThemeVariant, out object? cellPadding));

            Assert.Equal(new Thickness(4, 6, 4, 6), Assert.IsType<Thickness>(headerPadding));
            Assert.Equal(new Thickness(4), Assert.IsType<Thickness>(cellPadding));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Custom_cell_template_receives_row_and_preserves_display_tooltip()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewTestRow row = DynamicTableViewTestData.CreateRows()[0];
        cache.AddOrUpdate(row);
        IDataTemplate template = new FuncDataTemplate<DynamicTableViewTestRow>(
            static (item, _) => new TextBlock { Text = item is null ? string.Empty : $"Template: {item.Name}" },
            supportsRecycling: true);
        DynamicTableViewColumn<DynamicTableViewTestRow> templatedColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "templated", "Templated", static item => item.Name, static item => $"Display: {item.Name}", template);
        DynamicTableViewColumn<DynamicTableViewTestRow> fallbackColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "fallback", "Fallback", static item => item.Name, static item => $"Fallback: {item.Name}");
        using var source = DynamicTableViewTestData.CreateSource(cache, [templatedColumn, fallbackColumn]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ContentControl templatedCell = table.GetVisualDescendants().OfType<ContentControl>()
                .Single(control => Equals(ToolTip.GetTip(control), $"Display: {row.Name}"));
            ContentControl fallbackCell = table.GetVisualDescendants().OfType<ContentControl>()
                .Single(control => Equals(ToolTip.GetTip(control), $"Fallback: {row.Name}"));
            TextBlock templatedText = Assert.Single(templatedCell.GetVisualDescendants().OfType<TextBlock>());

            Assert.Same(row, templatedCell.Content);
            Assert.Equal($"Template: {row.Name}", templatedText.Text);
            Assert.Equal($"Fallback: {row.Name}", fallbackCell.Content);
            Assert.Equal($"Fallback: {row.Name}", ToolTip.GetTip(fallbackCell));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_blank_header_area_sorts_and_keeps_indicator_next_to_text()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var nameColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        nameColumn.WidthMode = DynamicTableViewWidthMode.Pixel;
        nameColumn.Width = 220;
        using var source = DynamicTableViewTestData.CreateSource(cache, [nameColumn]);
        cache.AddOrUpdate([
            DynamicTableViewTestData.CreateRows()[0] with { Name = "Gamma" },
            DynamicTableViewTestData.CreateRows()[1] with { Name = "Alpha" },
            DynamicTableViewTestData.CreateRows()[2] with { Name = "Beta" }]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var filterOrigin = filterButton.TranslatePoint(default, header)
                ?? throw new InvalidOperationException("Filter button has no header position.");
            var blankHeaderPoint = header.TranslatePoint(
                new Point(filterOrigin.X - 8, header.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");

            window.MouseDown(blankHeaderPoint, MouseButton.Left);
            window.MouseUp(blankHeaderPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(source.SortDescriptors);
            Assert.Equal(["b", "c", "a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
            Assert.Single(header.GetVisualDescendants().OfType<Button>());
            var label = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>()
                .Where(static textBlock => textBlock.Text == "Name"));
            var sortIcon = Assert.Single(header.GetVisualDescendants().OfType<PathIcon>()
                .Where(icon => icon.IsVisible && !icon.GetSelfAndVisualAncestors().OfType<Button>().Contains(filterButton)));
            var labelOrigin = label.TranslatePoint(default, header)!.Value;
            var sortIconOrigin = sortIcon.TranslatePoint(default, header)!.Value;
            Assert.True(sortIconOrigin.X >= labelOrigin.X + label.DesiredSize.Width);
            Assert.True(sortIconOrigin.X <= labelOrigin.X + label.Bounds.Width + 4);
            Assert.True(sortIconOrigin.X < filterOrigin.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_header_strip_includes_header_content_padding()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var headerPresenter = Assert.IsType<TableViewColumnHeadersPresenter>(table.GetVisualDescendants().OfType<TableViewColumnHeadersPresenter>().First());
            var headerStrip = Assert.IsType<Border>(headerPresenter.GetSelfAndVisualAncestors().OfType<Border>().First());
            var columnHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>().First();
            var header = columnHeader.GetVisualDescendants().OfType<TemplatedControl>().First();

            Assert.True(header.Padding.Top > 0);
            Assert.Equal(columnHeader.Bounds.Height, headerStrip.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_top_and_bottom_of_header_sorts_the_column()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var columnHeader = Assert.Single(header.GetSelfAndVisualAncestors().OfType<TableViewColumnHeader>());
            Assert.Equal(columnHeader.Bounds.Height, header.Bounds.Height);

            Point topPoint = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width / 2, 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseDown(topPoint, MouseButton.Left);
            window.MouseUp(topPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ListSortDirection.Ascending, Assert.Single(source.SortDescriptors).Direction);

            Point bottomPoint = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width / 2, columnHeader.Bounds.Height - 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseDown(bottomPoint, MouseButton.Left);
            window.MouseUp(bottomPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ListSortDirection.Descending, Assert.Single(source.SortDescriptors).Direction);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_header_changes_its_full_background_on_hover()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        SolidColorBrush hoverBrush = new(Colors.Orange);
        table.Resources["SystemControlHighlightListLowBrush"] = hoverBrush;
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var columnHeader = Assert.Single(header.GetSelfAndVisualAncestors().OfType<TableViewColumnHeader>());
            var headerStrip = Assert.Single(columnHeader.GetSelfAndVisualAncestors().OfType<Border>()
                .Where(border => border.GetVisualParent() is DockPanel { Name: "ContentPanel" }));
            var hoverSurface = Assert.Single(columnHeader.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => presenter.Name == "PART_ContentPresenter" && presenter.Bounds.Size == columnHeader.Bounds.Size && presenter.BorderThickness == default);
            var otherHeaders = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Where(candidate => !ReferenceEquals(candidate, columnHeader))
                .ToArray();
            var otherHoverSurfaces = otherHeaders.Select(candidate => Assert.Single(
                candidate.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => presenter.Name == "PART_ContentPresenter" && presenter.Bounds.Size == candidate.Bounds.Size && presenter.BorderThickness == default))
                .ToArray();
            Assert.Equal(window.Width, headerStrip.Bounds.Width);
            Assert.Equal(default, headerStrip.Padding);
            Assert.Equal(0, columnHeader.Bounds.X);
            Assert.True(headerStrip.Bounds.Height > 0);
            Assert.Equal(columnHeader.Bounds, hoverSurface.Bounds);
            var resizer = Assert.Single(columnHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            Assert.True(resizer.IsVisible);
            var lastColumnHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>().Single(candidate =>
                ReferenceEquals(candidate.Column, table.Columns[^1]));
            var lastColumnGrip = Assert.Single(lastColumnHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            Assert.True(lastColumnGrip.IsVisible);
            Assert.Equal(new Thickness(0, 0, -6, 0), resizer.Margin);
            Assert.Equal(12, resizer.Bounds.Width);
            Assert.Equal(0, resizer.Bounds.Y);
            Assert.Equal(columnHeader.Bounds.Height, resizer.Bounds.Height);
            Assert.Equal(headerStrip.Bounds.Height, columnHeader.Bounds.Height);
            Assert.NotSame(hoverBrush, hoverSurface.Background);
            Assert.NotSame(hoverBrush, headerStrip.Background);
            Assert.All(otherHoverSurfaces, surface => Assert.NotSame(hoverBrush, surface.Background));

            // The full header rectangle, including its top and bottom edges, is the hit target.
            var point = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width - 2, 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();

            Assert.True(columnHeader.IsPointerOver);
            Assert.Same(hoverBrush, hoverSurface.Background);
            Assert.NotSame(hoverBrush, headerStrip.Background);
            Assert.All(otherHoverSurfaces, surface => Assert.NotSame(hoverBrush, surface.Background));
            Assert.Equal(columnHeader.Bounds, hoverSurface.Bounds);

            var bottomPoint = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width - 2, columnHeader.Bounds.Height - 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseMove(bottomPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(columnHeader.IsPointerOver);
            Assert.Same(hoverBrush, hoverSurface.Background);

            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Name == "PART_FilterButton"));
            var filterButtonPoint = filterButton.TranslatePoint(
                new Point(filterButton.Bounds.Width / 2, filterButton.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Filter button has no window position.");
            window.MouseMove(filterButtonPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(filterButton.IsPointerOver);
            Assert.True(columnHeader.IsPointerOver);
            Assert.NotSame(hoverBrush, hoverSurface.Background);

            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();

            Assert.True(columnHeader.IsPointerOver);
            Assert.Same(hoverBrush, hoverSurface.Background);

            window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
            Dispatcher.UIThread.RunJobs();

            Assert.False(columnHeader.IsPointerOver);
            Assert.NotSame(hoverBrush, hoverSurface.Background);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Single_column_header_hover_fills_the_full_column_header_bounds()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        SolidColorBrush hoverBrush = new(Colors.Orange);
        table.Resources["SystemControlHighlightListLowBrush"] = hoverBrush;
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columnHeader = Assert.Single(table.GetVisualDescendants().OfType<TableViewColumnHeader>());
            var headerStrip = Assert.Single(columnHeader.GetSelfAndVisualAncestors().OfType<Border>()
                .Where(border => border.GetVisualParent() is DockPanel { Name: "ContentPanel" }));
            var hoverSurface = Assert.Single(columnHeader.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => presenter.Name == "PART_ContentPresenter" && presenter.Bounds.Size == columnHeader.Bounds.Size && presenter.BorderThickness == default);
            var point = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width / 2, columnHeader.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");

            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(hoverBrush, hoverSurface.Background);
            Assert.Equal(columnHeader.Bounds, hoverSurface.Bounds);
            Assert.Equal(headerStrip.Bounds.Height, columnHeader.Bounds.Height);
            Assert.Equal(columnHeader.TranslatePoint(default, window), hoverSurface.TranslatePoint(default, window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void All_column_headers_are_templated_with_many_columns()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        for (var index = source.Columns.Count; index < 11; index++)
            source.Columns.Add(DynamicTableViewColumn<DynamicTableViewTestRow>.Create($"extra-{index}", $"Extra {index}", static row => row.Id));
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 1200, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AssertVisibleHeaders(table, source.Columns.Select(static column => column.Header.ToString() ?? string.Empty));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Restoring_reordered_state_keeps_all_column_headers_visible()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        for (var index = source.Columns.Count; index < 11; index++)
            source.Columns.Add(DynamicTableViewColumn<DynamicTableViewTestRow>.Create($"extra-{index}", $"Extra {index}", static row => row.Id));
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 1200, Height = 320, Content = table };
        var expectedHeaders = source.Columns.Select(static column => column.Header.ToString() ?? string.Empty).ToArray();
        var savedColumns = source.Columns
            .Select((column, index) => new DynamicTableViewColumnState(column.Key, index, 140))
            .ToArray();
        savedColumns[0] = savedColumns[0] with { Order = 1 };
        savedColumns[1] = savedColumns[1] with { Order = 0 };
        (expectedHeaders[0], expectedHeaders[1]) = (expectedHeaders[1], expectedHeaders[0]);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AssertVisibleHeaders(table, source.Columns.Select(static column => column.Header.ToString() ?? string.Empty));

            table.RestoreState(new(savedColumns, [], [], default));
            Dispatcher.UIThread.RunJobs();

            AssertVisibleHeaders(table, expectedHeaders);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Grid_line_visibility_styles_rows_cells_and_headers()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        var lineBrush = new SolidColorBrush(Colors.Orange);
        table.Resources["TableViewColumnHeaderSeparatorBackground"] = lineBrush;
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var cells = table.GetVisualDescendants().OfType<TableViewCell>().ToArray();
            var rows = table.GetVisualDescendants().OfType<TableViewRow>().ToArray();
            var headers = table.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();

            Assert.NotEmpty(cells);
            Assert.NotEmpty(rows);
            Assert.NotEmpty(headers);
            Assert.Equal(DynamicTableViewGridLinesVisibility.None, table.GridLinesVisibility);

            (DynamicTableViewGridLinesVisibility Visibility, Thickness RowThickness, Thickness CellThickness, Thickness HeaderThickness)[] modes =
            [
                (DynamicTableViewGridLinesVisibility.None, new Thickness(0), new Thickness(0), new Thickness(0)),
                (DynamicTableViewGridLinesVisibility.Horizontal, new Thickness(0, 0, 0, 1), new Thickness(0), new Thickness(0, 0, 0, 1)),
                (DynamicTableViewGridLinesVisibility.Vertical, new Thickness(0), new Thickness(0, 0, 1, 0), new Thickness(0, 0, 1, 0)),
                (DynamicTableViewGridLinesVisibility.All, new Thickness(0, 0, 0, 1), new Thickness(0, 0, 1, 0), new Thickness(0, 0, 1, 1))
            ];

            foreach (var mode in modes)
            {
                table.GridLinesVisibility = mode.Visibility;
                Dispatcher.UIThread.RunJobs();

                Assert.All(rows, row => Assert.Equal(mode.RowThickness, row.BorderThickness));
                Assert.All(cells, cell => Assert.Equal(mode.CellThickness, cell.BorderThickness));
                Assert.All(headers, header => Assert.Equal(mode.HeaderThickness, header.BorderThickness));
                if (mode.Visibility != DynamicTableViewGridLinesVisibility.None)
                {
                    if (mode.RowThickness.Bottom > 0)
                    {
                        Assert.All(rows, row =>
                        {
                            Assert.Same(lineBrush, row.BorderBrush);
                            var rowBorder = Assert.Single(row.GetVisualDescendants().OfType<Border>());
                            Assert.Equal(row.Bounds.Height, rowBorder.Bounds.Height);
                        });
                    }
                    if (mode.CellThickness.Right > 0)
                        Assert.All(cells, cell => Assert.Same(lineBrush, cell.BorderBrush));
                    if (mode.HeaderThickness.Right > 0 || mode.HeaderThickness.Bottom > 0)
                        Assert.All(headers, header => Assert.Same(lineBrush, header.BorderBrush));
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Caller_added_key_binding_executes_for_a_custom_gesture()
    {
        DynamicTableView table = new() { Focusable = true };
        CallbackCommand command = new();
        table.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.K, KeyModifiers.Control),
            Command = command
        });
        Window window = new() { Width = 300, Height = 160, Content = table };

        try
        {
            window.Show();
            table.Focus();
            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);

            Assert.Equal(1, command.ExecutionCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Default_ui_scheduler_publishes_bound_rows_on_the_avalonia_dispatcher()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = new DynamicTableViewSource<DynamicTableViewTestRow, string>(
            cache,
            static row => row.Id,
            DynamicTableViewTestData.CreateColumns(),
            workerScheduler: ImmediateScheduler.Instance,
            searchDebounce: TimeSpan.Zero,
            searchScheduler: ImmediateScheduler.Instance);

        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        Assert.Empty(source.Items);

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
    }

    [AvaloniaFact]
    public void State_restore_reorders_columns_and_restores_width_sort_and_filter()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        DynamicTableViewState state = new(
        [
            new("age", 0, 123),
            new("name", 1, 180)
        ],
        [new("age", ListSortDirection.Descending)],
        [new("age", DynamicTableViewFilterOperator.GreaterThan, 15)]);

        table.RestoreState(state);
        var captured = table.CaptureState();

        Assert.Equal(["age", "name"], source.Columns.Take(2).Select(static column => column.Key));
        Assert.Equal(123, table.Columns[0].Width.Value);
        Assert.Equal(ListSortDirection.Descending, Assert.Single(captured.Sorts).Direction);
        Assert.Equal(DynamicTableViewFilterOperator.GreaterThan, Assert.Single(captured.Filters).Operator);
    }

    [AvaloniaFact]
    public void Restored_table_headers_remain_visible_after_switching_tabs()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        TabControl tabs = new()
        {
            Items =
            {
                new TabItem { Header = "Resources", Content = table },
                new TabItem { Header = "Other", Content = new Border() }
            }
        };
        Window window = new() { Width = 640, Height = 360, Content = tabs };
        string[] initialHeaders = ["Name", "Age", "Created", "Enabled", "State"];
        string[] restoredHeaders = ["Age", "Name", "Created", "Enabled", "State"];
        var attachCount = 0;
        var detachCount = 0;
        table.AttachedToVisualTree += (_, _) => attachCount++;
        table.DetachedFromVisualTree += (_, _) => detachCount++;

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AssertVisibleHeaders(table, initialHeaders);

            table.RestoreState(new(
                [new("age", 0, 120), new("name", 1, 180)],
                [],
                [],
                default));
            Dispatcher.UIThread.RunJobs();

            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, detachCount);
            tabs.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, attachCount);
            AssertVisibleHeaders(table, restoredHeaders);
            Assert.Equal(["age", "name"], source.Columns.Take(2).Select(static column => column.Key));
            Assert.Equal(120, table.Columns[0].Width.Value);
            Assert.Equal(180, table.Columns[1].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void State_capture_and_restore_preserves_horizontal_and_vertical_scroll_offsets()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(Enumerable.Range(0, 100).Select(index =>
            new DynamicTableViewTestRow($"row-{index:D3}", $"Row {index}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)));

        DynamicTableView firstTable = new() { Source = source };
        Window firstWindow = new() { Width = 420, Height = 240, Content = firstTable };
        try
        {
            firstWindow.Show();
            Dispatcher.UIThread.RunJobs();
            var firstScrollViewer = Assert.Single(firstTable.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(firstScrollViewer.ScrollBarMaximum.Y > 0);
            Assert.InRange(firstTable.GetVisualDescendants().OfType<TableViewRow>().Count(), 1, 99);
            firstScrollViewer.Offset = new Vector(80, 200);
            Dispatcher.UIThread.RunJobs();
            var state = firstTable.CaptureState();

            Assert.Equal(firstScrollViewer.Offset, state.ScrollOffset);

            firstWindow.Close();
            Dispatcher.UIThread.RunJobs();
            DynamicTableView secondTable = new() { Source = source };
            Window secondWindow = new() { Width = 420, Height = 240, Content = secondTable };
            try
            {
                secondWindow.Show();
                Dispatcher.UIThread.RunJobs();
                secondTable.RestoreState(state);
                Dispatcher.UIThread.RunJobs();

                var secondScrollViewer = Assert.Single(secondTable.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.Equal(state.ScrollOffset, secondScrollViewer.Offset);
            }
            finally
            {
                secondWindow.Close();
            }
        }
        finally
        {
            firstWindow.Close();
        }
    }

    [AvaloniaFact]
    public void State_restore_waits_for_virtualized_extent_to_grow()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var scrollViewer = Assert.Single(table.GetVisualDescendants().OfType<ScrollViewer>());
            table.RestoreState(new([], [], [], new Vector(0, 160)));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(default, scrollViewer.Offset);

            cache.AddOrUpdate(Enumerable.Range(3, 100).Select(index =>
                new DynamicTableViewTestRow($"row-{index:D3}", $"Row {index}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(160, scrollViewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_width_modes_preserve_pixel_and_star_settings()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var columns = DynamicTableViewTestData.CreateColumns();
        columns[0].WidthMode = DynamicTableViewWidthMode.Pixel;
        columns[0].Width = 155;
        columns[1].WidthMode = DynamicTableViewWidthMode.Star;
        columns[1].Width = 2;
        using var source = DynamicTableViewTestData.CreateSource(cache, columns);
        DynamicTableView table = new() { Source = source };

        Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
        Assert.Equal(155, table.Columns[0].Width.Value);
        Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
        Assert.Equal(2, table.Columns[1].Width.Value);
    }

    [AvaloniaFact]
    public void Restoring_saved_star_columns_after_widening_the_window_preserves_proportions_and_fills_view()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 2;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var originalState = table.CaptureState();
            Assert.Equal(DynamicTableViewWidthMode.Star, originalState.Columns[0].WidthMode);
            Assert.Equal(1, originalState.Columns[0].Width);
            Assert.Equal(DynamicTableViewWidthMode.Star, originalState.Columns[1].WidthMode);
            Assert.Equal(2, originalState.Columns[1].Width);

            window.Width = 960;
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(originalState);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
            Assert.InRange(table.Columns[1].ActualWidth / table.Columns[0].ActualWidth, 1.95, 2.05);
            var headers = table.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();
            var firstHeaderLeft = headers[0].TranslatePoint(default, table)?.X
                ?? throw new InvalidOperationException("First header has no table position.");
            var lastHeaderRight = headers[^1].TranslatePoint(
                new Point(headers[^1].Bounds.Width, 0), table)?.X
                ?? throw new InvalidOperationException("Last header has no table position.");
            Assert.InRange(
                Math.Abs(table.Columns.Sum(static column => column.ActualWidth) - (lastHeaderRight - firstHeaderLeft)),
                0,
                2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Hidden_star_columns_do_not_reduce_visible_star_column_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        first.MinWidth = 120;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        second.MinWidth = 120;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double firstWidthWithBothVisible = table.Columns[0].ActualWidth;

            table.Columns[1].IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > firstWidthWithBothVisible + 100);
            Assert.False(table.Columns[1].IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resize_grip_shows_resize_cursor_when_hovered()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source, GridLinesVisibility = DynamicTableViewGridLinesVisibility.All };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[1]));
            var leftHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizer = Assert.Single(leftHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            Assert.True(resizer.IsVisible);
            Assert.Equal(12, resizer.Bounds.Width);
            var gripCenter = resizer.TranslatePoint(new Point(resizer.Bounds.Width / 2, resizer.Bounds.Height / 2), leftHeader)
                ?? throw new InvalidOperationException("Resize grip has no header position.");
            Assert.InRange(gripCenter.X, leftHeader.Bounds.Width - 1, leftHeader.Bounds.Width + 1);
            Assert.Equal(0, resizer.Bounds.Y);
            Assert.InRange(resizer.Bounds.Height, leftHeader.Bounds.Height - 1, leftHeader.Bounds.Height);
            var dividerCenter = header.TranslatePoint(new Point(0, header.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Divider has no window position.");
            Point leftPoint = new(dividerCenter.X - 6, dividerCenter.Y);
            window.MouseMove(leftPoint);
            Dispatcher.UIThread.RunJobs();
            Assert.True(leftHeader.Classes.Contains("dynamic-table-view-resize-pointerover"));
            Assert.Equal(new Cursor(StandardCursorType.SizeWestEast).ToString(), leftHeader.Cursor?.ToString());

            Point rightPoint = new(dividerCenter.X + 6, dividerCenter.Y);
            window.MouseMove(rightPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(header.Classes.Contains("dynamic-table-view-resize-pointerover"));
            Assert.Equal(new Cursor(StandardCursorType.SizeWestEast).ToString(), header.Cursor?.ToString());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_a_star_column_manually_saves_and_restores_it_as_pixels()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizeGrip = Assert.Single(header.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            var resizeStart = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2 + 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeEnd = new(resizeStart.X + 3, resizeStart.Y);

            Assert.True(table.CanUserResizeColumns);
            window.MouseDown(resizeStart, MouseButton.Left);
            window.MouseUp(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DynamicTableViewWidthMode.Star, source.Columns[1].WidthMode);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);

            window.MouseDown(resizeStart, MouseButton.Left);
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            DynamicTableViewState state = table.CaptureState();
            Assert.Equal(DynamicTableViewWidthMode.Pixel, state.Columns[0].WidthMode);

            window.Width = 960;
            Dispatcher.UIThread.RunJobs();
            table.RestoreState(state);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
            Assert.Equal(state.Columns[0].Width, table.Columns[0].Width.Value);
            Assert.Equal(GridUnitType.Star, table.Columns[1].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_between_memory_and_created_changes_memory_column_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> memory = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "memory", "Memory", static row => row.Age);
        memory.WidthMode = DynamicTableViewWidthMode.Pixel;
        memory.Width = 150;
        DynamicTableViewColumn<DynamicTableViewTestRow> created = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "created", "Created", static row => row.CreatedAt);
        created.WidthMode = DynamicTableViewWidthMode.Pixel;
        created.Width = 180;
        using var source = DynamicTableViewTestData.CreateSource(cache, [memory, created]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var memoryHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizeGrip = Assert.Single(memoryHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            Point dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Memory header has no window position.");
            Point resizeStart = new(dividerCenter.X - 6, dividerCenter.Y);
            Point resizeEnd = new(resizeStart.X + 30, resizeStart.Y);
            double initialMemoryWidth = table.Columns[0].ActualWidth;
            double initialCreatedWidth = table.Columns[1].ActualWidth;
            Point resizeMidpoint = new(resizeStart.X + 10, resizeStart.Y);

            window.MouseDown(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Cursor(StandardCursorType.SizeWestEast).ToString(), table.Cursor?.ToString());
            window.MouseMove(resizeMidpoint, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Cursor(StandardCursorType.SizeWestEast).ToString(), table.Cursor?.ToString());
            Assert.True(table.Columns[0].ActualWidth > initialMemoryWidth,
                "Dragging from the left side of the Memory/Created divider should widen Memory.");
            Assert.Equal(initialCreatedWidth, table.Columns[1].ActualWidth);

            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(table.Classes.Contains("dynamic-table-view-column-resizing"));

            Assert.True(table.Columns[0].ActualWidth > initialMemoryWidth);
            Assert.Equal(initialCreatedWidth, table.Columns[1].ActualWidth);

            double memoryWidthAfterLeftSideDrag = table.Columns[0].ActualWidth;
            memoryHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            resizeGrip = Assert.Single(memoryHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));
            dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Memory header has no window position after resizing.");
            Point rightSideStart = new(dividerCenter.X + 6, dividerCenter.Y);
            Point rightSideMove = new(rightSideStart.X + 10, rightSideStart.Y);
            window.MouseDown(rightSideStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Cursor(StandardCursorType.SizeWestEast).ToString(), table.Cursor?.ToString());
            window.MouseMove(rightSideMove, RawInputModifiers.LeftMouseButton);
            window.MouseUp(rightSideMove, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(table.Columns[0].ActualWidth > memoryWidthAfterLeftSideDrag,
                "Dragging from the right side of the Memory/Created divider should also widen Memory.");
            Assert.Equal(initialCreatedWidth, table.Columns[1].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_second_star_column_when_first_is_also_star_resizes_correct_column()
    {
        // Scenario: first two columns are Star, third is Header, fourth is Cells
        // User resizes the divider between the two Star columns (second Star column)
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> firstStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first-star", "First Star", static row => row.Name);
        firstStar.WidthMode = DynamicTableViewWidthMode.Star;
        firstStar.Width = 1;
        firstStar.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> secondStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second-star", "Second Star", static row => row.Id);
        secondStar.WidthMode = DynamicTableViewWidthMode.Star;
        secondStar.Width = 2;
        secondStar.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> headerColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "Header", static row => row.Name);
        headerColumn.WidthMode = DynamicTableViewWidthMode.Header;
        headerColumn.MinWidth = 100;

        DynamicTableViewColumn<DynamicTableViewTestRow> cellsColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static row => row.Name);
        cellsColumn.WidthMode = DynamicTableViewWidthMode.Cells;
        cellsColumn.MinWidth = 100;

        using var source = DynamicTableViewTestData.CreateSource(cache, [firstStar, secondStar, headerColumn, cellsColumn]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 800, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Get the second Star column header (index 1)
            var secondStarHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[1]));
            var resizeGrip = Assert.Single(secondStarHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));

            // Click on the LEFT side of the grip (should resize the second Star column)
            Point dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeStart = new(dividerCenter.X - 6, dividerCenter.Y); // Left side of divider
            Point resizeEnd = new(resizeStart.X + 30, resizeStart.Y); // Drag right to widen

            double initialFirstStarWidth = table.Columns[0].ActualWidth;
            double initialSecondStarWidth = table.Columns[1].ActualWidth;
            double initialHeaderWidth = table.Columns[2].ActualWidth;
            double initialCellsWidth = table.Columns[3].ActualWidth;

            window.MouseDown(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[1].ActualWidth > initialSecondStarWidth,
                "Dragging right on the divider between first and second Star should widen the second Star column.");
            Assert.Equal(initialFirstStarWidth, table.Columns[0].ActualWidth, precision: 1);
            
            // Header and Cells columns should remain unchanged
            Assert.Equal(initialHeaderWidth, table.Columns[2].ActualWidth, precision: 1);
            Assert.Equal(initialCellsWidth, table.Columns[3].ActualWidth, precision: 1);

            // The second Star column should now be in Pixel mode (manually sized)
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[1].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[1].Width.GridUnitType);
            
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_between_star_and_header_resizes_star_column()
    {
        // Scenario: first column is Star, second is Header
        // User resizes the divider between Star and Header
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.WidthMode = DynamicTableViewWidthMode.Star;
        star.Width = 1;
        star.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> header = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "Header", static row => row.Name);
        header.WidthMode = DynamicTableViewWidthMode.Header;
        header.MinWidth = 100;

        DynamicTableViewColumn<DynamicTableViewTestRow> cells = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static row => row.Name);
        cells.WidthMode = DynamicTableViewWidthMode.Cells;
        cells.MinWidth = 100;

        using var source = DynamicTableViewTestData.CreateSource(cache, [star, header, cells]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 800, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Get the Star column header (index 0) - its grip is the divider between Star and Header
            var starHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizeGrip = Assert.Single(starHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));

            Point dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeStart = new(dividerCenter.X - 6, dividerCenter.Y); // Left side of divider
            Point resizeEnd = new(resizeStart.X + 30, resizeStart.Y); // Drag right to widen

            double initialStarWidth = table.Columns[0].ActualWidth;
            double initialHeaderWidth = table.Columns[1].ActualWidth;
            double initialCellsWidth = table.Columns[2].ActualWidth;

            window.MouseDown(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // The Star column (index 0) should widen
            Assert.True(table.Columns[0].ActualWidth > initialStarWidth,
                "Dragging right on the divider between Star and Header should widen the Star column.");
            
            // Header and Cells columns should remain unchanged
            Assert.Equal(initialHeaderWidth, table.Columns[1].ActualWidth, precision: 1);
            Assert.Equal(initialCellsWidth, table.Columns[2].ActualWidth, precision: 1);

            // The Star column should now be in Pixel mode (manually sized)
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_between_star_and_cells_resizes_star_column()
    {
        // Scenario: first column is Star, second is Cells
        // User resizes the divider between Star and Cells
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> star = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "star", "Star", static row => row.Name);
        star.WidthMode = DynamicTableViewWidthMode.Star;
        star.Width = 1;
        star.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> cells = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "cells", "Cells", static row => row.Name);
        cells.WidthMode = DynamicTableViewWidthMode.Cells;
        cells.MinWidth = 100;

        DynamicTableViewColumn<DynamicTableViewTestRow> header = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "Header", static row => row.Name);
        header.WidthMode = DynamicTableViewWidthMode.Header;
        header.MinWidth = 100;

        using var source = DynamicTableViewTestData.CreateSource(cache, [star, cells, header]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 800, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Get the Star column header (index 0) - its grip is the divider between Star and Cells
            var starHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizeGrip = Assert.Single(starHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));

            Point dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeStart = new(dividerCenter.X - 6, dividerCenter.Y); // Left side of divider
            Point resizeEnd = new(resizeStart.X + 30, resizeStart.Y); // Drag right to widen

            double initialStarWidth = table.Columns[0].ActualWidth;
            double initialCellsWidth = table.Columns[1].ActualWidth;
            double initialHeaderWidth = table.Columns[2].ActualWidth;

            window.MouseDown(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // The Star column (index 0) should widen
            Assert.True(table.Columns[0].ActualWidth > initialStarWidth,
                "Dragging right on the divider between Star and Cells should widen the Star column.");
            
            // Cells and Header columns should remain unchanged
            Assert.Equal(initialCellsWidth, table.Columns[1].ActualWidth, precision: 1);
            Assert.Equal(initialHeaderWidth, table.Columns[2].ActualWidth, precision: 1);

            // The Star column should now be in Pixel mode (manually sized)
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resizing_second_star_column_right_side_also_resizes_second_star()
    {
        // Scenario: first two columns are Star
        // User clicks on the RIGHT side of the second Star column's grip (should still resize second Star)
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> firstStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first-star", "First Star", static row => row.Name);
        firstStar.WidthMode = DynamicTableViewWidthMode.Star;
        firstStar.Width = 1;
        firstStar.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> secondStar = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second-star", "Second Star", static row => row.Id);
        secondStar.WidthMode = DynamicTableViewWidthMode.Star;
        secondStar.Width = 2;
        secondStar.MinWidth = 80;

        DynamicTableViewColumn<DynamicTableViewTestRow> headerColumn = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "header", "Header", static row => row.Name);
        headerColumn.WidthMode = DynamicTableViewWidthMode.Header;
        headerColumn.MinWidth = 100;

        using var source = DynamicTableViewTestData.CreateSource(cache, [firstStar, secondStar, headerColumn]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = true };
        Window window = new() { Width = 800, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Get the second Star column header (index 1)
            var secondStarHeader = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[1]));
            var resizeGrip = Assert.Single(secondStarHeader.GetVisualDescendants().OfType<Thumb>(),
                thumb => thumb.Classes.Contains("dynamic-table-view-column-resize-grip"));

            // Click on the RIGHT side of the grip (should still resize the second Star column)
            Point dividerCenter = resizeGrip.TranslatePoint(
                new Point(resizeGrip.Bounds.Width / 2, resizeGrip.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeStart = new(dividerCenter.X + 6, dividerCenter.Y); // Right side of divider
            Point resizeEnd = new(resizeStart.X + 30, resizeStart.Y); // Drag right to widen

            double initialFirstStarWidth = table.Columns[0].ActualWidth;
            double initialSecondStarWidth = table.Columns[1].ActualWidth;
            double initialHeaderWidth = table.Columns[2].ActualWidth;

            window.MouseDown(resizeStart, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[1].ActualWidth > initialSecondStarWidth,
                "Dragging right on the right side of the second Star divider should also widen the second Star column.");
            Assert.Equal(initialFirstStarWidth, table.Columns[0].ActualWidth, precision: 1);
            
            // Header column should remain unchanged
            Assert.Equal(initialHeaderWidth, table.Columns[2].ActualWidth, precision: 1);

            // The second Star column should now be in Pixel mode (manually sized)
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[1].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[1].Width.GridUnitType);
            
            Assert.Equal(DynamicTableViewWidthMode.Pixel, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Pixel, table.Columns[0].Width.GridUnitType);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Column_resizing_is_disabled_when_CanUserResizeColumns_is_false()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> first = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "first", "First", static row => row.Name);
        first.WidthMode = DynamicTableViewWidthMode.Star;
        first.Width = 1;
        DynamicTableViewColumn<DynamicTableViewTestRow> second = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "second", "Second", static row => row.Id);
        second.WidthMode = DynamicTableViewWidthMode.Star;
        second.Width = 1;
        using var source = DynamicTableViewTestData.CreateSource(cache, [first, second]);
        DynamicTableView table = new() { Source = source, CanUserResizeColumns = false };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .Single(candidate => ReferenceEquals(candidate.Column, table.Columns[0]));
            var resizeStart = header.TranslatePoint(
                new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point resizeEnd = new(resizeStart.X + 70, resizeStart.Y);

            window.MouseDown(resizeStart, MouseButton.Left);
            window.MouseMove(resizeEnd, RawInputModifiers.LeftMouseButton);
            window.MouseUp(resizeEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(table.CanUserResizeColumns);
            Assert.Equal(DynamicTableViewWidthMode.Star, source.Columns[0].WidthMode);
            Assert.Equal(GridUnitType.Star, table.Columns[0].Width.GridUnitType);
            Assert.Equal(1, table.Columns[0].Width.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Auto_width_grows_from_realized_cells_and_honors_header_and_pixel_modes()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var automatic = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        automatic.WidthMode = DynamicTableViewWidthMode.Auto;
        automatic.MinWidth = 30;
        var headerOnly = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "id", "Id", static row => row.Id);
        headerOnly.WidthMode = DynamicTableViewWidthMode.Header;
        headerOnly.MinWidth = 30;
        var fixedWidth = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "age", "Age", static row => row.Age);
        fixedWidth.WidthMode = DynamicTableViewWidthMode.Pixel;
        fixedWidth.Width = 123;
        fixedWidth.MinWidth = 30;
        using var source = DynamicTableViewTestData.CreateSource(cache, [automatic, headerOnly, fixedWidth]);
        var item = DynamicTableViewTestData.CreateRows()[0] with { Id = new string('I', 80), Name = "Short" };
        cache.AddOrUpdate(item);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 700, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var initialAutoWidth = table.Columns[0].ActualWidth;

            cache.AddOrUpdate(item with { Name = new string('W', 80) });
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > initialAutoWidth);
            Assert.True(table.Columns[0].ActualWidth > table.Columns[1].ActualWidth);
            Assert.Equal(123, table.Columns[2].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_the_only_pixel_column_to_auto_starts_measuring_on_next_layout()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = 80;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows()[0] with { Name = new string('W', 100) });
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, table.Columns[0].ActualWidth);

            column.WidthMode = DynamicTableViewWidthMode.Auto;
            window.Height += 10;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.True(table.Columns[0].ActualWidth > 80);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void Dragging_a_header_reorders_the_source_and_native_columns(int from, int to)
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        foreach (var column in source.Columns)
        {
            column.WidthMode = DynamicTableViewWidthMode.Pixel;
            column.Width = 120 + 10 * source.Columns.IndexOf(column);
        }
        var expected = source.Columns.ToList();
        var moved = expected[from];
        expected.RemoveAt(from);
        expected.Insert(to, moved);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 900, Height = 300, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            DragHeader(from, to);
            AssertOrder();
            var saved = table.CaptureState();
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            window.Content = table;
            Dispatcher.UIThread.RunJobs();
            AssertOrder();
            table.RestoreState(saved);
            Dispatcher.UIThread.RunJobs();
            AssertOrder();

            // A second gesture must use the new order and preserve column identity and widths.
            expected.RemoveAt(to);
            expected.Insert(from, moved);
            DragHeader(to, from);
            AssertOrder();
            Assert.Empty(source.SortDescriptors);
        }
        finally
        {
            window.Close();
        }

        void DragHeader(int startIndex, int targetIndex)
        {
            var start = HeaderCenter(startIndex);
            var target = HeaderCenter(targetIndex);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        Point HeaderCenter(int index)
        {
            var header = GetHeaderControl(table, index);
            return header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        }

        void AssertOrder()
        {
            Assert.Equal(expected, source.Columns);
            Assert.Equal(expected, table.Columns.Select(column => column.Header));
            Assert.Equal(expected.Select(column => column.Width), table.Columns.Select(column => column.Width.Value));
            var headers = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
                .OrderBy(header => header.TranslatePoint(default, table)!.Value.X).ToArray();
            Assert.Equal(expected, headers.Select(header => header.Column!.Header));
            var rows = table.GetVisualDescendants().OfType<TableViewRow>()
                .Where(row => row.DataContext is DynamicTableViewTestRow).ToArray();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var cells = row.GetVisualDescendants().OfType<TableViewCell>()
                    .OrderBy(cell => cell.TranslatePoint(default, table)!.Value.X);
                Assert.Equal(expected, cells.Select(cell => cell.Column!.Header));
            }
            Assert.Equal(expected.Select(column => column.Key), table.CaptureState().Columns.Select(column => column.Key));
        }
    }

    [AvaloniaFact]
    public void Virtualized_cells_rebind_to_current_rows_after_scrolling()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var name = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        using var source = DynamicTableViewTestData.CreateSource(cache, [name]);
        cache.AddOrUpdate(Enumerable.Range(0, 1_000).Select(index =>
            new DynamicTableViewTestRow($"row-{index:D4}", $"Item {index:D4}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)));
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var scrollViewer = Assert.Single(table.GetVisualDescendants().OfType<ScrollViewer>());
            var firstRows = table.GetVisualDescendants().OfType<TableViewRow>().ToArray();
            Assert.InRange(firstRows.Length, 1, 99);
            scrollViewer.Offset = new Vector(0, 8_000);
            Dispatcher.UIThread.RunJobs();

            var visibleRows = table.GetVisualDescendants().OfType<TableViewRow>().ToArray();
            Assert.InRange(visibleRows.Length, 1, 99);
            Assert.All(visibleRows, row =>
            {
                var item = Assert.IsType<DynamicTableViewTestRow>(row.DataContext);
                Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == item.Name);
            });
            Assert.Contains(visibleRows, row => ((DynamicTableViewTestRow)row.DataContext!).Id.CompareTo("row-0100") > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Updating_rows_and_changing_selection_do_not_scroll_offscreen_items_into_view()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = new DynamicTableViewSource<DynamicTableViewTestRow, string>(
            cache,
            static row => row.Id,
            DynamicTableViewTestData.CreateColumns(),
            workerScheduler: ImmediateScheduler.Instance,
            searchDebounce: TimeSpan.Zero,
            searchScheduler: ImmediateScheduler.Instance);
        var rows = Enumerable.Range(0, 1_000)
            .Select(index => new DynamicTableViewTestRow(
                $"row-{index:D4}", $"Item {index:D4}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready))
            .ToArray();
        cache.AddOrUpdate(rows);
        source.SetSort([new("name", ListSortDirection.Ascending)]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var scrollViewer = Assert.Single(table.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(1_000, source.Items.Cast<DynamicTableViewTestRow>().Count());
            Assert.True(scrollViewer.ScrollBarMaximum.Y > 0);
            Assert.False(table.AutoScrollToSelectedItem);
            source.SelectionModel.Select(500);
            Dispatcher.UIThread.RunJobs();
            var scrolledOffset = scrollViewer.Offset.Y;

            Assert.Equal(0, scrolledOffset);

            cache.AddOrUpdate(rows[0] with { Name = "Zzz updated first row" });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(scrolledOffset, scrollViewer.Offset.Y);
            Assert.Equal("row-0500", Assert.IsType<DynamicTableViewTestRow>(source.SelectionModel.SelectedItem).Id);

            source.SelectionModel.SelectedIndex = 900;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(scrolledOffset, scrollViewer.Offset.Y);
            Assert.Equal("row-0901", Assert.IsType<DynamicTableViewTestRow>(source.SelectionModel.SelectedItem).Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Single_and_multiple_selection_follow_the_selection_model()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };

        source.SelectionModel.SingleSelect = true;
        source.SelectionModel.Select(0);
        source.SelectionModel.Select(1);
        Assert.Single(source.SelectionModel.SelectedItems);

        source.SelectionModel.SingleSelect = false;
        source.SelectionModel.Clear();
        source.SelectionModel.Select(0);
        source.SelectionModel.Select(2);
        Assert.Equal(2, source.SelectionModel.SelectedItems.Count);
    }

    [AvaloniaFact]
    public void Dragging_in_single_select_mode_follows_the_pointer_in_both_directions()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(Enumerable.Range(0, 12).Select(index =>
            new DynamicTableViewTestRow($"row-{index:D2}", $"Item {index:D2}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)));
        source.SelectionModel.SingleSelect = true;
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 700, Height = 420, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var first = GetRowCenter(window, table, "row-00");
            var fifth = GetRowCenter(window, table, "row-04");
            var tenth = GetRowCenter(window, table, "row-09");
            window.MouseDown(fifth, MouseButton.Left);
            window.MouseMove(tenth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(9, source.SelectionModel.SelectedIndex);

            window.MouseMove(first, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, source.SelectionModel.SelectedIndex);
            window.MouseUp(first, MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dragging_across_rows_selects_and_deselects_range_while_left_button_is_held()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(Enumerable.Range(0, 12).Select(index =>
            new DynamicTableViewTestRow($"row-{index:D2}", $"Item {index:D2}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)));
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 700, Height = 420, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var first = GetRowCenter(window, table, "row-00");
            var tenth = GetRowCenter(window, table, "row-09");
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(tenth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(10, source.SelectionModel.SelectedItems.Count);
            Assert.Equal(Enumerable.Range(0, 10), source.SelectionModel.SelectedIndexes.Order());

            var fifth = GetRowCenter(window, table, "row-04");
            window.MouseMove(fifth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Enumerable.Range(0, 5), source.SelectionModel.SelectedIndexes.Order());

            window.MouseMove(tenth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Enumerable.Range(0, 10), source.SelectionModel.SelectedIndexes.Order());

            window.MouseUp(tenth, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            window.MouseDown(fifth, MouseButton.Left);
            window.MouseMove(tenth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Enumerable.Range(4, 6), source.SelectionModel.SelectedIndexes.Order());
            window.MouseUp(tenth, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var selectedStart = GetRowCenter(window, table, "row-04");
            var selectedEnd = GetRowCenter(window, table, "row-09");
            window.MouseDown(selectedStart, MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(selectedEnd, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(source.SelectionModel.SelectedItems);
            window.MouseUp(selectedEnd, MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Plain_click_before_live_reorder_does_not_restore_previous_multi_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        ManualScheduler uiScheduler = new();
        using var source = DynamicTableViewTestData.CreateSource(
            cache, workerScheduler: ImmediateScheduler.Instance, uiScheduler: uiScheduler);
        var rows = Enumerable.Range(0, 6)
            .Select(index => new DynamicTableViewTestRow(
                $"row-{index:D2}", $"Item {index:D2}", index, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready))
            .ToArray();
        cache.AddOrUpdate(rows);
        uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().Count() == rows.Length);
        source.SetSort([new("age", ListSortDirection.Ascending)]);
        uiScheduler.RunUntilIdle();

        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 700, Height = 420, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var first = GetRowCenter(window, table, "row-00");
            var fifth = GetRowCenter(window, table, "row-04");
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(fifth, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(fifth, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, source.SelectionModel.SelectedItems.Count);

            var newlyClickedRow = GetRowCenter(window, table, "row-05");
            window.MouseDown(newlyClickedRow, MouseButton.Left);
            window.MouseUp(newlyClickedRow, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            string[] expectedIds = ["row-05"];
            Assert.Equal(expectedIds, source.SelectionModel.SelectedItems
                .Cast<DynamicTableViewTestRow>()
                .Select(static row => row.Id)
                .Order(StringComparer.Ordinal));

            cache.Edit(updater =>
            {
                for (var index = 0; index < 5; index++)
                    updater.AddOrUpdate(rows[index] with { Age = rows[index].Age + 20 });
            });
            uiScheduler.RunUntil(() => source.Items.Cast<DynamicTableViewTestRow>().First().Id == "row-05");
            Dispatcher.UIThread.RunJobs();

            uiScheduler.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedIds, source.SelectionModel.SelectedItems
                .Cast<DynamicTableViewTestRow>()
                .Select(static row => row.Id)
                .Order(StringComparer.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Header_click_cycles_ascending_descending_and_unsorted()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate([
            DynamicTableViewTestData.CreateRows()[0] with { Name = "Gamma" },
            DynamicTableViewTestData.CreateRows()[1] with { Name = "Alpha" },
            DynamicTableViewTestData.CreateRows()[2] with { Name = "Beta" }]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 500, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var nameHeader = GetHeaderControl(table, 0);
            ClickHeader(window, nameHeader, 12);
            Assert.Equal(["b", "c", "a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

            ClickHeader(window, nameHeader, 12);
            Assert.Equal(["a", "c", "b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

            ClickHeader(window, nameHeader, 12);
            Assert.Equal(["a", "b", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Sort_arrow_stays_beside_filter_button_in_a_narrow_header()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.WidthMode = DynamicTableViewWidthMode.Pixel;
        column.Width = column.MinWidth;
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 240, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Name == "PART_FilterButton"));

            for (int click = 0; click < 2; click++)
            {
                ClickHeader(window, header, 5);
                Dispatcher.UIThread.RunJobs();

                PathIcon sortArrow = Assert.Single(header.GetVisualDescendants().OfType<PathIcon>()
                    .Where(static icon => icon.IsVisible && !icon.Classes.Contains("dynamic-table-view-filter-icon")));
                ListSortDirection expectedDirection = click == 0
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
                Assert.Equal(expectedDirection, Assert.Single(source.SortDescriptors).Direction);

                Point sortArrowPosition = sortArrow.TranslatePoint(default, header)
                    ?? throw new InvalidOperationException("Sort arrow has no header position.");
                Point filterButtonPosition = filterButton.TranslatePoint(default, header)
                    ?? throw new InvalidOperationException("Filter button has no header position.");

                Assert.True(filterButton.IsVisible);
                Assert.True(sortArrowPosition.X >= 0);
                Assert.True(sortArrowPosition.X + sortArrow.Bounds.Width <= filterButtonPosition.X);
                Assert.True(filterButtonPosition.X + filterButton.Bounds.Width <= header.Bounds.Width);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Multiple_sorts_are_disabled_by_default_and_the_clicked_column_replaces_the_previous_sort()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate([
            new DynamicTableViewTestRow("a", "Zeta", 30, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready),
            new DynamicTableViewTestRow("b", "Alpha", 10, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready),
            new DynamicTableViewTestRow("c", "Beta", 20, DateTimeOffset.UnixEpoch, true, DynamicTableViewTestState.Ready)]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(table.AllowMultipleSorts);
            ClickHeader(window, GetHeaderControl(table, 0), 12);
            ClickHeader(window, GetHeaderControl(table, 1), 12);

            var activeSort = Assert.Single(source.SortDescriptors);
            Assert.Equal("age", activeSort.ColumnKey);
            Assert.Equal(ListSortDirection.Ascending, activeSort.Direction);

            ClickHeader(window, GetHeaderControl(table, 1), 12);
            activeSort = Assert.Single(source.SortDescriptors);
            Assert.Equal(ListSortDirection.Descending, activeSort.Direction);

            ClickHeader(window, GetHeaderControl(table, 1), 12);
            Assert.Empty(source.SortDescriptors);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Enabling_multiple_sorts_allows_two_sort_descriptors()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source, AllowMultipleSorts = true };
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ClickHeader(window, GetHeaderControl(table, 0), 12);
            ClickHeader(window, GetHeaderControl(table, 1), 12);

            Assert.Equal(["name", "age"], source.SortDescriptors.Select(static descriptor => descriptor.ColumnKey));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Custom_filter_flyout_factory_is_attached_and_opened_from_header()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        var column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create("name", "Name", static row => row.Name);
        var factoryCalls = 0;
        column.FilterFlyoutFactory = _ =>
        {
            factoryCalls++;
            return new Border();
        };
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var attachedFlyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            Assert.Equal(0, factoryCalls);

            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            Assert.True(attachedFlyout.IsOpen);
            Assert.IsType<Border>(attachedFlyout.Content);
            Assert.Equal(1, factoryCalls);
            attachedFlyout.Hide();
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, factoryCalls);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standard_filter_flyout_uses_its_default_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var flyoutContent = Assert.IsAssignableFrom<Control>(flyout.Content);
            var panel = Assert.Single(flyoutContent.GetVisualDescendants().OfType<StackPanel>()
                .Where(static candidate => candidate.Children.OfType<TextBlock>().Any()));
            Assert.Equal(280, panel.Width);
            Assert.Equal(280, panel.Bounds.Width);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standard_filter_flyout_uses_table_width_override()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        table.Resources["DynamicTableView.FilterFlyoutWidth"] = 360d;
        Window window = new() { Width = 640, Height = 320, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var panel = Assert.Single(content.GetVisualDescendants().OfType<StackPanel>()
                .Where(static candidate => candidate.Children.OfType<TextBlock>().Any()));

            Assert.Equal(360, panel.Width);
            Assert.Equal(360, panel.Bounds.Width);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standard_filter_dropdowns_fill_the_flyout_width()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 3);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var flyoutContent = Assert.IsAssignableFrom<Control>(flyout.Content);
            var panel = Assert.Single(flyoutContent.GetVisualDescendants().OfType<StackPanel>()
                .Where(static candidate => candidate.Children.OfType<TextBlock>().Any()));
            var operatorBox = Assert.Single(flyoutContent.GetVisualDescendants().OfType<ComboBox>()
                .Where(static comboBox => comboBox.Name == "PART_OperatorBox"));
            var choiceBox = Assert.Single(flyoutContent.GetVisualDescendants().OfType<ComboBox>()
                .Where(static comboBox => comboBox.Name == "PART_ChoiceBox"));

            Assert.Equal(280, panel.Bounds.Width);
            Assert.True(operatorBox.IsVisible);
            Assert.True(choiceBox.IsVisible);
            Assert.Equal(panel.Bounds.Width, operatorBox.Bounds.Width);
            Assert.Equal(panel.Bounds.Width, choiceBox.Bounds.Width);

            choiceBox.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();

            var popup = Assert.Single(choiceBox.GetVisualDescendants().OfType<Popup>());
            Assert.Equal(choiceBox.Bounds.Width, popup.MinWidth);
            Assert.True(popup.Bounds.Width >= choiceBox.Bounds.Width);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standard_multi_choice_filter_caps_list_height_and_remains_scrollable()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableViewColumn<DynamicTableViewTestRow> column = DynamicTableViewColumn<DynamicTableViewTestRow>.Create(
            "name", "Name", static row => row.Name);
        column.FilterChoices = Enumerable.Range(0, 40)
            .Select(static index => new DynamicTableViewFilterChoice($"Choice {index}", $"value-{index}"))
            .ToArray();
        using var source = DynamicTableViewTestData.CreateSource(cache, [column]);
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 640, Height = 800, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var operatorBox = Assert.Single(content.GetVisualDescendants().OfType<ComboBox>()
                .Where(static comboBox => comboBox.Name == "PART_OperatorBox"));
            operatorBox.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            var choices = Assert.Single(content.GetVisualDescendants().OfType<ListBox>()
                .Where(static listBox => listBox.Name == "PART_MultipleChoiceBox"));
            var scrollViewer = Assert.Single(choices.GetVisualDescendants().OfType<ScrollViewer>());

            Assert.True(choices.IsVisible);
            Assert.Equal(240, choices.MaxHeight);
            Assert.Equal(240, choices.Bounds.Height);
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scrollViewer.Offset.Y > 0);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standard_text_and_numeric_filter_flyouts_apply_typed_values()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ApplyStandardFilter(table, 0, 0, "ph");
            Assert.Equal("ph", Assert.Single(source.FilterDescriptors).Value);
            Assert.Equal(["a"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
            AssertStandardFilterState(table, 0, 0, "ph");

            source.ClearFilters();
            ApplyStandardFilter(table, 1, 3, "20");
            Assert.Equal(["b", "c"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Empty_text_filter_is_rejected_and_reports_validation()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var textInput = Assert.Single(content.GetVisualDescendants().OfType<TextBox>()
                .Where(static textBox => textBox.Name == "PART_ValueBox"));
            var applyButton = content.GetVisualDescendants().OfType<Button>()
                .Single(static button => button.Name == "PART_ApplyButton");

            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(source.FilterDescriptors);
            Assert.True(DataValidationErrors.GetHasErrors(textInput));
            Assert.Contains(
                DynamicTableViewResources.FilterValidationRequired,
                DataValidationErrors.GetErrors(textInput)!.Select(static error => error.ToString()));

            textInput.Text = "   ";
            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(source.FilterDescriptors);
            Assert.True(DataValidationErrors.GetHasErrors(textInput));

            textInput.Text = "ph";
            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(DataValidationErrors.GetHasErrors(textInput));
            Assert.Equal("ph", Assert.Single(source.FilterDescriptors).Value);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Invalid_numeric_filter_shows_validation_error_and_does_not_apply()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 1);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var numericInput = Assert.Single(content.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(static input => input.Name == "PART_NumericValueBox"));
            Assert.True(numericInput.IsVisible);
            Assert.True(numericInput.ShowButtonSpinner);
            Assert.DoesNotContain(content.GetVisualDescendants().OfType<TextBox>(),
                static input => input.Name == "PART_ValueBox" && input.IsVisible);
            var numericEditor = Assert.Single(numericInput.GetVisualDescendants().OfType<TextBox>());
            numericInput.Text = "not a number";

            content.GetVisualDescendants().OfType<Button>()
                .Single(static button => button.Name == "PART_ApplyButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(source.FilterDescriptors);
            Assert.True(DataValidationErrors.GetHasErrors(numericEditor));
            Assert.Contains(
                DynamicTableViewResources.FilterValidationNumber,
                DataValidationErrors.GetErrors(numericEditor)!.Select(static error => error.ToString()));

            numericInput.Text = "20";
            content.GetVisualDescendants().OfType<Button>()
                .Single(static button => button.Name == "PART_ApplyButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(DataValidationErrors.GetHasErrors(numericEditor));
            Assert.Equal(20m, Assert.Single(source.FilterDescriptors).Value);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Numeric_between_filter_validates_each_numeric_input()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 1);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
            FlyoutBase.ShowAttachedFlyout(filterButton);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var operatorBox = Assert.Single(content.GetVisualDescendants().OfType<ComboBox>()
                .Where(static comboBox => comboBox.Name == "PART_OperatorBox"));
            operatorBox.SelectedIndex = 6;
            Dispatcher.UIThread.RunJobs();

            var firstInput = Assert.Single(content.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(static input => input.Name == "PART_NumericValueBox"));
            var secondInput = Assert.Single(content.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(static input => input.Name == "PART_NumericSecondValueBox"));
            Assert.True(firstInput.IsVisible);
            Assert.True(secondInput.IsVisible);
            Assert.True(firstInput.ShowButtonSpinner);
            Assert.True(secondInput.ShowButtonSpinner);
            firstInput.Text = "15";
            secondInput.Text = "25";

            var applyButton = content.GetVisualDescendants().OfType<Button>()
                .Single(static button => button.Name == "PART_ApplyButton");
            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            DynamicTableViewFilterDescriptor appliedFilter = Assert.Single(source.FilterDescriptors);
            Assert.Equal(DynamicTableViewFilterOperator.Between, appliedFilter.Operator);
            Assert.Equal(15m, appliedFilter.Value);
            Assert.Equal(25m, appliedFilter.SecondValue);
            Assert.Equal(["b"], source.Items.Cast<DynamicTableViewTestRow>().Select(static row => row.Id));

            secondInput.Text = "invalid";
            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            TextBox secondInputEditor = Assert.Single(secondInput.GetVisualDescendants().OfType<TextBox>());
            Assert.True(DataValidationErrors.GetHasErrors(secondInputEditor));
            Assert.Same(appliedFilter, Assert.Single(source.FilterDescriptors));
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Filter_button_background_stays_transparent_while_icon_highlights_on_hover()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var filterIcon = Assert.Single(filterButton.GetVisualDescendants().OfType<PathIcon>());
            var presenter = Assert.Single(filterButton.GetVisualDescendants().OfType<ContentPresenter>()
                .Where(static item => item.Name == "PART_ContentPresenter"));
            SolidColorBrush normalIconBrush = Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            Assert.True(Application.Current!.Styles.TryGetResource("SystemControlHighlightBaseMediumBrush", table.ActualThemeVariant, out object? hoverBrush));
            Assert.True(Application.Current!.Styles.TryGetResource("ToggleButtonBackgroundCheckedPointerOver", table.ActualThemeVariant, out object? accentBrush));
            AssertFilterButtonBackgroundIsTransparent(presenter);

            var point = filterButton.TranslatePoint(
                new Point(filterButton.Bounds.Width / 2, filterButton.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Filter button has no window position.");
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();

            Assert.True(filterButton.IsPointerOver);
            Assert.Same(hoverBrush, filterIcon.Foreground);
            SolidColorBrush hoveredIconBrush = Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            Assert.NotEqual((normalIconBrush.Color, normalIconBrush.Opacity), (hoveredIconBrush.Color, hoveredIconBrush.Opacity));
            Assert.NotEqual((Assert.IsAssignableFrom<SolidColorBrush>(accentBrush).Color, Assert.IsAssignableFrom<SolidColorBrush>(accentBrush).Opacity),
                (hoveredIconBrush.Color, hoveredIconBrush.Opacity));
            AssertFilterButtonBackgroundIsTransparent(presenter);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Keyboard_focused_filter_button_keeps_a_visible_focus_background()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        DynamicTableView table = new() { Source = source };
        SolidColorBrush focusBrush = new(Colors.LightGray);
        table.Resources["SystemControlBackgroundChromeMediumBrush"] = focusBrush;
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var presenter = Assert.Single(filterButton.GetVisualDescendants().OfType<ContentPresenter>()
                .Where(static item => item.Name == "PART_ContentPresenter"));

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(filterButton.Focus());
            window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
            Dispatcher.UIThread.RunJobs();

            Assert.True(filterButton.IsFocused);
            Assert.False(filterButton.IsPointerOver);
            Assert.Same(focusBrush, presenter.Background);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Active_filter_keeps_transparent_button_background_and_highlights_icon()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        Window window = new() { Width = 520, Height = 260, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var header = GetHeaderControl(table, 0);
            var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
                .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
            var filterIcon = Assert.Single(filterButton.GetVisualDescendants().OfType<PathIcon>());
            var presenter = Assert.Single(filterButton.GetVisualDescendants().OfType<ContentPresenter>()
                .Where(static item => item.Name == "PART_ContentPresenter"));
            SolidColorBrush normalIconBrush = Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            Assert.True(Application.Current!.Styles.TryGetResource("ToggleButtonBackgroundChecked", table.ActualThemeVariant, out object? checkedBrush));
            Assert.True(Application.Current!.Styles.TryGetResource("SystemControlHighlightBaseMediumBrush", table.ActualThemeVariant, out object? checkedHoverBrush));
            Assert.True(Application.Current!.Styles.TryGetResource("ButtonForegroundPressed", table.ActualThemeVariant, out object? checkedPressedBrush));

            ApplyStandardFilter(table, 0, 0, "ph");
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("filtered", header.Classes);
            Assert.Same(checkedBrush, filterIcon.Foreground);
            SolidColorBrush checkedIconBrush = Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            Assert.NotEqual((normalIconBrush.Color, normalIconBrush.Opacity), (checkedIconBrush.Color, checkedIconBrush.Opacity));
            AssertFilterButtonBackgroundIsTransparent(presenter);

            var point = filterButton.TranslatePoint(
                new Point(filterButton.Bounds.Width / 2, filterButton.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Filter button has no window position.");
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();

            Assert.True(filterButton.IsPointerOver);
            Assert.Same(checkedHoverBrush, filterIcon.Foreground);
            SolidColorBrush checkedHoveredIconBrush = Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            Assert.NotEqual((checkedIconBrush.Color, checkedIconBrush.Opacity), (checkedHoveredIconBrush.Color, checkedHoveredIconBrush.Opacity));
            AssertFilterButtonBackgroundIsTransparent(presenter);

            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(filterButton.IsPressed);
            Assert.Same(checkedPressedBrush, filterIcon.Foreground);
            Assert.IsAssignableFrom<SolidColorBrush>(filterIcon.Foreground);
            AssertFilterButtonBackgroundIsTransparent(presenter);

            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            source.ClearFilters();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("filtered", header.Classes);
            Assert.NotSame(checkedBrush, filterIcon.Foreground);
            AssertFilterButtonBackgroundIsTransparent(presenter);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Right_click_targets_clicked_row_or_existing_multi_selection()
    {
        using SourceCache<DynamicTableViewTestRow, string> cache = new(static row => row.Id);
        using var source = DynamicTableViewTestData.CreateSource(cache);
        cache.AddOrUpdate(DynamicTableViewTestData.CreateRows());
        DynamicTableView table = new() { Source = source };
        DynamicTableViewContextMenuEventArgs? received = null;
        table.ContextMenuRequested += (_, args) => received = args;
        Window window = new() { Width = 420, Height = 240, Content = table };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            source.SelectionModel.Select(0);
            source.SelectionModel.Select(1);
            RightClickRow(window, table, "b");

            Assert.Equal("b", received?.ClickedItem is DynamicTableViewTestRow clicked ? clicked.Id : null);
            Assert.Equal(["a", "b"], received?.TargetItems.Cast<DynamicTableViewTestRow>().Select(static item => item.Id));

            RightClickRow(window, table, "c");

            Assert.Equal("c", received?.ClickedItem is DynamicTableViewTestRow unselected ? unselected.Id : null);
            Assert.Equal(["c"], received?.TargetItems.Cast<DynamicTableViewTestRow>().Select(static item => item.Id));
        }
        finally
        {
            window.Close();
        }
    }

    private static void RightClickRow(Window window, DynamicTableView table, string id)
    {
        var row = table.GetVisualDescendants().OfType<TableViewRow>()
            .Single(item => item.DataContext is DynamicTableViewTestRow rowData && rowData.Id == id);
        var position = row.TranslatePoint(new Point(5, 5), window) ?? throw new InvalidOperationException("Row has no window position.");
        window.MouseDown(position, MouseButton.Right);
        window.MouseUp(position, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point GetRowCenter(Window window, DynamicTableView table, string id)
    {
        var row = table.GetVisualDescendants().OfType<TableViewRow>()
            .Single(item => item.DataContext is DynamicTableViewTestRow rowData && rowData.Id == id);
        return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Row has no window position.");
    }

    private static void ClickHeader(Window window, Control header, double x)
    {
        var position = header.TranslatePoint(new Point(x, header.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Header has no window position.");
        window.MouseDown(position, MouseButton.Left);
        window.MouseUp(position, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertVisibleHeaders(DynamicTableView table, IEnumerable<string> expectedHeaders)
    {
        var visibleLabels = table.GetVisualDescendants().OfType<TextBlock>()
            .Where(static textBlock => textBlock.IsVisible)
            .Select(static textBlock => textBlock.Text)
            .ToHashSet(StringComparer.Ordinal);
        var headerDetails = table.GetVisualDescendants().OfType<TableViewColumnHeader>()
            .Select(header => $"{header.Column?.Header} visible={header.IsVisible} bounds={header.Bounds} text=[{string.Join(",", header.GetVisualDescendants().OfType<TextBlock>().Select(static textBlock => textBlock.Text))}]");
        foreach (var header in expectedHeaders)
            Assert.True(visibleLabels.Contains(header), $"Missing visible header '{header}'. Visible labels: {string.Join(",", visibleLabels)}. Headers: {string.Join("; ", headerDetails)}");
    }

    private static Control GetHeaderControl(DynamicTableView table, int columnIndex)
        => table.GetVisualDescendants().OfType<TableViewColumnHeader>()
            .Single(header => ReferenceEquals(header.Column, table.Columns[columnIndex]))
            .GetVisualDescendants().OfType<TemplatedControl>().First();

    private static void AssertFilterButtonBackgroundIsTransparent(ContentPresenter presenter)
        => Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);

    private static void ApplyStandardFilter(DynamicTableView table, int columnIndex, int operatorIndex, string firstValue)
    {
        var header = GetHeaderControl(table, columnIndex);
        var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
            .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
        FlyoutBase.ShowAttachedFlyout(filterButton);
        Dispatcher.UIThread.RunJobs();
        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        var comboBoxes = content.GetVisualDescendants().OfType<ComboBox>().ToArray();
        comboBoxes[0].SelectedIndex = operatorIndex;
        NumericUpDown? numericValueBox = content.GetVisualDescendants().OfType<NumericUpDown>()
            .SingleOrDefault(static input => input.Name == "PART_NumericValueBox" && input.IsVisible);
        if (numericValueBox is not null)
        {
            numericValueBox.Text = firstValue;
            Assert.Equal(firstValue, numericValueBox.Text);
        }
        else
        {
            TextBox valueBox = content.GetVisualDescendants().OfType<TextBox>()
                .Single(static textBox => textBox.Name == "PART_ValueBox");
            valueBox.Text = firstValue;
            Assert.Equal(firstValue, valueBox.Text);
        }
        content.GetVisualDescendants().OfType<Button>()
            .Single(static button => button.Name == "PART_ApplyButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        flyout.Hide();
    }

    private static void AssertStandardFilterState(DynamicTableView table, int columnIndex, int operatorIndex, string firstValue)
    {
        var header = GetHeaderControl(table, columnIndex);
        var filterButton = Assert.Single(header.GetVisualDescendants().OfType<Button>()
            .Where(static button => button.Classes.Contains("dynamic-table-view-filter-button")));
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(filterButton));
        FlyoutBase.ShowAttachedFlyout(filterButton);
        Dispatcher.UIThread.RunJobs();

        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        Assert.Equal(operatorIndex, content.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex);
        NumericUpDown? numericValueBox = content.GetVisualDescendants().OfType<NumericUpDown>()
            .SingleOrDefault(static input => input.Name == "PART_NumericValueBox" && input.IsVisible);
        if (numericValueBox is not null)
        {
            Assert.Equal(firstValue, numericValueBox.Text);
        }
        else
        {
            Assert.Equal(firstValue, content.GetVisualDescendants().OfType<TextBox>()
                .Single(static textBox => textBox.Name == "PART_ValueBox").Text);
        }
        flyout.Hide();
    }

    private sealed class ManualScheduler : IScheduler
    {
        private readonly Queue<Action> _actions = new();

        public DateTimeOffset Now => DateTimeOffset.UtcNow;

        public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public IDisposable Schedule<TState>(TState state, TimeSpan dueTime,
            Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime,
            Func<IScheduler, TState, IDisposable> action)
            => Enqueue(() => action(this, state));

        public void RunUntil(Func<bool> condition)
        {
            var steps = 0;
            while (!condition())
            {
                Assert.True(_actions.Count > 0, "Scheduler queue emptied before expected source state arrived.");
                RunNext();
                Assert.True(++steps < 1000, "Scheduler exceeded expected work limit.");
            }
        }

        public void RunUntilIdle()
        {
            var steps = 0;
            while (_actions.Count > 0)
            {
                RunNext();
                Assert.True(++steps < 1000, "Scheduler exceeded expected work limit.");
            }
        }

        private IDisposable Enqueue(Action action)
        {
            _actions.Enqueue(action);
            return Disposable.Empty;
        }

        private void RunNext()
        {
            Action action = _actions.Dequeue();
            action();
        }
    }
}
