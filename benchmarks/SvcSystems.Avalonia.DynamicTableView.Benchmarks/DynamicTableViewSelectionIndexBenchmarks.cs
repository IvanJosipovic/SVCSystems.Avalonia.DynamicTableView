using System.ComponentModel;
using System.Reactive.Concurrency;
using BenchmarkDotNet.Attributes;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("SelectionIndex")]
public class DynamicTableViewSelectionIndexBenchmarks : IDisposable
{
    private SourceCache<DynamicTableViewBenchmarkRow, int> _cache = null!;
    private DynamicTableViewSource<DynamicTableViewBenchmarkRow, int> _source = null!;
    private QueuedScheduler _uiScheduler = null!;
    private int _selectedRowId;

    [Params(100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [Params(0, 1, 2)]
    public int SelectedPosition { get; set; }

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

        _selectedRowId = SelectedPosition switch
        {
            0 => 0,
            1 => RowCount / 2,
            2 => RowCount - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(SelectedPosition))
        };
        _source.SelectionModel.Select(_selectedRowId);
    }

    [Benchmark]
    public int ReplaceSelectedInPlace()
    {
        DynamicTableViewBenchmarkRow row = _cache.Lookup(_selectedRowId).Value;
        _cache.AddOrUpdate(row with { Enabled = !row.Enabled });
        _uiScheduler.RunUntilIdle();
        return _source.SelectionModel.SelectedItems.Count;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _source?.Dispose();
        _cache?.Dispose();
    }
}
