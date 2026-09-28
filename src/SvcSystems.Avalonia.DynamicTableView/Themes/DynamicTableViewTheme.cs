using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Includes the DynamicTableView styles in an application.</summary>
public sealed partial class DynamicTableViewTheme : Styles
{
    /// <summary>Loads the compiled DynamicTableView styles.</summary>
    /// <param name="serviceProvider">The XAML parent's service provider.</param>
    public DynamicTableViewTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
