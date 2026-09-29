using Avalonia.Controls;
using DynamicData;

namespace SvcSystems.Avalonia.DynamicTableView.Sample;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly DynamicTableViewSource<SampleRow, string> _xamlSource;
    private readonly DynamicTableViewSource<SampleRow, string> _codeSource;
    private readonly SourceCache<SampleRow, string> _rows;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        var xamlColumns = (SampleColumnDefinitions)Resources["XamlColumnDefinitions"]!;
        _rows = new SourceCache<SampleRow, string>(static row => row.Id);
        _rows.AddOrUpdate(SampleRows.Create());
        _xamlSource = new DynamicTableViewSource<SampleRow, string>(
            _rows, static row => row.Id, xamlColumns.CreateColumns());
        _codeSource = new DynamicTableViewSource<SampleRow, string>(
            _rows, static row => row.Id, CreateCodeColumns());

        XamlTable.Source = _xamlSource;
        CodeFirstHost.Content = new DynamicTableView { Source = _codeSource };
        Closing += (_, _) => Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _xamlSource.Dispose();
        _codeSource.Dispose();
        _rows.Dispose();
    }

    private static DynamicTableViewColumn<SampleRow>[] CreateCodeColumns()
    {
        var name = DynamicTableViewColumn<SampleRow>.Create("name", "Name", static row => row.Name);
        name.WidthMode = DynamicTableViewWidthMode.Star;
        return
        [
            name,
            DynamicTableViewColumn<SampleRow>.Create("restarts", "Restarts", static row => row.Restarts),
            DynamicTableViewColumn<SampleRow>.Create("ready", "Ready", static row => row.Ready),
            DynamicTableViewColumn<SampleRow>.Create("cpuCores", "CPU cores", static row => row.CpuCores),
            DynamicTableViewColumn<SampleRow>.Create("memory", "Memory", static row => row.Memory),
            DynamicTableViewColumn<SampleRow>.Create("createdAt", "Created", static row => row.CreatedAt)
        ];
    }
}
