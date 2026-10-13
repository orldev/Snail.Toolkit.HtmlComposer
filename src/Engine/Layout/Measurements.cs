namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Asks the renderer for each width and each set of metrics once per composition.</summary>
/// <remarks>
/// A document repeats the same words and the same few fonts constantly, while a renderer's measurement goes
/// through shaping and font lookup every time. Keeping the answers for the length of one composition removes
/// nearly all of those calls without holding memory between documents.
/// <para>
/// The widths are kept by font and then by text, so a word read from a buffer is looked up as the letters it
/// holds: a word met before comes back as the string it was measured as, and only a new word becomes a string
/// of its own. A string for every word of a long document was a tenth of what it allocated.
/// </para>
/// </remarks>
internal sealed class Measurements(ITextMeasurer measurer)
{
    private readonly Dictionary<Font, Dictionary<string, float>> _widths = [];

    private readonly Dictionary<Font, FontMetrics> _metrics = [];

    private Font? _lastFont;

    private Dictionary<string, float> _lastWidths = [];

    public float Measure(string text, Font font)
    {
        Dictionary<string, float> widths = WidthsIn(font);

        if (widths.TryGetValue(text, out float width))
        {
            return width;
        }

        width = measurer.Measure(text, font);
        widths[text] = width;

        return width;
    }

    /// <summary>The word the letters spell, as the string it was measured as before or as a new one, with its width.</summary>
    public string Word(ReadOnlySpan<char> letters, Font font, out float width)
    {
        Dictionary<string, float> widths = WidthsIn(font);
        Dictionary<string, float>.AlternateLookup<ReadOnlySpan<char>> known = widths.GetAlternateLookup<ReadOnlySpan<char>>();

        if (known.TryGetValue(letters, out string? word, out width))
        {
            return word;
        }

        word = letters.ToString();
        width = measurer.Measure(word, font);
        widths[word] = width;

        return word;
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

    /// <summary>The widths taken in one font, the last font asked about being kept at hand.</summary>
    private Dictionary<string, float> WidthsIn(Font font)
    {
        if (ReferenceEquals(font, _lastFont))
        {
            return _lastWidths;
        }

        if (!_widths.TryGetValue(font, out Dictionary<string, float>? widths))
        {
            widths = new Dictionary<string, float>(StringComparer.Ordinal);
            _widths[font] = widths;
        }

        _lastFont = font;
        _lastWidths = widths;

        return widths;
    }
}
