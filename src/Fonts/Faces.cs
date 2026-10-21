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

    /// <summary>The cut faces drawn in place of the library's, each with the data it was made over.</summary>
    private readonly Dictionary<SKTypeface, (SKTypeface Typeface, SKData Data)> _cuts = [];

    /// <summary>Where the characters this instance measures are counted, while a composition is surveyed.</summary>
    private Census? _census;

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
        SKTypeface typeface = _cuts.TryGetValue(match.Typeface, out (SKTypeface Typeface, SKData Data) cut) ? cut.Typeface : match.Typeface;
        var created = new SKFont(typeface, font.Size)
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

        _census?.Add(text, font);

        return font.Tracking == 0 ? face.MeasureText(text) : face.MeasureText(text) + (font.Tracking * face.CountGlyphs(text));
    }

    /// <summary>The same faces cut down to the glyphs a composition draws, for a writer that embeds what it draws with.</summary>
    /// <param name="composition">The pages to be drawn; the runs on them and the labels of their drawings decide the glyphs.</param>
    /// <param name="ct">Looked at between pages and while drawings are surveyed.</param>
    /// <returns>Faces to draw the composition with, which the caller disposes once the document is written.</returns>
    /// <remarks>
    /// A writer that embeds a face whole — Skia's PDF as SkiaSharp builds it — carries every glyph of it: 443 KB
    /// for two letters of PT Sans. Drawn with these faces it carries only the outlines drawn. Every glyph keeps its
    /// number and its advance, so a run is drawn exactly where and as wide as it was measured. Text the pages do
    /// not hold is not in these faces: draw only the composition they were cut for. A face of CFF outlines, one
    /// whose maker forbade subsetting and one that does not read as plain TrueType are drawn whole, as before.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The token was cancelled while the composition was surveyed.</exception>
    public Faces Subset(Composition composition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        Dictionary<SKTypeface, HashSet<int>> used = Survey(composition, ct);
        var subset = new Faces(library);

        foreach ((SKTypeface typeface, HashSet<int> characters) in used)
        {
            if (library.TrueTypeDataOf(typeface) is not { } data || Subsetter.Cut(data.AsSpan(), GlyphsOf(typeface, characters)) is not { } bytes)
            {
                continue;
            }

            var cutData = SKData.CreateCopy(bytes);
            SKTypeface? cut = SKTypeface.FromData(cutData);

            if (cut is null)
            {
                cutData.Dispose();
                continue;
            }

            subset._cuts[typeface] = (cut, cutData);
        }

        return subset;
    }

    /// <summary>The characters a composition draws in each face: its runs, and the labels its drawings set.</summary>
    /// <remarks>
    /// A label is found where it is drawn: every drawing is drawn once onto a canvas that keeps nothing, through
    /// faces that count what they measure, and every piece of a label is measured before it is drawn.
    /// </remarks>
    private Dictionary<SKTypeface, HashSet<int>> Survey(Composition composition, CancellationToken ct)
    {
        var census = new Census(library);
        using var listening = new Faces(library) { _census = census };

        foreach (Page page in composition.Pages)
        {
            ct.ThrowIfCancellationRequested();

            foreach (TextSpan span in page.Spans)
            {
                census.Add(span.Text, span.Font);
            }

            foreach (ImageBox box in page.Images)
            {
                if (box.Image.IsVector)
                {
                    using var canvas = new SKNoDrawCanvas((int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height));
                    var frame = new SKRect(box.Bounds.X, box.Bounds.Y, box.Bounds.Right, box.Bounds.Bottom);

                    Drawings.Draw(canvas, box.Image, frame, listening, box.Source, ct);
                }
            }
        }

        return census.Characters;
    }

    private static ushort[] GlyphsOf(SKTypeface typeface, HashSet<int> characters)
    {
        var text = new StringBuilder(characters.Count * 2);

        foreach (int character in characters)
        {
            if (Rune.IsValid(character))
            {
                text.Append(new Rune(character).ToString());
            }
        }

        using var font = new SKFont(typeface);

        return font.GetGlyphs(text.ToString());
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
            if (Lacks(rune, face))
            {
                yield return rune;
            }
        }
    }

    /// <summary>Whether the face has no glyph for a character that draws one.</summary>
    internal bool Lacks(Rune rune, Font font) => Lacks(rune, For(font));

    private static bool Lacks(Rune rune, SKFont face) =>
        !Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune) && !face.ContainsGlyph(rune.Value);

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

        foreach ((SKTypeface typeface, SKData data) in _cuts.Values)
        {
            typeface.Dispose();
            data.Dispose();
        }

        _fonts.Clear();
        _cuts.Clear();
    }

    /// <summary>The characters measured in each face, counted while a composition is surveyed.</summary>
    /// <remarks>A run's font is resolved to its face once per font, not once per run.</remarks>
    private sealed class Census(FontLibrary library)
    {
        private readonly Dictionary<Font, HashSet<int>> _byFont = [];

        public Dictionary<SKTypeface, HashSet<int>> Characters { get; } = [];

        public void Add(string text, Font font)
        {
            if (!_byFont.TryGetValue(font, out HashSet<int>? characters))
            {
                SKTypeface typeface = library.Resolve(font).Typeface;

                if (!Characters.TryGetValue(typeface, out characters))
                {
                    characters = [];
                    Characters[typeface] = characters;
                }

                _byFont[font] = characters;
            }

            foreach (Rune rune in text.EnumerateRunes())
            {
                characters.Add(rune.Value);
            }
        }
    }
}
