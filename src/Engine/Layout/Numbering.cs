using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AngleSharp.Dom;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>The marker a list item hangs before its first line: a bullet to draw, or a number to set.</summary>
/// <remarks>
/// An item finds its own ordinal in its list, counted from the list's <c>start</c>, so no counter travels with
/// the walk and an item set twice, as a table cell is, numbers the same both times. Alphabetic and roman
/// numerals are formed here for the same reason: a marker is a property of the item, not of the pass that
/// reached it.
/// </remarks>
internal static class Numbering
{
    /// <summary>The largest number roman numerals write; past it, as in a browser, the number is written in figures.</summary>
    private const int LargestRoman = 3999;

    private static readonly (int Value, string Numeral)[] Numerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    /// <summary>The ordinal of every item of a list, counted once per list.</summary>
    /// <remarks>
    /// Each item used to count the siblings before it, so a list of n items cost n² steps: 40 000 items took
    /// nearly nine seconds to number. The table is keyed weakly by the list element, so it lives as long as the
    /// document does, and it is filled whole the first time any item of the list asks. The list's nodes are
    /// walked rather than its element collection, whose indexer filters the nodes again on every call.
    /// </remarks>
    private static readonly ConditionalWeakTable<IElement, Dictionary<IElement, long>> Ordinals = [];

    public static Marker? MarkerOf(IElement item, Style style) => style.ListMarker switch
    {
        ListMarker.None => null,
        ListMarker.Disc or ListMarker.Circle or ListMarker.Square => new Marker(style.ListMarker, "", style),
        var numbering => new Marker(numbering, NumberOf(numbering, OrdinalOf(item)), style),
    };

    /// <remarks>
    /// Roman numerals are written for 1 to 3999 and letters for numbers above zero, as browsers write them; any
    /// other number is written in figures. A list starting at two billion in roman numerals wrote two million
    /// «M» for every item, 231 MB for twenty items.
    /// </remarks>
    private static string NumberOf(ListMarker numbering, long ordinal) => numbering switch
    {
        ListMarker.LowerAlpha when ordinal > 0 => $"{Alphabetic(ordinal)}.",
        ListMarker.UpperAlpha when ordinal > 0 => $"{Alphabetic(ordinal).ToUpperInvariant()}.",
        ListMarker.LowerRoman when ordinal is > 0 and <= LargestRoman => $"{Roman((int)ordinal).ToLowerInvariant()}.",
        ListMarker.UpperRoman when ordinal is > 0 and <= LargestRoman => $"{Roman((int)ordinal)}.",
        _ => $"{ordinal.ToString(CultureInfo.InvariantCulture)}.",
    };

    private static long OrdinalOf(IElement item)
    {
        if (item.ParentElement is not { } list)
        {
            return 1;
        }

        Dictionary<IElement, long> ordinals = Ordinals.GetValue(list, Counted);

        return ordinals.TryGetValue(item, out long ordinal) ? ordinal : 1;
    }

    private static Dictionary<IElement, long> Counted(IElement list)
    {
        long next = long.TryParse(list.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long first) ? first : 1;
        Dictionary<IElement, long> ordinals = new(ReferenceEqualityComparer.Instance);
        INodeList children = list.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            if (children[index] is IElement child && IsItem(child))
            {
                ordinals[child] = next++;
            }
        }

        return ordinals;
    }

    private static bool IsItem(IElement element) => element.LocalName.Equals("li", StringComparison.Ordinal);

    private static string Alphabetic(long ordinal)
    {
        var letters = new StringBuilder();

        for (long remaining = ordinal; remaining > 0; remaining = (remaining - 1) / 26)
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
