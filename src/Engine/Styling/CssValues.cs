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

    public static float? ReadNumber(string text) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number : null;

    public static Length? ReadLength(string text, float fontSize, float rootFontSize)
    {
        string value = text.Trim();

        if (value.EndsWith('%'))
        {
            return ReadNumber(value[..^1]) is { } percent ? new Length(0, percent) : null;
        }

        if (ReadNumber(value) is { } unitless)
        {
            return unitless == 0 ? new Length(0) : null;
        }

        foreach ((string? unit, float points) in UnitsOf(fontSize, rootFontSize))
        {
            if (value.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && ReadNumber(value[..^unit.Length]) is { } number)
            {
                return new Length(number * points);
            }
        }

        return null;
    }

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

        string[] channels = value[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (channels.Length < 3)
        {
            return null;
        }

        byte alpha = channels.Length > 3 ? ReadAlpha(channels[3]) : (byte)255;

        return new Rgba(ReadChannel(channels[0]), ReadChannel(channels[1]), ReadChannel(channels[2]), alpha);
    }

    private static IEnumerable<(string Unit, float Points)> UnitsOf(float fontSize, float rootFontSize) =>
    [
        ("rem", rootFontSize),
        ("em", fontSize),
        ("ex", fontSize / 2),
        ("ch", fontSize / 2),
        .. AbsoluteUnits,
    ];

    private static byte ReadChannel(string text) =>
        text.EndsWith('%') ? Clamp((ReadNumber(text[..^1]) ?? 0) * 255f / 100f) : Clamp(ReadNumber(text) ?? 0);

    private static byte ReadAlpha(string text) =>
        text.EndsWith('%') ? Clamp((ReadNumber(text[..^1]) ?? 100) * 255f / 100f) : Clamp((ReadNumber(text) ?? 1) * 255f);

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
