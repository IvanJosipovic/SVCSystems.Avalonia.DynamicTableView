using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView.Tests.Fixtures;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.UI;

public sealed class DynamicTableViewColumnLayoutTests
{
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
