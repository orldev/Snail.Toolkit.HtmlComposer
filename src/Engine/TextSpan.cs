namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A run of text in one font and colour, on one line of one page.</summary>
/// <param name="Text">The characters to draw.</param>
/// <param name="X">Left edge of the run.</param>
/// <param name="Baseline">Vertical position of the baseline.</param>
/// <param name="Width">The advance width the engine measured, which a renderer uses to draw decorations.</param>
/// <param name="Font">The font the run was measured with and has to be drawn with.</param>
/// <param name="Color">The text colour.</param>
/// <param name="Decoration">Lines drawn together with the text.</param>
public sealed record TextSpan(
    string Text,
    float X,
    float Baseline,
    float Width,
    Font Font,
    Rgba Color,
    TextDecoration Decoration = TextDecoration.None)
{
    /// <summary>Which number of a running line this run is, if it is one.</summary>
    /// <remarks>
    /// <c>{page}</c> and <c>{pages}</c> are written into a running line as numbers before it is set, so a fixed
    /// page needs nothing more. A deck has a slide number of its own that follows the slides as they are moved
    /// and added, and draws it in place of the number the engine wrote.
    /// </remarks>
    public TextField Field { get; init; }
}

/// <summary>What a run of a running line stands for, beyond its characters.</summary>
public enum TextField
{
    /// <summary>Ordinary text.</summary>
    None,

    /// <summary>The number of the page, written for <c>{page}</c>.</summary>
    PageNumber,

    /// <summary>The number of pages in the document, written for <c>{pages}</c>.</summary>
    PageCount,
}
