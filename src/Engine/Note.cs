namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Which of the things that could not be set as written a note reports.</summary>
public enum NoteKind
{
    /// <summary>A picture could not be read, and its <c>alt</c> text stands in its place.</summary>
    MissingPicture,

    /// <summary>No loaded family matched what the text asked for, and the first family loaded set it.</summary>
    FamilySubstituted,

    /// <summary>Bold was asked of a family with no bold face, and the regular face set it.</summary>
    BoldSubstituted,

    /// <summary>A face has CFF outlines, which the format embeds poorly or cannot carry at all.</summary>
    CffOutlines,

    /// <summary>A face has no glyph for a character, which prints as a crossed box.</summary>
    MissingGlyph,

    /// <summary>No fonts were loaded, so the machine's default face set the text.</summary>
    NoFonts,

    /// <summary>A cell asked to cover more columns or rows than a table may hold, and its span was capped.</summary>
    SpanCapped,

    /// <summary>Markup nested deeper, or a drawing larger, than the limits allow: the layout set the rest as plain text, a drawing left it undrawn.</summary>
    NestingCapped,

    /// <summary>The output format cannot hold something as it was set, and it was fitted to what the format allows.</summary>
    FormatLimit,
}

/// <summary>One thing that could not be set as written, and what was done instead.</summary>
/// <param name="Kind">What the note is about.</param>
/// <param name="Subject">
/// The picture source, the family or the family list the note is about; <see langword="null"/> when it is
/// about the document as a whole.
/// </param>
/// <param name="Text">The note as a sentence, for a person to read.</param>
/// <remarks>
/// A note used to be its sentence alone, and an application that treated a missing picture differently from
/// a missing glyph had to read the sentence for it. The kind and the subject are what an application decides
/// by; the text is what a person reads, and <see cref="ToString"/> answers it so that a note logs as it
/// always did.
/// </remarks>
public sealed record Note(NoteKind Kind, string? Subject, string Text)
{
    /// <summary>The note as a sentence.</summary>
    public override string ToString() => Text;
}
