using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

/// <summary>
/// Configures the runtime benchmark jobs.
/// </summary>
/// <remarks>
/// The repository pins .NET 11 RC builds. BenchmarkDotNet 0.15.8 does not recognize them: it
/// resolves the runtime to <c>RuntimeMoniker.NotRecognized</c> and throws from
/// <c>RuntimeMonikerExtensions.GetRuntimeVersion</c> during job validation, so no benchmark
/// could run. 0.16.0-preview.2 recognizes <c>CoreRuntime.Core11_0</c>, so the benchmarks name
/// that runtime explicitly instead of relying on detection of the pinned RC build.
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
