using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Themes.Fluent;

namespace SvcSystems.Avalonia.DynamicTableView.Rendering.Tests;

internal sealed class RenderingTestApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DynamicTableViewTheme());
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class RenderingTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<RenderingTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
