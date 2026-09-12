using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Turns HTML into a PowerPoint deck: lays the markup out into pages and writes them as slides.</summary>
/// <remarks>
/// A deck is an editable document rather than a proof. Every run is placed where the engine set it, but a
/// reader without the faces the document was measured in re-draws the text in whatever it has, and then the
/// positions hold while the glyphs no longer fill them exactly.
/// </remarks>
public sealed class HtmlToPptx(FontLibrary fonts, PptxOptions? options = null)
    : Converter<PptxOptions>(fonts, options ?? new PptxOptions())
{
    /// <inheritdoc/>
    protected override void Write(Composition composition, Stream output, Faces faces, CancellationToken ct) =>
        Deck.Write(composition, output, faces, Library, Options, ct);

    /// <summary>A note for every family asked to travel inside the deck that cannot.</summary>
    /// <remarks>
    /// A deck carries TrueType and nothing else. A CFF-flavoured OpenType face is left behind silently by the
    /// format, and the deck then names a face the reader may not have, so it is named here instead: the
    /// document is written either way, and the note says what will be substituted and how to prevent it.
    /// </remarks>
    protected override IEnumerable<Note> NotesFor(Composition composition)
    {
        if (!Options.EmbedFonts)
        {
            return [];
        }

        var used = composition.Pages.SelectMany(page => page.Spans).Select(span => span.Font).Distinct().ToList();

        return Library
            .Unembeddable(used)
            .Select(family => new Note(
                NoteKind.CffOutlines,
                family,
                $"\"{family}\" has CFF outlines, which a deck cannot carry, so it is named but not embedded; "
                + "load a TrueType (.ttf) version of it to have it travel with the file."));
    }
}
