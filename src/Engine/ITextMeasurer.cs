namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Measures text the way the renderer is going to draw it.</summary>
/// <remarks>
/// The engine breaks lines but never measures glyphs itself: a width taken from one face and drawn with
/// another puts every line break in the wrong place. The renderer that owns the fonts implements this, so the
/// engine keeps no knowledge of any output format and each new renderer brings its own measurements.
/// </remarks>
public interface ITextMeasurer
{
    /// <summary>The advance width of the text set in the font, in points.</summary>
    /// <remarks>
    /// A font carrying <see cref="Font.Tracking"/> must be measured with it, once per glyph and after the last
    /// one as well, exactly as the renderer will space the glyphs when it draws them.
    /// </remarks>
    float Measure(string text, Font font);

    /// <summary>The vertical metrics of the face the renderer draws the font with.</summary>
    FontMetrics MetricsOf(Font font);
}
