using System.ComponentModel;
using System.Reactive.Concurrency;
using BenchmarkDotNet.Attributes;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Selection")]
public class DynamicTableViewSelectionBenchmarks : IDisposable
{
    private SourceCache<DynamicTableViewBenchmarkRow, int> _cache = null!;
    private DynamicTableViewSource<DynamicTableViewBenchmarkRow, int> _source = null!;
    private QueuedScheduler _uiScheduler = null!;
    private int _iteration;
    private int _selectedRowId;

    [Params(100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [Params(1, 64, 10_000)]
    public int RequestedSelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cache = new(static row => row.Id);
        _uiScheduler = new();
        DynamicTableViewColumn<DynamicTableViewBenchmarkRow>[] columns =
        [
            DynamicTableViewColumn<DynamicTableViewBenchmarkRow>.Create(
                "score", "Score", static row => row.Score)
        ];
        _source = new(
            _cache,
            static row => row.Id,
            columns,
            workerScheduler: ImmediateScheduler.Instance,
            uiScheduler: _uiScheduler,
            searchDebounce: TimeSpan.Zero);

        DynamicTableViewBenchmarkRow[] rows = new DynamicTableViewBenchmarkRow[RowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new(index, $"Item {index:D5}", index, true);
        }

        _cache.AddOrUpdate(rows);
        _uiScheduler.RunUntilIdle();
        _source.SetSort([new("score", ListSortDirection.Ascending)]);
        _uiScheduler.RunUntilIdle();

        _selectedRowId = RowCount / 2;
        var selectionCount = Math.Min(RequestedSelectionCount, RowCount - 1);
        var firstSelectedRowId = Math.Clamp(
            _selectedRowId - selectionCount / 2,
            1,
            RowCount - selectionCount);
        if (selectionCount > 0)
        {
            _source.SelectionModel.SelectRange(firstSelectedRowId, firstSelectedRowId + selectionCount - 1);
        }
    }

    [Benchmark(Baseline = true)]
    public int ReplaceUnselectedInPlace()
        => ReplaceEnabledState(0);

    [Benchmark]
    public int ReplaceSelectedInPlace()
        => ReplaceEnabledState(_selectedRowId);

    [Benchmark]
    public int ReplaceSelectedAndMove()
    {
        DynamicTableViewBenchmarkRow row = _cache.Lookup(_selectedRowId).Value;
        var nextScore = (_iteration++ & 1) == 0 ? -1 : RowCount + 1;
        _cache.AddOrUpdate(row with { Score = nextScore });
        _uiScheduler.RunUntilIdle();
        return _source.SelectionModel.SelectedItems.Count;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _source?.Dispose();
        _cache?.Dispose();
    }

    private int ReplaceEnabledState(int rowId)
    {
        DynamicTableViewBenchmarkRow row = _cache.Lookup(rowId).Value;
        _cache.AddOrUpdate(row with { Enabled = !row.Enabled });
        _uiScheduler.RunUntilIdle();
        return _source.SelectionModel.SelectedItems.Count;
    }
}
