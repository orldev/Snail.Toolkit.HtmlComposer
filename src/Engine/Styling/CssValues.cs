using System.Globalization;

namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>Reads CSS values as AngleSharp leaves them once shorthands are expanded.</summary>
/// <remarks>
/// Every number is read with the invariant culture. On a machine set to a culture with a decimal comma,
/// <c>1.5em</c> read with the current culture fails to parse, and the declaration silently falls back to its
/// initial value.
/// </remarks>
internal static class CssValues
{
    /// <summary>The largest length a declaration may state, in points: about 350 metres.</summary>
    /// <remarks>
    /// A number past float's range parses to infinity, and infinity times the zero share of a length is NaN:
    /// <c>font-size:1e39px</c> or a margin as large set every coordinate after it to a value no renderer can draw.
    /// Browsers clamp lengths far below this; a value beyond it is cut to it, and one that is not a finite
    /// number at all is not read.
    /// </remarks>
    public const float Largest = 1_000_000;

    private static readonly (string Unit, float Points)[] AbsoluteUnits =
    [
        ("px", Points.PerPixel),
        ("pt", 1f),
        ("pc", 12f),
        ("in", Points.PerInch),
        ("cm", Points.PerInch / 2.54f),
        ("mm", Points.PerInch / 25.4f),
        ("q", Points.PerInch / 101.6f),
    ];

    public static float? ReadNumber(string text) => ReadNumber(text.AsSpan());

    /// <summary>A length in points, or a percentage, as a declaration states it.</summary>
    /// <remarks>
    /// Read on spans, with the units tried in a fixed order — <c>rem</c> before <c>em</c>, which it ends with —
    /// rather than through an array of eleven units built per call and a substring per unit tried: every
    /// margin, padding and border of every element is read here.
    /// </remarks>
    public static Length? ReadLength(string text, float fontSize, float rootFontSize)
    {
        ReadOnlySpan<char> value = text.AsSpan().Trim();

        if (value.EndsWith('%'))
        {
            return ReadNumber(value[..^1]) is { } percent ? new Length(0, Bounded(percent)) : null;
        }

        if (ReadNumber(value) is { } unitless)
        {
            return unitless == 0 ? new Length(0) : null;
        }

        return Scaled(value, "rem", rootFontSize)
            ?? Scaled(value, "em", fontSize)
            ?? Scaled(value, "ex", fontSize / 2)
            ?? Scaled(value, "ch", fontSize / 2)
            ?? Absolute(value);
    }

    /// <summary>A colour as a declaration states it, or nothing when it cannot be read.</summary>
    /// <remarks>
    /// AngleSharp states a colour as <c>rgba(r, g, b, a)</c>, and every colour, background and border of every
    /// element is read here. The channels are split and read on spans, where a substring and an array of
    /// strings were made for every colour.
    /// </remarks>
    public static Rgba? ReadColor(string text, Rgba current)
    {
        string value = text.Trim();

        if (value.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return new Rgba(0, 0, 0, 0);
        }

        if (value.StartsWith('#'))
        {
            return ReadHex(value[1..]);
        }

        int open = value.IndexOf('(', StringComparison.Ordinal);
        if (open < 0 || !value.EndsWith(')'))
        {
            return null;
        }

        ReadOnlySpan<char> inner = value.AsSpan()[(open + 1)..^1];
        Span<Range> channels = stackalloc Range[5];
        int count = inner.SplitAny(channels, ", /", StringSplitOptions.RemoveEmptyEntries);
        if (count < 3)
        {
            return null;
        }

        byte alpha = count > 3 ? ReadAlpha(inner[channels[3]]) : (byte)255;

        return new Rgba(ReadChannel(inner[channels[0]]), ReadChannel(inner[channels[1]]), ReadChannel(inner[channels[2]]), alpha);
    }

    private static float? ReadNumber(ReadOnlySpan<char> text) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number) ? number : null;

    private static float Bounded(float value) => Math.Clamp(value, -Largest, Largest);

    private static Length? Scaled(ReadOnlySpan<char> value, string unit, float points) =>
        value.EndsWith(unit, StringComparison.OrdinalIgnoreCase)
        && ReadNumber(value[..^unit.Length]) is { } number
        && number * points is var scaled
        && float.IsFinite(scaled)
            ? new Length(Bounded(scaled))
            : null;

    private static Length? Absolute(ReadOnlySpan<char> value)
    {
        foreach ((string unit, float points) in AbsoluteUnits)
        {
            if (Scaled(value, unit, points) is { } length)
            {
                return length;
            }
        }

        return null;
    }

    private static byte ReadChannel(ReadOnlySpan<char> text) =>
        text is [.., '%'] ? Clamp((ReadNumber(text[..^1]) ?? 0) * 255f / 100f) : Clamp(ReadNumber(text) ?? 0);

    private static byte ReadAlpha(ReadOnlySpan<char> text) =>
        text is [.., '%'] ? Clamp((ReadNumber(text[..^1]) ?? 100) * 255f / 100f) : Clamp((ReadNumber(text) ?? 1) * 255f);

    private static byte Clamp(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    private static Rgba? ReadHex(string digits)
    {
        string expanded = digits.Length is 3 or 4 ? string.Concat(digits.Select(digit => $"{digit}{digit}")) : digits;

        if (expanded.Length is not (6 or 8)
            || !uint.TryParse(expanded, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
        {
            return null;
        }

        uint withAlpha = expanded.Length == 6 ? (packed << 8) | 0xFF : packed;

        return new Rgba((byte)(withAlpha >> 24), (byte)(withAlpha >> 16), (byte)(withAlpha >> 8), (byte)withAlpha);
    }
}
