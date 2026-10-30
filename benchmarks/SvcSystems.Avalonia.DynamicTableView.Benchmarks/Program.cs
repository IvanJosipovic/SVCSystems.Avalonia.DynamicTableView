using BenchmarkDotNet.Running;
using SvcSystems.Avalonia.DynamicTableView.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkDotNetConfiguration.Create());
