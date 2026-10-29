namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Measures text with the table's font so auto-sized columns can be given a pixel width.</summary>
/// <remarks>
/// Owns the off-tree <see cref="TextBlock"/> used for measurement, so the control does not have to
/// keep a measuring control as a field and a single instance can be reused across passes.
/// </remarks>
internal sealed class TextMeasurer
{
    private readonly TextBlock _textBlock = new();

    /// <summary>Measures <paramref name="text"/> with an unbounded width.</summary>
    /// <returns>The desired width in device-independent pixels.</returns>
    public double Measure(string? text)
    {
        _textBlock.Text = text ?? string.Empty;
        _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _textBlock.DesiredSize.Width;
    }
}
