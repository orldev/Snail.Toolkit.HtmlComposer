namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>The sheet a document is set on: its size and the margins framing the content, in points.</summary>
/// <param name="Width">Sheet width.</param>
/// <param name="Height">Sheet height.</param>
/// <param name="Margins">Space left blank on each side of the content.</param>
public sealed record PageSetup(float Width, float Height, Edges Margins)
{
    /// <summary>ISO A4 portrait, 210 × 297 mm, with 20 mm margins.</summary>
    public static PageSetup A4 { get; } = FromMillimeters(210, 297, 20);

    /// <summary>ISO A5 portrait, 148 × 210 mm, with 15 mm margins.</summary>
    public static PageSetup A5 { get; } = FromMillimeters(148, 210, 15);

    /// <summary>US Letter portrait, 8.5 × 11 in, with 1 in margins.</summary>
    public static PageSetup Letter { get; } =
        new(Points.FromInches(8.5f), Points.FromInches(11), Edges.All(Points.FromInches(1)));

    /// <summary>The colour of the whole sheet, painted edge to edge under everything else.</summary>
    /// <remarks>
    /// A background declared in the markup stops at the margins, so a cover that bleeds to the paper edge is a
    /// property of the sheet rather than of a box set on it.
    /// </remarks>
    public Rgba? Background { get; init; }

    /// <summary>Markup set in the top margin of every page.</summary>
    /// <remarks>
    /// The band is as wide as the content and as tall as the margin, and <c>{page}</c> and <c>{pages}</c> in it
    /// are replaced by the page number and the number of pages in the document. Nothing is clipped: furniture
    /// taller than its margin runs into the content.
    /// </remarks>
    public string? Header { get; init; }

    /// <summary>Markup set in the bottom margin of every page, under the same rules as <see cref="Header"/>.</summary>
    public string? Footer { get; init; }

    /// <summary>Markup set in a side margin of every page, beside the content.</summary>
    /// <remarks>
    /// The band is as wide as the margin it stands in and as tall as the content, so a note beside the text is
    /// a property of the sheet rather than a column of the layout: the content flows from page to page on its
    /// own while the note is set again on each one. A layout that wants a wide note gives that margin the width
    /// the note needs. <c>{page}</c> and <c>{pages}</c> are replaced as they are in a header, and nothing is
    /// clipped: a note wider than its margin runs into the content.
    /// </remarks>
    public string? Side { get; init; }

    /// <summary>Which margin <see cref="Side"/> stands in; the left one unless told otherwise.</summary>
    public PageSide SideAt { get; init; }

    /// <summary>The area inside the margins, where content is set.</summary>
    public Rect Content => new(Margins.Left, Margins.Top, Width - Margins.Horizontal, Height - Margins.Vertical);

    /// <summary>The same sheet with its longer side horizontal.</summary>
    public PageSetup Landscape() => this with { Width = Math.Max(Width, Height), Height = Math.Min(Width, Height) };

    private static PageSetup FromMillimeters(float width, float height, float margin) =>
        new(Points.FromMillimeters(width), Points.FromMillimeters(height), Edges.All(Points.FromMillimeters(margin)));
}
