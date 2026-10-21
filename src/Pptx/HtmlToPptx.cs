using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Turns HTML into a PowerPoint deck: lays the markup out into pages and writes them as slides.</summary>
/// <remarks>
/// A deck is an editable document rather than a proof. Tables become PowerPoint tables and blocks of text
/// become text boxes that wrap their paragraphs, so the deck is edited like any other; a reader without the
/// faces the document was measured in re-draws the text in whatever it has, and the lines then wrap by its widths.
/// </remarks>
public sealed class HtmlToPptx(FontLibrary fonts, PptxOptions? options = null)
    : Converter<PptxOptions>(fonts, options ?? new PptxOptions())
{
    /// <inheritdoc/>
    protected override void Write(Composition composition, Stream output, Faces faces, CancellationToken ct) =>
        Deck.Write(composition, output, faces, Library, Options, ct);

    /// <inheritdoc/>
    /// <remarks>A deck writes tables as tables and blocks of text as text boxes, which is what the structure is for.</remarks>
    protected override bool IsStructured => true;

    /// <summary>A note for every family asked to travel inside the deck that cannot.</summary>
    /// <remarks>
    /// A deck carries TrueType and nothing else. A CFF-flavoured OpenType face is left behind silently by the
    /// format, and the deck then names a face the reader may not have, so it is named here instead: the
    /// document is written either way, and the note says what will be substituted and how to prevent it.
    /// </remarks>
    protected override IEnumerable<Note> NotesFor(Composition composition) => [.. Sheets(composition), .. Unembedded(composition)];

    /// <summary>A note when the pages cannot all become slides of their own size.</summary>
    /// <remarks>
    /// A deck has one slide size, between one inch and fifty-six a side. A section on another sheet — a landscape
    /// table after portrait pages — is set on the first page's slide and cut off, and a sheet outside the range is
    /// fitted to it; the deck is written either way, and the note says what changed.
    /// </remarks>
    private static IEnumerable<Note> Sheets(Composition composition)
    {
        if (composition.Pages is not [var first, ..])
        {
            yield break;
        }

        bool isMixed = composition.Pages.Any(page => page.Width != first.Width || page.Height != first.Height);
        bool isFitted = Deck.SlideSide(first.Width) != (int)Math.Round(first.Width * 12700) || Deck.SlideSide(first.Height) != (int)Math.Round(first.Height * 12700);

        if (isMixed || isFitted)
        {
            yield return new Note(
                NoteKind.FormatLimit,
                null,
                $"A deck has one slide size, from 72 to 4 032 points a side; every slide is {first.Width:0.#} × {first.Height:0.#} points as the first page is, fitted to that range.");
        }
    }

    private IEnumerable<Note> Unembedded(Composition composition)
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
