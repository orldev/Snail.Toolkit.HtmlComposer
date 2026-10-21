using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>What a deck accepts beyond what every document does.</summary>
public sealed record PptxOptions : DocumentOptions
{
    /// <summary>Whether the faces the document is set in travel inside the deck. On unless turned off.</summary>
    /// <remarks>
    /// A deck names its faces, and a reader that has none of them substitutes whatever it has, which moves
    /// every line. Carrying the faces inside the file settles that, the way a PDF does. Only TrueType can
    /// travel: a CFF-flavoured OpenType face is named in the notes instead of being embedded. Turn this off
    /// when a licence forbids passing a face on inside a document, or when every reader has the fonts anyway
    /// and the file should stay small.
    /// </remarks>
    public bool EmbedFonts { get; init; } = true;

    /// <summary>The BCP 47 tag the text is marked with, such as <c>ru-RU</c>; none unless given.</summary>
    /// <remarks>
    /// A run's language is what a reader spell-checks and hyphenates it by. The deck used to mark every run
    /// as English, Cyrillic included, so a Russian document was proofed as English. Without a tag the reader
    /// falls back to its own editing language, which is right more often than a guess would be.
    /// </remarks>
    public string? Language { get; init; }

    /// <summary>Families the deck names after another face, such as Arimo set as Arial; none unless given.</summary>
    /// <remarks>
    /// A document can be measured in one face and handed over naming another with the same metrics: Arimo is
    /// drawn to Arial's widths, so a server without Arial lays a page out in Arimo and the deck asks the reader
    /// for the Arial every reader has, and the lines fall where they fell. A renamed family is not embedded —
    /// the text no longer asks for it. The keys are family names as the faces report them.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Typefaces { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
