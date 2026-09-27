using Avalonia.Headless;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(SvcSystems.Avalonia.DynamicTableView.Tests.Infra.HeadlessTestAppBuilder))]
