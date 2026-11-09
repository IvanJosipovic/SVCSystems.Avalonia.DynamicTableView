using Avalonia.VisualTree;

namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Common visual-tree queries used by DynamicTableView and its header.</summary>
internal static class ControlTreeHelpers
{
    /// <summary>Finds the first visual ancestor (including the source itself) of <typeparamref name="T"/>.</summary>
    public static T? FindVisualAncestor<T>(object? source) where T : Visual
    {
        if (source is not Visual visual)
            return null;

        foreach (Visual ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is T match)
                return match;
        }

        return null;
    }

    /// <summary>Reports whether the source is inside a control with the specified name.</summary>
    public static bool IsWithinNamedControl(object? source, string name)
        => source is Visual visual && visual.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Any(control => control.Name == name);

    /// <summary>Reports whether the source is inside a control with the specified class.</summary>
    public static bool IsWithinClass(object? source, string className)
        => source is Visual visual && visual.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Any(control => control.Classes.Contains(className));
}
