using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>The marker a list item hangs before its first line: a bullet to draw, or a number to set.</summary>
/// <remarks>
/// An item finds its own ordinal by counting the <c>li</c> siblings before it from the list's <c>start</c>,
/// so no counter travels with the walk and an item set twice, as a table cell is, numbers the same both
/// times. Alphabetic and roman numerals are formed here for the same reason: a marker is a property of the
/// item, not of the pass that reached it.
/// </remarks>
internal static class Numbering
{
    private static readonly (int Value, string Numeral)[] Numerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    public static Marker? MarkerOf(IElement item, Style style) => style.ListMarker switch
    {
        ListMarker.None => null,
        ListMarker.Disc or ListMarker.Circle or ListMarker.Square => new Marker(style.ListMarker, "", style),
        var numbering => new Marker(numbering, NumberOf(numbering, OrdinalOf(item)), style),
    };

    private static string NumberOf(ListMarker numbering, int ordinal) => numbering switch
    {
        ListMarker.LowerAlpha => $"{Alphabetic(ordinal)}.",
        ListMarker.UpperAlpha => $"{Alphabetic(ordinal).ToUpperInvariant()}.",
        ListMarker.LowerRoman => $"{Roman(ordinal).ToLowerInvariant()}.",
        ListMarker.UpperRoman => $"{Roman(ordinal)}.",
        _ => $"{ordinal}.",
    };

    private static int OrdinalOf(IElement item)
    {
        IElement? list = item.ParentElement;
        int start = int.TryParse(list?.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int first) ? first : 1;
        int preceding = list?.Children.TakeWhile(sibling => sibling != item).Count(IsItem) ?? 0;

        return start + preceding;
    }

    private static bool IsItem(IElement element) => element.LocalName.Equals("li", StringComparison.Ordinal);

    private static string Alphabetic(int ordinal)
    {
        var letters = new StringBuilder();

        for (int remaining = ordinal; remaining > 0; remaining = (remaining - 1) / 26)
        {
            letters.Insert(0, (char)('a' + ((remaining - 1) % 26)));
        }

        return letters.ToString();
    }

    private static string Roman(int ordinal)
    {
        var numeral = new StringBuilder();
        int remaining = ordinal;

        foreach ((int value, string symbol) in Numerals)
        {
            for (; remaining >= value; remaining -= value)
            {
                numeral.Append(symbol);
            }
        }

        return numeral.ToString();
    }
}
