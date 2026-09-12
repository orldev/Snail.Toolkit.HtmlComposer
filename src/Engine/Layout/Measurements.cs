namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Asks the renderer for each width and each set of metrics once per composition.</summary>
/// <remarks>
/// A document repeats the same words and the same few fonts constantly, while a renderer's measurement goes
/// through shaping and font lookup every time. Keeping the answers for the length of one composition removes
/// nearly all of those calls without holding memory between documents.
/// </remarks>
internal sealed class Measurements(ITextMeasurer measurer)
{
    private readonly Dictionary<(string Text, Font Font), float> _widths = [];

    private readonly Dictionary<Font, FontMetrics> _metrics = [];

    public float Measure(string text, Font font)
    {
        if (_widths.TryGetValue((text, font), out float width))
        {
            return width;
        }

        width = measurer.Measure(text, font);
        _widths[(text, font)] = width;

        return width;
    }

    public FontMetrics MetricsOf(Font font)
    {
        if (_metrics.TryGetValue(font, out FontMetrics metrics))
        {
            return metrics;
        }

        metrics = measurer.MetricsOf(font);
        _metrics[font] = metrics;

        return metrics;
    }
}
