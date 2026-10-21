using System.Globalization;
using AngleSharp.Dom;
using SkiaSharp;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Where a line of SVG text stands against its x position.</summary>
internal enum TextAnchor
{
    Start,
    Middle,
    End,
}

/// <summary>The presentation an SVG element is drawn with, inherited from its parent and refined by its own properties.</summary>
/// <param name="Fill">The fill colour, or <see langword="null"/> for none.</param>
/// <param name="Stroke">The stroke colour, or <see langword="null"/> for none.</param>
/// <param name="FillOpacity">The share of the fill colour that shows, from 0 to 1.</param>
/// <param name="StrokeOpacity">The share of the stroke colour that shows, from 0 to 1.</param>
/// <param name="StrokeWidth">The stroke width in user units.</param>
/// <param name="Dashes">The dash pattern in user units, or <see langword="null"/> for a solid stroke.</param>
/// <param name="Cap">How open ends of a stroke are finished.</param>
/// <param name="Join">How corners of a stroke are joined.</param>
/// <param name="Miter">The longest a mitred corner may reach, in stroke widths.</param>
/// <param name="IsEvenOdd">Whether overlapping parts of a shape cancel out rather than add up.</param>
/// <param name="Family">The CSS font family list of text.</param>
/// <param name="Size">The font size in user units.</param>
/// <param name="Weight">The CSS font weight.</param>
/// <param name="IsItalic">Whether an italic face is asked for.</param>
/// <param name="Spacing">Space added after every glyph, in user units.</param>
/// <param name="Anchor">Where text stands against its x position.</param>
/// <param name="Color">What <c>currentColor</c> means here.</param>
/// <param name="IsVisible">Whether shapes and text here are drawn at all.</param>
internal sealed record Presentation(
    SKColor? Fill,
    SKColor? Stroke,
    float FillOpacity,
    float StrokeOpacity,
    float StrokeWidth,
    float[]? Dashes,
    SKStrokeCap Cap,
    SKStrokeJoin Join,
    float Miter,
    bool IsEvenOdd,
    string Family,
    float Size,
    int Weight,
    bool IsItalic,
    float Spacing,
    TextAnchor Anchor,
    SKColor Color,
    bool IsVisible)
{
    /// <summary>What an SVG root starts from: black fill, no stroke, 16 pixel text.</summary>
    public static Presentation Initial { get; } = new(
        SKColors.Black, null, 1, 1, 1, null, SKStrokeCap.Butt, SKStrokeJoin.Miter, 4, false,
        "sans-serif", 16, 400, false, 0, TextAnchor.Start, SKColors.Black, true);

    /// <summary>The presentation of a child that declared the given properties.</summary>
    public Presentation Inherit(Properties properties, Area area)
    {
        SKColor color = Colors.Parse(properties.Get("color"), Color) ?? Color;
        float size = Length(properties.Get("font-size"), Size) ?? Size;
        Presentation sized = this with { Color = color, Size = size };
        Presentation painted = sized.Painted(properties, area);
        Presentation lettered = painted.Lettered(properties);

        return lettered;
    }

    /// <summary>The paint a child declares over this one: fill and stroke, their opacities, the line and whether it shows.</summary>
    private Presentation Painted(Properties properties, Area area)
    {
        Presentation painted = this with
        {
            Fill = PaintOf(properties.Get("fill"), Fill, Color),
            Stroke = PaintOf(properties.Get("stroke"), Stroke, Color),
            FillOpacity = Opacity(properties.Get("fill-opacity")) ?? FillOpacity,
            StrokeOpacity = Opacity(properties.Get("stroke-opacity")) ?? StrokeOpacity,
            StrokeWidth = Length(properties.Get("stroke-width"), area.Diagonal) ?? StrokeWidth,
            Dashes = DashesOf(properties.Get("stroke-dasharray"), area) ?? Dashes,
            Cap = CapOf(properties.Get("stroke-linecap")) ?? Cap,
            Join = JoinOf(properties.Get("stroke-linejoin")) ?? Join,
            Miter = Properties.NumberOf(properties.Get("stroke-miterlimit")) ?? Miter,
            IsEvenOdd = properties.Get("fill-rule") is { } rule ? rule.Equals("evenodd", StringComparison.OrdinalIgnoreCase) : IsEvenOdd,
            IsVisible = properties.Get("visibility") is { } visibility ? visibility.Equals("visible", StringComparison.OrdinalIgnoreCase) : IsVisible,
        };

        return painted;
    }

    /// <summary>The lettering a child declares over this one: the face, its spacing and where text is anchored.</summary>
    private Presentation Lettered(Properties properties)
    {
        Presentation lettered = this with
        {
            Family = properties.Get("font-family") ?? Family,
            Weight = WeightOf(properties.Get("font-weight")) ?? Weight,
            IsItalic = properties.Get("font-style") is { } style ? !style.Equals("normal", StringComparison.OrdinalIgnoreCase) : IsItalic,
            Spacing = SpacingOf(properties.Get("letter-spacing"), this) ?? Spacing,
            Anchor = AnchorOf(properties.Get("text-anchor")) ?? Anchor,
        };

        return lettered;
    }

    /// <summary>A length in user units: a plain number, an absolute unit, an em of the font size, or a share of a reference.</summary>
    public float? Length(string? value, float reference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        int end = trimmed.Length;

        while (end > 0 && (char.IsAsciiLetter(trimmed[end - 1]) || trimmed[end - 1] == '%'))
        {
            end--;
        }

        if (!float.TryParse(trimmed[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
        {
            return null;
        }

        return trimmed[end..].ToLowerInvariant() switch
        {
            "" or "px" => number,
            "pt" => number * 4 / 3,
            "pc" => number * 16,
            "in" => number * 96,
            "cm" => number * 96 / 2.54f,
            "mm" => number * 96 / 25.4f,
            "em" => number * Size,
            "ex" => number * Size / 2,
            "%" => number * reference / 100,
            _ => null,
        };
    }

    /// <summary>A colour with only the given share of its alpha left.</summary>
    public static SKColor Faded(SKColor color, float opacity) =>
        color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));

    private static SKColor? PaintOf(string? value, SKColor? inherited, SKColor current)
    {
        if (value is null)
        {
            return inherited;
        }

        string paint = value.Trim();

        if (paint.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int close = paint.IndexOf(')', StringComparison.Ordinal);

            return close < 0 ? null : Colors.Parse(paint[(close + 1)..], current);
        }

        return Colors.IsNone(paint) ? null : Colors.Parse(paint, current) ?? inherited;
    }

    private static float? Opacity(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        bool isShare = trimmed.EndsWith('%');
        float? number = Properties.NumberOf(isShare ? trimmed[..^1] : trimmed);

        return number is { } found ? Math.Clamp(isShare ? found / 100 : found, 0, 1) : null;
    }

    private float[]? DashesOf(string? value, Area area)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        string[] parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        float[] dashes = new float[parts.Length];

        for (int index = 0; index < parts.Length; index++)
        {
            if (Length(parts[index], area.Diagonal) is not { } dash || dash < 0)
            {
                return null;
            }

            dashes[index] = dash;
        }

        return dashes;
    }

    private static SKStrokeCap? CapOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "butt" => SKStrokeCap.Butt,
        "round" => SKStrokeCap.Round,
        "square" => SKStrokeCap.Square,
        _ => null,
    };

    private static SKStrokeJoin? JoinOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "miter" or "miter-clip" or "arcs" => SKStrokeJoin.Miter,
        "round" => SKStrokeJoin.Round,
        "bevel" => SKStrokeJoin.Bevel,
        _ => null,
    };

    private int? WeightOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "normal" => 400,
        "bold" => 700,
        "bolder" => Math.Min(900, Weight + 300),
        "lighter" => Math.Max(100, Weight - 300),
        { } number => int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) ? Math.Clamp(weight, 1, 1000) : null,
    };

    private static float? SpacingOf(string? value, Presentation sized) =>
        value?.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase) == true ? 0 : sized.Length(value, sized.Size);

    private static TextAnchor? AnchorOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "start" => TextAnchor.Start,
        "middle" => TextAnchor.Middle,
        "end" => TextAnchor.End,
        _ => null,
    };
}

/// <summary>The properties an SVG element declares, as presentation attributes and in its <c>style</c> attribute.</summary>
/// <remarks>A declaration in <c>style</c> wins over the attribute of the same name, as the cascade of SVG says.</remarks>
internal sealed class Properties
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The properties of one element.</summary>
    public static Properties Of(IElement element)
    {
        var properties = new Properties();

        foreach (IAttr attribute in element.Attributes)
        {
            properties._values[attribute.Name] = attribute.Value;
        }

        string[] declarations = (element.GetAttribute("style") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (string declaration in declarations)
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0)
            {
                string value = declaration[(colon + 1)..].Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();

                properties._values[declaration[..colon].Trim()] = value;
            }
        }

        return properties;
    }

    /// <summary>The value of a property, or nothing when the element does not declare it or asks to inherit it.</summary>
    public string? Get(string name) =>
        _values.TryGetValue(name, out string? value) && !value.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase) ? value : null;

    /// <summary>Whether the property is declared with the given value.</summary>
    public bool Has(string name, string value) => Get(name)?.Trim().Equals(value, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A property read as a plain number.</summary>
    public float? Number(string name) => NumberOf(Get(name));

    /// <summary>A plain number, or nothing when the text is not one.</summary>
    public static float? NumberOf(string? value) =>
        float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number : null;
}

/// <summary>Reads CSS colours as SVG writes them: hex, <c>rgb()</c>, <c>rgba()</c> and the common names.</summary>
internal static class Colors
{
    private static readonly Dictionary<string, SKColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new SKColor(0x00, 0x00, 0x00),
        ["white"] = new SKColor(0xFF, 0xFF, 0xFF),
        ["red"] = new SKColor(0xFF, 0x00, 0x00),
        ["green"] = new SKColor(0x00, 0x80, 0x00),
        ["blue"] = new SKColor(0x00, 0x00, 0xFF),
        ["yellow"] = new SKColor(0xFF, 0xFF, 0x00),
        ["orange"] = new SKColor(0xFF, 0xA5, 0x00),
        ["gray"] = new SKColor(0x80, 0x80, 0x80),
        ["grey"] = new SKColor(0x80, 0x80, 0x80),
        ["silver"] = new SKColor(0xC0, 0xC0, 0xC0),
        ["maroon"] = new SKColor(0x80, 0x00, 0x00),
        ["navy"] = new SKColor(0x00, 0x00, 0x80),
        ["purple"] = new SKColor(0x80, 0x00, 0x80),
        ["teal"] = new SKColor(0x00, 0x80, 0x80),
        ["olive"] = new SKColor(0x80, 0x80, 0x00),
        ["lime"] = new SKColor(0x00, 0xFF, 0x00),
        ["aqua"] = new SKColor(0x00, 0xFF, 0xFF),
        ["cyan"] = new SKColor(0x00, 0xFF, 0xFF),
        ["fuchsia"] = new SKColor(0xFF, 0x00, 0xFF),
        ["magenta"] = new SKColor(0xFF, 0x00, 0xFF),
        ["transparent"] = new SKColor(0x00, 0x00, 0x00, 0x00),
    };

    /// <summary>Whether the paint asks for nothing to be drawn.</summary>
    public static bool IsNone(string value) => value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>The colour the text names, <c>currentColor</c> included, or nothing when it names none.</summary>
    public static SKColor? Parse(string? value, SKColor current)
    {
        string text = value?.Trim() ?? "";

        if (text.Length == 0)
        {
            return null;
        }

        if (text.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (text[0] == '#')
        {
            return Hex(text[1..]);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return Functional(text);
        }

        return Named.TryGetValue(text, out SKColor named) ? named : null;
    }

    private static SKColor? Hex(string digits)
    {
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return null;
        }

        return digits.Length switch
        {
            3 => new SKColor(Doubled(value >> 8), Doubled(value >> 4), Doubled(value)),
            4 => new SKColor(Doubled(value >> 12), Doubled(value >> 8), Doubled(value >> 4), Doubled(value)),
            6 => new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value),
            8 => new SKColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            _ => null,
        };
    }

    private static byte Doubled(uint nibble) => (byte)((nibble & 0xF) * 0x11);

    private static SKColor? Functional(string text)
    {
        int open = text.IndexOf('(', StringComparison.Ordinal);
        int close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return null;
        }

        string[] parts = text[(open + 1)..close].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return null;
        }

        byte[] channels = new byte[3];

        for (int index = 0; index < 3; index++)
        {
            if (Channel(parts[index]) is not { } channel)
            {
                return null;
            }

            channels[index] = channel;
        }

        float alpha = parts.Length > 3 ? Alpha(parts[3]) ?? 1 : 1;

        return new SKColor(channels[0], channels[1], channels[2], (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));
    }

    private static byte? Channel(string part)
    {
        bool isShare = part.EndsWith('%');
        float? number = Properties.NumberOf(isShare ? part[..^1] : part);

        return number is { } found ? (byte)Math.Round(Math.Clamp(isShare ? found * 2.55f : found, 0, 255)) : null;
    }

    private static float? Alpha(string part)
    {
        bool isShare = part.EndsWith('%');
        float? number = Properties.NumberOf(isShare ? part[..^1] : part);

        return number is { } found && isShare ? found / 100 : number;
    }
}
