using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks.UI;

public sealed class DynamicTableViewBenchmarkApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://SvcSystems.Avalonia.DynamicTableView"))
        {
            Source = new Uri("avares://SvcSystems.Avalonia.DynamicTableView/Themes/DynamicTableViewTheme.axaml")
        });
        base.OnFrameworkInitializationCompleted();
    }
}
