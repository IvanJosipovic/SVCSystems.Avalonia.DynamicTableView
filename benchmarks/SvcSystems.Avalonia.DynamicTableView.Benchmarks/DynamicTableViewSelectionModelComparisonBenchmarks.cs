using System.ComponentModel;
using System.Reactive.Concurrency;
using Avalonia.Controls.Selection;
using BenchmarkDotNet.Attributes;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("SelectionModel")]
public class DynamicTableViewSelectionModelComparisonBenchmarks : IDisposable
{
    private SourceContext _identityContext = null!;
    private SourceContext _nativeContext = null!;

    [Params(100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [Params(0, 1, 2)]
    public int SelectedPosition { get; set; }

    [Params(1, 64, 9_999)]
    public int RequestedSelectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var selectedRowId = SelectedPosition switch
        {
            0 => 0,
            1 => RowCount / 2,
            2 => RowCount - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(SelectedPosition))
        };

        var selectionCount = Math.Min(RequestedSelectionCount, RowCount);
        var firstSelectedRowId = Math.Clamp(
            selectedRowId - selectionCount / 2,
            0,
            RowCount - selectionCount);

        _identityContext = new(RowCount, selectedRowId, firstSelectedRowId, selectionCount, useNativeSelection: false);
        _nativeContext = new(RowCount, selectedRowId, firstSelectedRowId, selectionCount, useNativeSelection: true);
    }

    [Benchmark(Baseline = true)]
    public int IdentityPreservingSelectionReplace()
        => _identityContext.ReplaceSelectedInPlace();

    [Benchmark]
    public int AvaloniaNativeSelectionReplace()
        => _nativeContext.ReplaceSelectedInPlace();

    [Benchmark]
    public int IdentityPreservingSelectionMove()
        => _identityContext.ReplaceSelectedAndMove();

    // This is a lower-bound cost reference; native SelectionModel does not restore the selected identity after a move.
    [Benchmark]
    public int AvaloniaNativeMoveWithoutIdentityRestoration()
        => _nativeContext.ReplaceSelectedAndMove();

    [GlobalCleanup]
    public void Dispose()
    {
        _identityContext?.Dispose();
        _nativeContext?.Dispose();
    }

    private sealed class SourceContext : IDisposable
    {
        private readonly SourceCache<DynamicTableViewBenchmarkRow, int> _cache;
        private readonly DynamicTableViewSource<DynamicTableViewBenchmarkRow, int> _source;
        private readonly QueuedScheduler _uiScheduler;
        private readonly int _selectedRowId;
        private readonly SelectionModel<object?>? _nativeSelection;
        private int _moveIteration;

        public SourceContext(
            int rowCount,
            int selectedRowId,
            int firstSelectedRowId,
            int selectionCount,
            bool useNativeSelection)
        {
            _selectedRowId = selectedRowId;
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

            DynamicTableViewBenchmarkRow[] rows = new DynamicTableViewBenchmarkRow[rowCount];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = new(index, $"Item {index:D5}", index, true);
            }

            _cache.AddOrUpdate(rows);
            _uiScheduler.RunUntilIdle();
            _source.SetSort([new("score", ListSortDirection.Ascending)]);
            _uiScheduler.RunUntilIdle();

            if (useNativeSelection)
            {
                _nativeSelection = new() { SingleSelect = false, Source = _source.Items };
                _nativeSelection.SelectRange(firstSelectedRowId, firstSelectedRowId + selectionCount - 1);
            }
            else
            {
                _source.SelectionModel.SelectRange(firstSelectedRowId, firstSelectedRowId + selectionCount - 1);
            }
        }

        public int ReplaceSelectedInPlace()
        {
            DynamicTableViewBenchmarkRow row = _cache.Lookup(_selectedRowId).Value;
            _cache.AddOrUpdate(row with { Enabled = !row.Enabled });
            _uiScheduler.RunUntilIdle();
            return _nativeSelection?.SelectedItems.Count ?? _source.SelectionModel.SelectedItems.Count;
        }

        public int ReplaceSelectedAndMove()
        {
            DynamicTableViewBenchmarkRow row = _cache.Lookup(_selectedRowId).Value;
            var nextScore = (_moveIteration++ & 1) == 0 ? -1 : int.MaxValue;
            _cache.AddOrUpdate(row with { Score = nextScore });
            _uiScheduler.RunUntilIdle();
            return _nativeSelection?.SelectedItems.Count ?? _source.SelectionModel.SelectedItems.Count;
        }

        public void Dispose()
        {
            _source.Dispose();
            _cache.Dispose();
        }
    }
}
