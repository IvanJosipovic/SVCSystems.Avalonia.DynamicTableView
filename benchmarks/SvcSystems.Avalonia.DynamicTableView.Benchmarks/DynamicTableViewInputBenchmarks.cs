using System.Reactive.Concurrency;
using BenchmarkDotNet.Attributes;
using DynamicData;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

[MemoryDiagnoser]
public class DynamicTableViewInputBenchmarks : IDisposable
{
    private SourceCache<DynamicTableViewBenchmarkRow, int> _cache = null!;
    private DynamicTableViewSource<DynamicTableViewBenchmarkRow, int> _source = null!;
    private int _iteration;

    [Params(100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        DynamicTableViewBenchmarkRow[] initial = new DynamicTableViewBenchmarkRow[RowCount];
        for (int i = 0; i < initial.Length; i++)
            initial[i] = new(i, $"Item {i:D5}", i % 101, true);

        _cache = new(static row => row.Id);
        _source = new(_cache, static row => row.Id,
            workerScheduler: ImmediateScheduler.Instance, uiScheduler: ImmediateScheduler.Instance);
        _cache.AddOrUpdate(initial);
    }

    [Benchmark]
    public int SourceCacheReplace()
    {
        int index = _iteration++ % RowCount;
        DynamicTableViewBenchmarkRow row = _cache.Lookup(index).Value;
        _cache.AddOrUpdate(row with { Score = _iteration });
        return _source.SelectionModel.Count;
    }

    [Benchmark]
    public int SelectAndClear()
    {
        _source.SelectionModel.Select(RowCount / 2);
        int count = _source.SelectionModel.SelectedItems.Count;
        _source.SelectionModel.Clear();
        return count;
    }

    [IterationSetup(Target = nameof(ReplaceSelectedLast))]
    public void SelectLast()
    {
        _source.SelectionModel.Clear();
        _source.SelectionModel.Select(RowCount - 1);
    }

    [Benchmark]
    public int ReplaceSelectedLast()
    {
        DynamicTableViewBenchmarkRow row = _cache.Lookup(RowCount - 1).Value;
        _cache.AddOrUpdate(row with { Score = ++_iteration });
        return _source.SelectionModel.SelectedItems.Count;
    }

    [Benchmark]
    public int SelectMany()
    {
        _source.SelectionModel.Clear();
        for (int index = 0; index < Math.Min(RowCount, 128); index++)
            _source.SelectionModel.Select(index);
        return _source.SelectionModel.SelectedItems.Count;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _source?.Dispose();
        _cache?.Dispose();
    }
}
