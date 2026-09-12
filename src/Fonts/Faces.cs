using System.Text;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>The faces of one conversion, used both to measure text and to draw it.</summary>
/// <remarks>
/// Measuring and drawing go through the very same <see cref="SKFont"/> objects, so a line is drawn exactly as
/// wide as it was measured. Hinting is off and advances are unrounded because a page has no pixels: hinted
/// advances are rounded to whole device pixels at one size, and summed over a line they drift the text away
/// from where it was broken. A slanted upright face stands in for a missing italic, since a skewed TrueType
/// face still embeds as a real font. One instance serves one document and is disposed with it.
/// </remarks>
public sealed class Faces(FontLibrary library) : ITextMeasurer, IDisposable
{
    private const float Slant = -0.25f;

    private readonly Dictionary<Font, SKFont> _fonts = [];

    private bool _isDisposed;

    /// <summary>The font object a run is measured and drawn with, made once per face and size.</summary>
    /// <remarks>
    /// Every measurement and every drawing goes through here, so the check that these faces are still alive
    /// belongs here too: a font object outliving the library it was made from wraps a typeface that has been
    /// released.
    /// </remarks>
    public SKFont For(Font font)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_fonts.TryGetValue(font, out SKFont? cached))
        {
            return cached;
        }

        FontLibrary.Match match = library.Resolve(font);
        var created = new SKFont(match.Typeface, font.Size)
        {
            Subpixel = true,
            LinearMetrics = true,
            Hinting = SKFontHinting.None,
            SkewX = match.IsSlanted ? Slant : 0,
        };

        _fonts[font] = created;

        return created;
    }

    /// <inheritdoc/>
    public float Measure(string text, Font font)
    {
        SKFont face = For(font);

        return font.Tracking == 0 ? face.MeasureText(text) : face.MeasureText(text) + (font.Tracking * face.CountGlyphs(text));
    }

    /// <summary>The characters of the text the face has no glyph for.</summary>
    /// <remarks>
    /// Asked once the document is set, so the answer covers the text as it will be drawn. A face without the
    /// glyph draws the missing-character box, and nothing in the file says why, which is why it becomes a note.
    /// </remarks>
    public IEnumerable<Rune> Missing(string text, Font font)
    {
        SKFont face = For(font);

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune) && !face.ContainsGlyph(rune.Value))
            {
                yield return rune;
            }
        }
    }

    /// <inheritdoc/>
    public FontMetrics MetricsOf(Font font)
    {
        SKFontMetrics metrics = For(font).Metrics;

        return new FontMetrics(-metrics.Ascent, metrics.Descent, metrics.Leading);
    }

    /// <summary>Releases the font objects made for this document.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (SKFont font in _fonts.Values)
        {
            font.Dispose();
        }

        _fonts.Clear();
    }
}
