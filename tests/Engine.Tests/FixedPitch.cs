namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>Measures every character as half an em wide, so expected positions can be worked out by hand.</summary>
/// <remarks>
/// Ascent is 0.8 em and descent 0.2 em with no line gap, which makes a line of <c>line-height: normal</c>
/// exactly one em tall: 12 points of 12pt text, with the baseline 9.6 points below the top of the line.
/// </remarks>
internal sealed class FixedPitch : ITextMeasurer
{
    public float Measure(string text, Font font) => text.Length * ((font.Size / 2) + font.Tracking);

    public FontMetrics MetricsOf(Font font) => new(font.Size * 0.8f, font.Size * 0.2f, 0);
}
