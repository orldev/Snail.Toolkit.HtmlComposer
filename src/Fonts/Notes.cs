using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>What a renderer has to say about the faces a document was set in.</summary>
/// <remarks>
/// Shared by every renderer, because the answers are the same whatever is being written: a family that is not
/// loaded is substituted the same way for a deck as for a PDF, and a character the face has no glyph for is
/// lost the same way in both. Kept here rather than copied into each renderer, where the two would drift.
/// </remarks>
public static class Notes
{
    /// <summary>What to say about the faces a composition was set in: families substituted, characters no face could set.</summary>
    public static IEnumerable<Note> For(Composition composition, FontLibrary library, Faces measurer)
    {
        var used = composition.Pages.SelectMany(page => page.Spans).Select(span => span.Font).Distinct().ToList();

        return [.. library.NotesFor(used), .. Unset(composition, measurer)];
    }

    /// <summary>A note for every family asked to set a character it has no glyph for.</summary>
    /// <remarks>
    /// A missing glyph prints as a crossed box and the file says nothing about why, so the character that
    /// could not be set is named here instead. Grouped by family: one arrow missing from one face is one note,
    /// however many times the document sets it.
    /// </remarks>
    private static IEnumerable<Note> Unset(Composition composition, Faces measurer)
    {
        var spans = composition.Pages.SelectMany(page => page.Spans);
        var missing = spans.SelectMany(span => Missing(span, measurer)).Distinct();
        var families = missing.GroupBy(character => character.Family);

        return families.Select(family => new Note(
            NoteKind.MissingGlyph,
            family.Key,
            $"Family \"{family.Key}\" has no glyph for {string.Join(' ', family.Select(character => character.Character).Order(StringComparer.Ordinal))}, "
            + "which prints as a crossed box."));
    }

    /// <summary>
    /// The characters of one span its face has no glyph for, each with the families the span asked for — named only
    /// for a character that is missing, as almost every span has none.
    /// </summary>
    private static IEnumerable<(string Family, string Character)> Missing(TextSpan span, Faces measurer) =>
        measurer.Missing(span.Text, span.Font).Select(rune => (Family: string.Join(", ", span.Font.Families), Character: rune.ToString()));
}
