namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>Turns the declarations that won the cascade into a computed style.</summary>
/// <remarks>
/// Inheritance happens here rather than in the cascade, because a child inherits its parent's computed value
/// and never its declaration. Inheriting <c>font-size: 1.5em</c> as written would grow every nested level by
/// half again instead of keeping the size the parent settled on.
/// </remarks>
internal static class Styles
{
    private const float MediumFontSize = 12f;

    private static readonly Dictionary<string, float> AbsoluteSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xx-small"] = 0.6f,
        ["x-small"] = 0.75f,
        ["small"] = 8f / 9f,
        ["medium"] = 1f,
        ["large"] = 1.2f,
        ["x-large"] = 1.5f,
        ["xx-large"] = 2f,
        ["xxx-large"] = 3f,
    };

    private static readonly SideNames Margins = new("margin-top", "margin-right", "margin-bottom", "margin-left");

    private static readonly SideNames Paddings = new("padding-top", "padding-right", "padding-bottom", "padding-left");

    private static readonly BorderNames Top = new("border-top-style", "border-top-width", "border-top-color");

    private static readonly BorderNames Right = new("border-right-style", "border-right-width", "border-right-color");

    private static readonly BorderNames Bottom = new("border-bottom-style", "border-bottom-width", "border-bottom-color");

    private static readonly BorderNames Left = new("border-left-style", "border-left-width", "border-left-color");

    public static Style Compute(IReadOnlyDictionary<string, string> specified, Style parent, float rootFontSize)
    {
        string? Declared(string property) => specified.GetValueOrDefault(property);

        Font font = FontOf(specified, parent.Font, rootFontSize);
        Rgba color = Declared("color") is { } declaredColor
            ? CssValues.ReadColor(declaredColor, parent.Color) ?? parent.Color
            : parent.Color;
        float size = font.Size;
        Display display = DisplayOf(Declared("display"));
        VerticalAlign verticalAlign = VerticalAlignOf(Declared("vertical-align"));
        bool isBlock = display is Display.Block or Display.ListItem;

        var style = new Style
        {
            Font = font,
            Color = color,
            Display = display,
            Shift = (isBlock ? 0 : parent.Shift) + ShiftOf(verticalAlign, size),
            LineHeight = LineHeightOf(Declared("line-height"), parent.LineHeight, size, rootFontSize),
            TextAlign = TextAlignOf(Declared("text-align"), parent.TextAlign),
            Decoration = parent.Decoration | DecorationOf(Declared("text-decoration-line") ?? Declared("text-decoration")),
            Transform = TransformOf(Declared("text-transform"), parent.Transform),
            VerticalAlign = verticalAlign,
            ObjectFit = ObjectFitOf(Declared("object-fit")),
            IsCollapsed = IsCollapsed(Declared("border-collapse"), parent.IsCollapsed),
            WhiteSpace = WhiteSpaceOf(Declared("white-space"), parent.WhiteSpace),
            TextIndent = LengthOf(Declared("text-indent"), size, rootFontSize) ?? parent.TextIndent,
            ListMarker = ListMarkerOf(Declared("list-style-type"), parent.ListMarker),
            Orphans = CountOf(Declared("orphans"), parent.Orphans),
            Widows = CountOf(Declared("widows"), parent.Widows),
            Margin = SidesOf(specified, Margins, size, rootFontSize),
            Padding = NonNegative(SidesOf(specified, Paddings, size, rootFontSize)),
            Border = BorderOf(specified, color, size, rootFontSize),
            BorderRadius = Declared("border-top-left-radius")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var radius, ..]
                ? CssValues.ReadLength(radius, size, rootFontSize)?.Points ?? 0
                : 0,
            Background = BackgroundOf(Declared("background-color"), color),
            Width = SizeOf(Declared("width"), size, rootFontSize),
            Height = SizeOf(Declared("height"), size, rootFontSize),
            MaxWidth = SizeOf(Declared("max-width"), size, rootFontSize),
            IsBorderBox = Keyword(Declared("box-sizing")) is "border-box",
            ClonesDecoration = Keyword(Declared("box-decoration-break")) is "clone",
            BreakBefore = BreakOf(Declared("break-before") ?? Declared("page-break-before")),
            BreakAfter = BreakOf(Declared("break-after") ?? Declared("page-break-after")),
            AvoidsBreakInside = BreakOf(Declared("break-inside") ?? Declared("page-break-inside")) == BreakRule.Avoid,
        };

        return style;
    }

    private static Font FontOf(IReadOnlyDictionary<string, string> specified, Font parent, float rootFontSize)
    {
        string? declaredFamily = specified.GetValueOrDefault("font-family");

        string family = Keyword(declaredFamily) switch
        {
            null or "inherit" or "unset" => parent.Family,
            "initial" => Style.Initial.Font.Family,
            _ => declaredFamily!.Trim(),
        };

        float size = FontSizeOf(specified.GetValueOrDefault("font-size"), parent.Size, rootFontSize);

        return new Font(
            family,
            size,
            FontWeightOf(specified.GetValueOrDefault("font-weight"), parent.Weight),
            IsItalic(specified.GetValueOrDefault("font-style"), parent.IsItalic),
            TrackingOf(specified.GetValueOrDefault("letter-spacing"), parent.Tracking, size, rootFontSize));
    }

    private static float TrackingOf(string? value, float parent, float fontSize, float rootFontSize) => Keyword(value) switch
    {
        null or "inherit" or "unset" => parent,
        "normal" or "initial" => 0,
        var keyword => CssValues.ReadLength(keyword, fontSize, rootFontSize)?.Resolve(fontSize) ?? parent,
    };

    /// <summary>The largest font size, in points: a metre and more of type.</summary>
    /// <remarks>
    /// Relative sizes compound down the tree, and 140 nested <c>2em</c> reach infinity: every run below set at an
    /// infinite size and every position after it at NaN. A size is kept within this whatever its ancestors did.
    /// </remarks>
    private const float LargestFontSize = 3_000;

    /// <summary>How far <c>sub</c> and <c>super</c> move the baseline, downwards being positive.</summary>
    /// <remarks>
    /// CSS fixes no distance; a fifth of the em down and a third up are the usual ones, taken from the element's
    /// own size, so a smaller face raised in a larger one moves by its own measure.
    /// </remarks>
    private static float ShiftOf(VerticalAlign align, float size) => align switch
    {
        VerticalAlign.Sub => size * 0.2f,
        VerticalAlign.Super => size * -0.33f,
        _ => 0,
    };

    private static float FontSizeOf(string? value, float parentSize, float rootFontSize) =>
        Math.Clamp(DeclaredFontSizeOf(value, parentSize, rootFontSize), 0, LargestFontSize);

    /// <remarks>A negative size is not a size, and a browser drops the declaration; so is it dropped here.</remarks>
    private static float DeclaredFontSizeOf(string? value, float parentSize, float rootFontSize)
    {
        string? keyword = Keyword(value);

        if (keyword is null or "inherit" or "unset")
        {
            return parentSize;
        }

        if (keyword is "initial")
        {
            return MediumFontSize;
        }

        if (AbsoluteSizes.TryGetValue(keyword, out float factor))
        {
            return MediumFontSize * factor;
        }

        return keyword switch
        {
            "smaller" => parentSize / 1.2f,
            "larger" => parentSize * 1.2f,
            _ => CssValues.ReadLength(keyword, parentSize, rootFontSize)?.Resolve(parentSize) is >= 0 and var size ? size : parentSize,
        };
    }

    private static int FontWeightOf(string? value, int parent) => Keyword(value) switch
    {
        null or "inherit" or "unset" => parent,
        "normal" or "initial" => 400,
        "bold" => 700,
        "bolder" => parent < 350 ? 400 : parent < 550 ? 700 : 900,
        "lighter" => parent < 550 ? 100 : parent < 750 ? 400 : 700,
        var keyword => CssValues.ReadNumber(keyword) is { } weight ? (int)Math.Clamp(weight, 1, 1000) : parent,
    };

    private static bool IsItalic(string? value, bool parent) => Keyword(value) switch
    {
        "italic" or "oblique" => true,
        "normal" or "initial" => false,
        { } keyword when keyword.StartsWith("oblique", StringComparison.Ordinal) => true,
        _ => parent,
    };

    private static Display DisplayOf(string? value) => Keyword(value) switch
    {
        "block" or "flex" or "grid" or "flow-root" or "table" or "table-row" or "table-row-group"
            or "table-header-group" or "table-footer-group" or "table-caption" => Display.Block,
        "list-item" => Display.ListItem,
        "none" => Display.None,
        _ => Display.Inline,
    };

    private static LineHeight? LineHeightOf(string? value, LineHeight? parent, float fontSize, float rootFontSize)
    {
        string? keyword = Keyword(value);

        if (keyword is null or "inherit" or "unset")
        {
            return parent;
        }

        if (keyword is "normal" or "initial")
        {
            return null;
        }

        if (CssValues.ReadNumber(keyword) is { } factor)
        {
            return factor >= 0 ? new LineHeight(factor, IsFactor: true) : parent;
        }

        return CssValues.ReadLength(keyword, fontSize, rootFontSize) is { } length && length.Resolve(fontSize) is >= 0 and var points
            ? new LineHeight(points, IsFactor: false)
            : parent;
    }

    private static TextAlign TextAlignOf(string? value, TextAlign parent) => Keyword(value) switch
    {
        "left" or "start" or "initial" => TextAlign.Left,
        "right" or "end" => TextAlign.Right,
        "center" => TextAlign.Center,
        "justify" => TextAlign.Justify,
        _ => parent,
    };

    private static TextDecoration DecorationOf(string? value) =>
        (Mentions(value, "underline") ? TextDecoration.Underline : TextDecoration.None)
        | (Mentions(value, "line-through") ? TextDecoration.LineThrough : TextDecoration.None);

    private static TextTransform TransformOf(string? value, TextTransform parent) => Keyword(value) switch
    {
        "uppercase" => TextTransform.Uppercase,
        "lowercase" => TextTransform.Lowercase,
        "capitalize" => TextTransform.Capitalize,
        "none" or "initial" => TextTransform.None,
        _ => parent,
    };

    /// <summary>Reads <c>vertical-align</c>, with <c>top</c> and <c>bottom</c> taken as their text-relative kin.</summary>
    /// <remarks>
    /// Aligning with the line box itself asks how tall the line is, which is the very thing the alignment is
    /// being used to work out. Against the text of the line there is no such circle.
    /// </remarks>
    private static VerticalAlign VerticalAlignOf(string? value) => Keyword(value) switch
    {
        "middle" => VerticalAlign.Middle,
        "top" or "text-top" => VerticalAlign.TextTop,
        "bottom" or "text-bottom" => VerticalAlign.TextBottom,
        "sub" => VerticalAlign.Sub,
        "super" => VerticalAlign.Super,
        _ => VerticalAlign.Baseline,
    };

    private static bool IsCollapsed(string? value, bool parent) => Keyword(value) switch
    {
        "collapse" => true,
        "separate" or "initial" => false,
        _ => parent,
    };

    private static ObjectFit ObjectFitOf(string? value) => Keyword(value) switch
    {
        "cover" => ObjectFit.Cover,
        "contain" => ObjectFit.Contain,
        _ => ObjectFit.Fill,
    };

    private static WhiteSpace WhiteSpaceOf(string? value, WhiteSpace parent) => Keyword(value) switch
    {
        "normal" or "initial" => WhiteSpace.Normal,
        "nowrap" => WhiteSpace.NoWrap,
        "pre" => WhiteSpace.Pre,
        "pre-wrap" or "break-spaces" => WhiteSpace.PreWrap,
        "pre-line" => WhiteSpace.PreLine,
        _ => parent,
    };

    private static ListMarker ListMarkerOf(string? value, ListMarker parent) => Keyword(value) switch
    {
        "none" => ListMarker.None,
        "disc" or "initial" => ListMarker.Disc,
        "circle" => ListMarker.Circle,
        "square" => ListMarker.Square,
        "decimal" or "decimal-leading-zero" => ListMarker.Decimal,
        "lower-alpha" or "lower-latin" => ListMarker.LowerAlpha,
        "upper-alpha" or "upper-latin" => ListMarker.UpperAlpha,
        "lower-roman" => ListMarker.LowerRoman,
        "upper-roman" => ListMarker.UpperRoman,
        _ => parent,
    };

    /// <summary>A count of lines the style declares — <c>orphans</c>, <c>widows</c> — held to what an int can carry.</summary>
    /// <remarks>
    /// The number comes from the markup, and narrowing a float past <see cref="int.MaxValue"/> is undefined
    /// rather than saturating: <c>orphans: 99999999999</c> would arrive as <see cref="int.MinValue"/>, a count
    /// below one, and quietly stand for none. A count below one is not a count and keeps the parent's.
    /// </remarks>
    private static int CountOf(string? value, int parent)
    {
        if (value is null || CssValues.ReadNumber(value) is not { } count || count < 1)
        {
            return parent;
        }

        return count >= int.MaxValue ? int.MaxValue : (int)count;
    }

    private static Length? LengthOf(string? value, float fontSize, float rootFontSize) =>
        value is null ? null : CssValues.ReadLength(value, fontSize, rootFontSize);

    /// <summary>The four sides of a margin or a padding.</summary>
    /// <remarks>The property names are constants: building them per element was twenty strings for every element styled.</remarks>
    private static Sides SidesOf(IReadOnlyDictionary<string, string> specified, SideNames properties, float fontSize, float rootFontSize) =>
        new(
            SideOf(specified.GetValueOrDefault(properties.Top), fontSize, rootFontSize),
            SideOf(specified.GetValueOrDefault(properties.Right), fontSize, rootFontSize),
            SideOf(specified.GetValueOrDefault(properties.Bottom), fontSize, rootFontSize),
            SideOf(specified.GetValueOrDefault(properties.Left), fontSize, rootFontSize));

    /// <summary>Sides that may not be negative — a padding — with every negative side read as none, as a browser reads it.</summary>
    private static Sides NonNegative(Sides sides) =>
        new(NonNegative(sides.Top), NonNegative(sides.Right), NonNegative(sides.Bottom), NonNegative(sides.Left));

    private static Length? NonNegative(Length? length) =>
        length is { } side && (side.Points < 0 || side.Percent < 0) ? new Length(0) : length;

    private static Length? SideOf(string? value, float fontSize, float rootFontSize) => Keyword(value) switch
    {
        null => new Length(0),
        "auto" => null,
        var keyword => CssValues.ReadLength(keyword, fontSize, rootFontSize) ?? new Length(0),
    };

    private static Border BorderOf(IReadOnlyDictionary<string, string> specified, Rgba color, float fontSize, float rootFontSize) =>
        new(
            BorderSideOf(specified, Top, color, fontSize, rootFontSize),
            BorderSideOf(specified, Right, color, fontSize, rootFontSize),
            BorderSideOf(specified, Bottom, color, fontSize, rootFontSize),
            BorderSideOf(specified, Left, color, fontSize, rootFontSize));

    private static BorderSide BorderSideOf(
        IReadOnlyDictionary<string, string> specified,
        BorderNames side,
        Rgba color,
        float fontSize,
        float rootFontSize)
    {
        string? stroke = Keyword(specified.GetValueOrDefault(side.Style));

        if (stroke is null or "none" or "hidden" or "initial")
        {
            return default;
        }

        float width = BorderWidthOf(specified.GetValueOrDefault(side.Width), fontSize, rootFontSize);
        Rgba paint = specified.GetValueOrDefault(side.Color) is { } declared
            ? CssValues.ReadColor(declared, color) ?? color
            : color;

        return new BorderSide(width, paint, StrokeOf(stroke));
    }

    private static BorderStyle StrokeOf(string stroke) => stroke switch
    {
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        _ => BorderStyle.Solid,
    };

    private static float BorderWidthOf(string? value, float fontSize, float rootFontSize) => Keyword(value) switch
    {
        null or "medium" or "initial" => Points.FromPixels(3),
        "thin" => Points.FromPixels(1),
        "thick" => Points.FromPixels(5),
        var keyword => CssValues.ReadLength(keyword, fontSize, rootFontSize)?.Points is >= 0 and var width ? width : Points.FromPixels(3),
    };

    private static Rgba? BackgroundOf(string? value, Rgba color) =>
        value is not null && CssValues.ReadColor(value, color) is { IsVisible: true } paint ? paint : null;

    /// <remarks>A negative width or height is not a size, and a browser drops the declaration; it reads here as <c>auto</c>.</remarks>
    private static Length? SizeOf(string? value, float fontSize, float rootFontSize) => Keyword(value) switch
    {
        null or "auto" or "none" or "initial" => null,
        var keyword => CssValues.ReadLength(keyword, fontSize, rootFontSize) is { Points: >= 0, Percent: >= 0 } length ? length : null,
    };

    private static BreakRule BreakOf(string? value) => Keyword(value) switch
    {
        "page" or "always" or "left" or "right" or "recto" or "verso" => BreakRule.Page,
        "avoid" or "avoid-page" => BreakRule.Avoid,
        _ => BreakRule.Auto,
    };

    private static bool Mentions(string? value, string word) =>
        value?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Keyword(string? value) => value?.Trim().ToLowerInvariant();

    /// <summary>The names of the four sides of a box property, in the order CSS lists them.</summary>
    private readonly record struct SideNames(string Top, string Right, string Bottom, string Left);

    /// <summary>The names of the three properties that draw one side of a border.</summary>
    private readonly record struct BorderNames(string Style, string Width, string Color);
}
