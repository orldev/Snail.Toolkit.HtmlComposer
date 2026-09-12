namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

internal enum Display
{
    Inline,
    Block,
    ListItem,
    None,
}

internal enum TextAlign
{
    Left,
    Right,
    Center,
    Justify,
}

internal enum WhiteSpace
{
    Normal,
    NoWrap,
    Pre,
    PreWrap,
    PreLine,
}

internal enum BreakRule
{
    Auto,
    Page,
    Avoid,
}

internal enum TextTransform
{
    None,
    Uppercase,
    Lowercase,
    Capitalize,
}

internal enum VerticalAlign
{
    Baseline,
    Middle,
    TextTop,
    TextBottom,
    Sub,
    Super,
}

internal enum ObjectFit
{
    Fill,
    Cover,
    Contain,
}

internal enum ListMarker
{
    None,
    Disc,
    Circle,
    Square,
    Decimal,
    LowerAlpha,
    UpperAlpha,
    LowerRoman,
    UpperRoman,
}

/// <summary>A computed <c>line-height</c> other than <c>normal</c>.</summary>
/// <remarks>
/// A unitless factor is inherited as the factor, not as the length it produced: a heading inside a paragraph
/// with <c>line-height: 1.5</c> gets one and a half of its own size, not of the paragraph's.
/// </remarks>
internal readonly record struct LineHeight(float Value, bool IsFactor)
{
    public float Resolve(float fontSize) => IsFactor ? Value * fontSize : Value;
}

/// <summary>Margins or padding on four sides, where a missing side is <c>auto</c>.</summary>
internal readonly record struct Sides(Length? Top, Length? Right, Length? Bottom, Length? Left)
{
    public static Sides Zero { get; } = new(new Length(0), new Length(0), new Length(0), new Length(0));

    /// <summary>The four lengths in points against the containing block, an <c>auto</c> side counting as nothing.</summary>
    public Edges Resolve(float basis) =>
        new(Top?.Resolve(basis) ?? 0, Right?.Resolve(basis) ?? 0, Bottom?.Resolve(basis) ?? 0, Left?.Resolve(basis) ?? 0);
}

/// <summary>The computed style of one element: every value absolute except what depends on layout.</summary>
internal sealed record Style
{
    public static Style Initial { get; } = new() { Font = new Font("sans-serif", 12f), Display = Display.Block };

    public required Font Font { get; init; }

    public Display Display { get; init; }

    public Rgba Color { get; init; } = Rgba.Black;

    public LineHeight? LineHeight { get; init; }

    public TextAlign TextAlign { get; init; }

    public TextDecoration Decoration { get; init; }

    public TextTransform Transform { get; init; }

    /// <summary>Where inline content sits against the baseline, and where a cell holds its content.</summary>
    /// <remarks>Not inherited: CSS gives each box its own alignment rather than passing one down.</remarks>
    public VerticalAlign VerticalAlign { get; init; }

    /// <summary>How a picture fills a frame whose shape is not its own.</summary>
    public ObjectFit ObjectFit { get; init; }

    /// <summary>Whether a table draws one rule where two cells meet instead of each cell drawing its own.</summary>
    /// <remarks>Inherited, as CSS has it, so a cell reads the decision its table made.</remarks>
    public bool IsCollapsed { get; init; }

    public WhiteSpace WhiteSpace { get; init; }

    public Length TextIndent { get; init; }

    public ListMarker ListMarker { get; init; }

    public int Orphans { get; init; } = 2;

    public int Widows { get; init; } = 2;

    public Sides Margin { get; init; } = Sides.Zero;

    public Sides Padding { get; init; } = Sides.Zero;

    public Border Border { get; init; }

    public float BorderRadius { get; init; }

    public Rgba? Background { get; init; }

    public Length? Width { get; init; }

    public Length? Height { get; init; }

    public Length? MaxWidth { get; init; }

    public bool IsBorderBox { get; init; }

    public BreakRule BreakBefore { get; init; }

    public BreakRule BreakAfter { get; init; }

    public bool AvoidsBreakInside { get; init; }

    public bool IsBlockLevel => Display is Display.Block or Display.ListItem;
}
