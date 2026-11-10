using Avalonia;
using Avalonia.Themes.Fluent;
using SvcSystems.Avalonia.DynamicTableView;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks.UI;

public sealed class DynamicTableViewBenchmarkApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DynamicTableViewTheme());
        base.OnFrameworkInitializationCompleted();
    }
}
