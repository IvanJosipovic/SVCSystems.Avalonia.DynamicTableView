using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

/// <summary>
/// Configures the runtime benchmark jobs.
/// </summary>
/// <remarks>
/// The repository targets .NET 11. The latest stable BenchmarkDotNet release (0.15.8) does not
/// recognize .NET 11; support first appears in 0.16.0-preview.1. The project therefore uses
/// 0.16.0-preview.2 and names <c>CoreRuntime.Core11_0</c> explicitly instead of relying on
/// detection of the pinned RC runtime.
/// </remarks>
public static class BenchmarkDotNetConfiguration
{
    /// <summary>Gets the runtime the benchmarks execute on.</summary>
    public static CoreRuntime Runtime => CoreRuntime.Core11_0;

    /// <summary>Creates the configuration used when the benchmark project runs without arguments.</summary>
    public static IConfig Create()
        => DefaultConfig.Instance
            .AddJob(Job.Default.WithRuntime(Runtime));
}
