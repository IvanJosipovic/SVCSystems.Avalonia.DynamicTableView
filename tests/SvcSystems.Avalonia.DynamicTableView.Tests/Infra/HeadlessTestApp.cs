using Avalonia;
using Avalonia.Themes.Fluent;

namespace SvcSystems.Avalonia.DynamicTableView.Tests.Infra;

public sealed class HeadlessTestApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DynamicTableViewTheme());
        base.OnFrameworkInitializationCompleted();
    }
}
