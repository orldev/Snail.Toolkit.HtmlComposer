using System.Text;
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
        List<(string Family, string Character)> missing = Missing(composition, measurer);
        var families = missing.Distinct().GroupBy(character => character.Family);

        foreach (IGrouping<string, (string Family, string Character)> family in families)
        {
            yield return new Note(
                NoteKind.MissingGlyph,
                family.Key,
                $"Family \"{family.Key}\" has no glyph for {string.Join(' ', family.Select(character => character.Character).Order(StringComparer.Ordinal))}, "
                + "which prints as a crossed box.");
        }
    }

    /// <summary>The characters the faces of a composition have no glyph for, each with the families its span asked for.</summary>
    /// <remarks>
    /// Each face is asked about each character once, and its families are named once: asking for every character
    /// of every span, with the families joined again each time, was a fifth of the time a long document took. A
    /// character is listed where the document first sets it in that face, which is where the query over every
    /// character listed it first, so the notes come out the same and in the same order.
    /// </remarks>
    private static List<(string Family, string Character)> Missing(Composition composition, Faces measurer)
    {
        Dictionary<Font, (string Family, HashSet<int> Asked)> faces = [];
        List<(string Family, string Character)> missing = [];

        foreach (TextSpan span in composition.Pages.SelectMany(page => page.Spans))
        {
            if (!faces.TryGetValue(span.Font, out (string Family, HashSet<int> Asked) face))
            {
                face = (string.Join(", ", span.Font.Families), []);
                faces[span.Font] = face;
            }

            foreach (Rune rune in span.Text.EnumerateRunes())
            {
                if (face.Asked.Add(rune.Value) && measurer.Lacks(rune, span.Font))
                {
                    missing.Add((face.Family, rune.ToString()));
                }
            }
        }

        return missing;
    }
}
