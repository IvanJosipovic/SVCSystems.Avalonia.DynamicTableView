# Performance baseline: native column sizing

Captured 2026-11-09 after the `GridLength` sizing migration. These are reference results,
not performance gates; compare future runs only on the same runtime and a similarly idle host.

## Method

- Full benchmark suite, Release configuration, 100 / 1,000 / 10,000 rows.
- Two sequential runs on the same host; each used 4 warmups, 8 measurement iterations,
  200 ms target iteration time, and one launch.
- The UI and source benchmarks use 20 columns. The source operations below measure the
  non-visual source with those same 20 column definitions.
- BenchmarkDotNet 0.16.0-preview.2 on .NET 11.0.100-rc.3.26522.148, Ubuntu 26.04.1,
  Intel Xeon E5-2690 VM (BenchmarkDotNet reports 2 CPUs, 32 logical / 16 physical cores).
  The sandbox denied raising process priority, and reported low available memory, so these
  numbers are environment-specific.
- Run command (change the artifact directory for the second run):

  ```sh
  dotnet run --project benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks.csproj \
    -c Release --no-build -- --filter '*' --warmupCount 4 --iterationCount 8 \
    --iterationTime 200 --launchCount 1 \
    --artifacts benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks/BenchmarkDotNet.Artifacts/run1
  ```

## Results

Means and managed allocations per operation. Two run values are shown to make host variance
visible; they are not before/after comparisons.

### UI sizing (20 columns)

| Operation | Rows | Run 1 mean / allocated | Run 2 mean / allocated |
|---|---:|---:|---:|
| DefaultWidthAfterSourceUpdate | 100 | 10.377 ms / 1,282.66 KB | 10.367 ms / 1,277.56 KB |
| DefaultWidthAfterSourceUpdate | 1,000 | 9.927 ms / 1,278.84 KB | 10.006 ms / 1,277.60 KB |
| DefaultWidthAfterSourceUpdate | 10,000 | 10.843 ms / 1,276.37 KB | 10.536 ms / 1,279.02 KB |
| FixedWidthAfterSourceUpdate | 100 | 9.562 ms / 1,277.58 KB | 10.047 ms / 1,282.77 KB |
| FixedWidthAfterSourceUpdate | 1,000 | 9.816 ms / 1,273.85 KB | 10.851 ms / 1,274.67 KB |
| FixedWidthAfterSourceUpdate | 10,000 | 10.329 ms / 1,278.55 KB | 9.691 ms / 1,278.72 KB |
| ResizeStarColumns | 100 | 2.634 ms / 295.83 KB | 3.157 ms / 306.83 KB |
| ResizeStarColumns | 1,000 | 2.641 ms / 294.40 KB | 2.592 ms / 295.24 KB |
| ResizeStarColumns | 10,000 | 2.591 ms / 294.42 KB | 2.914 ms / 294.38 KB |

### Source operations (20 column definitions)

| Operation | Rows | Run 1 mean / allocated | Run 2 mean / allocated |
|---|---:|---:|---:|
| SourceUpdate | 100 | 7.137 us / 4,504 B | 7.110 us / 4,505 B |
| SourceUpdate | 1,000 | 30.122 us / 26,055 B | 33.542 us / 26,054 B |
| SourceUpdate | 10,000 | 264.158 us / 242,052 B | 248.297 us / 242,048 B |
| CreateTwentyColumns | 100 | 5.311 us / 12,248 B | 4.663 us / 12,248 B |
| CreateTwentyColumns | 1,000 | 4.834 us / 12,248 B | 4.827 us / 12,248 B |
| CreateTwentyColumns | 10,000 | 4.462 us / 12,248 B | 4.710 us / 12,248 B |

Full BenchmarkDotNet markdown, CSV, and HTML reports for all 60 cases per run are under
`BenchmarkDotNet.Artifacts/run1/results` and `BenchmarkDotNet.Artifacts/run2/results`.
Those generated artifacts are ignored by Git.
