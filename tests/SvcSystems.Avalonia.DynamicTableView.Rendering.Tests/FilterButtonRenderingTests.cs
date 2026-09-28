using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;

namespace SvcSystems.Avalonia.DynamicTableView.Rendering.Tests;

public sealed class FilterButtonRenderingTests
{
    [AvaloniaFact]
    public void Hover_paints_filter_button_background_without_painting_header_or_adjacent_area()
    {
        using SourceCache<FilterRow, string> cache = new(static row => row.Id);
        DynamicTableViewColumn<FilterRow> column = DynamicTableViewColumn<FilterRow>.Create(
            "name", "A very long column title that definitely stretches beneath the filter button", static row => row.Name);
        using DynamicTableViewSource<FilterRow, string> source = new(
            cache.Connect(),
            static row => row.Id,
            [column],
            ImmediateScheduler.Instance,
            ImmediateScheduler.Instance,
            TimeSpan.Zero,
            ImmediateScheduler.Instance);
        cache.AddOrUpdate(new FilterRow("one", "One"));

        DynamicTableView table = new() { Source = source };
        SolidColorBrush headerHoverBrush = new(Colors.Orange);
        table.Resources["SystemControlHighlightListLowBrush"] = headerHoverBrush;
        Window window = new() { Width = 520, Height = 260, Content = table };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columnHeader = Assert.Single(table.GetVisualDescendants().OfType<TableViewColumnHeader>());
            var headerStrip = Assert.Single(columnHeader.GetSelfAndVisualAncestors().OfType<Border>(),
                static border => border.GetVisualParent() is DockPanel { Name: "ContentPanel" });
            var hoverSurface = Assert.Single(columnHeader.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => presenter.Name == "PART_ContentPresenter" && presenter.Bounds.Size == columnHeader.Bounds.Size && presenter.BorderThickness == default);
            var filterButton = Assert.Single(columnHeader.GetVisualDescendants().OfType<Button>(),
                static button => button.Classes.Contains("dynamic-table-view-filter-button"));
            var buttonOrigin = filterButton.TranslatePoint(default, window)
                ?? throw new InvalidOperationException("Filter button has no window position.");
            var hoverPoint = filterButton.TranslatePoint(
                new Point(filterButton.Bounds.Width / 2, filterButton.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Filter button has no window position.");

            window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
            Dispatcher.UIThread.RunJobs();
            using var normalFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the normal filter button frame.");
            Point buttonBackgroundPoint = new(buttonOrigin.X + 2, buttonOrigin.Y + 2);
            Point adjacentHeaderPoint = new(buttonOrigin.X - 2, buttonBackgroundPoint.Y);
            string normalButtonPixel = ReadPixel(normalFrame, window, buttonBackgroundPoint);
            string normalAdjacentPixel = ReadPixel(normalFrame, window, adjacentHeaderPoint);

            Point headerTopPoint = columnHeader.TranslatePoint(new Point(columnHeader.Bounds.Width / 2, 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            Point headerBackgroundPoint = columnHeader.TranslatePoint(new Point(2, columnHeader.Bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseMove(headerTopPoint);
            Dispatcher.UIThread.RunJobs();
            Assert.True(columnHeader.IsPointerOver, $"Header did not receive pointer at {headerTopPoint}; header bounds={columnHeader.Bounds}, position={columnHeader.TranslatePoint(default, window)}, strip={headerStrip.Bounds}, window={window.Bounds}.");
            Assert.Equal(45, headerStrip.Bounds.Height);
            Assert.Equal(headerStrip.Bounds.Height, columnHeader.Bounds.Height);
            Assert.Equal(columnHeader.Bounds, hoverSurface.Bounds);
            Assert.Same(headerHoverBrush, hoverSurface.Background);
            using var headerTopFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the hovered header top edge.");
            string normalHeaderTopPixel = ReadPixel(normalFrame, window, headerTopPoint);
            Assert.NotEqual(normalHeaderTopPixel, ReadPixel(headerTopFrame, window, headerTopPoint));
            string headerHoverBackgroundPixel = ReadPixel(headerTopFrame, window, headerBackgroundPoint);

            Point headerBottomPoint = columnHeader.TranslatePoint(
                new Point(columnHeader.Bounds.Width / 2, columnHeader.Bounds.Height - 1), window)
                ?? throw new InvalidOperationException("Header has no window position.");
            window.MouseMove(headerBottomPoint);
            Dispatcher.UIThread.RunJobs();
            Assert.True(columnHeader.IsPointerOver);
            Assert.Same(headerHoverBrush, hoverSurface.Background);
            using var headerBottomFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the hovered header bottom edge.");
            string normalHeaderBottomPixel = ReadPixel(normalFrame, window, headerBottomPoint);
            Assert.NotEqual(normalHeaderBottomPixel, ReadPixel(headerBottomFrame, window, headerBottomPoint));

            window.MouseMove(hoverPoint);
            Dispatcher.UIThread.RunJobs();
            using var hoverFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the hovered filter button frame.");

            var headerText = Assert.Single(columnHeader.GetVisualDescendants().OfType<TextBlock>(),
                static textBlock => textBlock.Text?.StartsWith("A very long column title", StringComparison.Ordinal) == true);
            string longText = headerText.Text ?? string.Empty;
            string filterBackgroundPixel = ReadPixel(hoverFrame, window, buttonBackgroundPoint);
            Rect filterButtonBounds = new(buttonOrigin.X, buttonOrigin.Y, filterButton.Bounds.Width, filterButton.Bounds.Height);
            string longHeaderButtonPixels = ReadRegion(hoverFrame, window, filterButtonBounds);
            headerText.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            using var emptyTextFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the filter button without header text.");

            Assert.True(filterButton.IsPointerOver);
            Assert.True(columnHeader.IsPointerOver);
            Assert.NotSame(headerHoverBrush, hoverSurface.Background);
            Assert.NotEqual(normalButtonPixel, ReadPixel(hoverFrame, window, buttonBackgroundPoint));
            Assert.Equal(headerHoverBackgroundPixel, ReadPixel(hoverFrame, window, buttonBackgroundPoint));
            Assert.Equal(filterBackgroundPixel, ReadPixel(emptyTextFrame, window, buttonBackgroundPoint));
            Assert.Equal(longHeaderButtonPixels, ReadRegion(emptyTextFrame, window, filterButtonBounds));
            Assert.Equal(normalAdjacentPixel, ReadPixel(hoverFrame, window, adjacentHeaderPoint));
            headerText.Text = longText;

            source.SetFilter(new("name", DynamicTableViewFilterOperator.Contains, "One"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(source.FilterDescriptors);
            window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
            Dispatcher.UIThread.RunJobs();
            using var activeFilterNormalFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the active filter button without hover.");
            Assert.True(filterButton.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(filterButton.IsFocused);
            using var activeFilterFocusedFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the focused active filter button.");
            window.MouseMove(hoverPoint);
            Dispatcher.UIThread.RunJobs();
            using var activeFilterHoverFrame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("Could not capture the hovered active filter button.");

            Assert.Equal(headerHoverBackgroundPixel, ReadPixel(activeFilterHoverFrame, window, buttonBackgroundPoint));
            Assert.Equal(filterBackgroundPixel, ReadPixel(activeFilterHoverFrame, window, buttonBackgroundPoint));
            Assert.NotEqual(ReadPixel(activeFilterFocusedFrame, window, buttonBackgroundPoint),
                ReadPixel(activeFilterHoverFrame, window, buttonBackgroundPoint));
            Assert.NotEqual(ReadPixel(activeFilterNormalFrame, window, buttonBackgroundPoint),
                ReadPixel(activeFilterHoverFrame, window, buttonBackgroundPoint));
        }
        finally
        {
            window.Close();
        }
    }

    private static string ReadPixel(WriteableBitmap frame, Window window, Point logicalPoint)
    {
        int x = Math.Clamp((int)(logicalPoint.X * frame.PixelSize.Width / window.Bounds.Width), 0, frame.PixelSize.Width - 1);
        int y = Math.Clamp((int)(logicalPoint.Y * frame.PixelSize.Height / window.Bounds.Height), 0, frame.PixelSize.Height - 1);
        byte[] pixel = new byte[4];
        GCHandle pinnedPixel = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(x, y, 1, 1), pinnedPixel.AddrOfPinnedObject(), pixel.Length, pixel.Length);
        }
        finally
        {
            pinnedPixel.Free();
        }

        return Convert.ToHexString(pixel);
    }

    private static string ReadRegion(WriteableBitmap frame, Window window, Rect logicalBounds)
    {
        int x = Math.Clamp((int)(logicalBounds.X * frame.PixelSize.Width / window.Bounds.Width), 0, frame.PixelSize.Width - 1);
        int y = Math.Clamp((int)(logicalBounds.Y * frame.PixelSize.Height / window.Bounds.Height), 0, frame.PixelSize.Height - 1);
        int width = Math.Clamp((int)(logicalBounds.Width * frame.PixelSize.Width / window.Bounds.Width), 1, frame.PixelSize.Width - x);
        int height = Math.Clamp((int)(logicalBounds.Height * frame.PixelSize.Height / window.Bounds.Height), 1, frame.PixelSize.Height - y);
        byte[] pixels = new byte[width * height * 4];
        GCHandle pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(x, y, width, height), pinnedPixels.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pinnedPixels.Free();
        }

        return Convert.ToHexString(pixels);
    }

    private sealed record FilterRow(string Id, string Name);
}
