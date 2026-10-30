namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>
/// Attached properties that let a column's minimum width be set from a style.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DynamicTableViewColumn.MinWidth"/> is plain metadata, so it cannot be set from a
/// <c>Style</c>. The attached property here is a real Avalonia property on the native
/// <see cref="TableViewColumn"/>, which means a theme or application style can set it:
/// </para>
/// <code>
/// &lt;Style Selector="TableViewColumn.narrow"&gt;
///   &lt;Setter Property="dynamicTable:DynamicTableViewColumnProperties.MinWidth" Value="120" /&gt;
/// &lt;/Style&gt;
/// </code>
/// <para>
/// Styles are applied to a column when it is attached to a <see cref="TableView"/>, so the value
/// must be in place before the column joins the table. When set, it takes precedence over
/// <see cref="DynamicTableViewColumn.MinWidth"/>; when unset, the metadata value is used.
/// </para>
/// </remarks>
public static class DynamicTableViewColumnProperties
{
    /// <summary>Defines the styleable minimum width for a column. Defaults to NaN, meaning unset.</summary>
    /// <remarks>
    /// Registered by hand rather than with <c>GenerateAttachedProperty</c>: the generator emits
    /// <c>AvaloniaProperty.RegisterAttached&lt;Owner, Host, T&gt;</c>, which requires a non-static
    /// owner type, so it cannot host an attached property on a static class.
    /// </remarks>
    public static readonly AttachedProperty<double> MinWidthProperty =
        AvaloniaProperty.RegisterAttached<TableViewColumn, double>(
            "MinWidth",
            typeof(DynamicTableViewColumnProperties),
            defaultValue: double.NaN,
            inherits: false);

    /// <summary>Gets the styled minimum width for <paramref name="column"/>, or NaN when unset.</summary>
    public static double GetMinWidth(TableViewColumn column)
        => column.GetValue(MinWidthProperty);

    /// <summary>Sets the styled minimum width for <paramref name="column"/>.</summary>
    public static void SetMinWidth(TableViewColumn column, double value)
        => column.SetValue(MinWidthProperty, value);
}
