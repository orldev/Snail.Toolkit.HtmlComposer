using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;

namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>Decides which declaration of each property wins for an element.</summary>
/// <remarks>
/// AngleSharp.Css parses the stylesheets, expands shorthands and matches selectors, and that is all it is
/// trusted with. Its computed styles are not used: they throw on <c>em</c> units without a render device,
/// leave <c>bolder</c> unresolved and skip inheritance, so values are computed by <see cref="Styles"/>.
/// Media queries are judged by their media type alone: <c>print</c> and <c>all</c> apply, every other type
/// does not, and features such as <c>min-width</c> are not evaluated.
/// <para>
/// The rules are put in cascade order once, when the sheets are read, so deciding an element only filters
/// them; and the decision is kept per element, because a table cell is asked twice, measured and then set,
/// and a nested table as many times again. Matching every selector against every element is what the
/// cascade costs, and this pays it once.
/// </para>
/// </remarks>
internal sealed class Cascade
{
    private readonly CssParser _parser = new();

    private readonly IReadOnlyList<Rule> _rules;

    private readonly Dictionary<IElement, IReadOnlyDictionary<string, string>> _decided = new(ReferenceEqualityComparer.Instance);

    private Cascade(IReadOnlyList<Rule> rules) => _rules = rules;

    private enum Origin
    {
        Agent,
        Author,
    }

    public static Cascade Of(IDocument document)
    {
        var parser = new CssParser();
        List<Rule> rules = [];

        Collect(UserAgentSheet.Sheet.Rules, Origin.Agent, rules);

        foreach (IElement? style in document.QuerySelectorAll("style").Where(style => IsPrinted(style.GetAttribute("media"))))
        {
            Collect(parser.ParseStyleSheet(style.TextContent).Rules, Origin.Author, rules);
        }

        return new Cascade([.. rules.OrderBy(rule => rule.Origin).ThenBy(rule => rule.Selector.Specificity).ThenBy(rule => rule.Order)]);
    }

    public IReadOnlyDictionary<string, string> Specified(IElement element)
    {
        if (_decided.TryGetValue(element, out IReadOnlyDictionary<string, string>? known))
        {
            return known;
        }

        Dictionary<string, string> decided = Decide(element);
        _decided[element] = decided;

        return decided;
    }

    private Dictionary<string, string> Decide(IElement element)
    {
        List<Rule> matched = [.. _rules.Where(rule => rule.Selector.Match(element, null))];
        ICssStyleDeclaration? inline = element.GetAttribute("style") is { Length: > 0 } text ? _parser.ParseDeclaration(text) : null;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (Rule? rule in matched)
        {
            Apply(rule.Style, isImportant: false, values);
        }

        Apply(inline, isImportant: false, values);

        foreach (Rule? rule in matched.Where(rule => rule.Origin == Origin.Author))
        {
            Apply(rule.Style, isImportant: true, values);
        }

        Apply(inline, isImportant: true, values);

        return values;
    }

    private static void Apply(ICssStyleDeclaration? declarations, bool isImportant, Dictionary<string, string> values)
    {
        foreach (ICssProperty? property in declarations?.Where(property => property.IsImportant == isImportant) ?? [])
        {
            values[property.Name] = property.Value;
        }
    }

    private static void Collect(IEnumerable<ICssRule> source, Origin origin, List<Rule> rules)
    {
        foreach (ICssRule rule in source)
        {
            if (rule is ICssStyleRule { Selector: { } selector } style)
            {
                rules.Add(new Rule(selector, style.Style, origin, rules.Count));
            }

            if (rule is ICssMediaRule media && IsPrinted(media.Media.MediaText))
            {
                Collect(media.Rules, origin, rules);
            }
        }
    }

    private static bool IsPrinted(string? media) =>
        string.IsNullOrWhiteSpace(media) || media.Split(',').Any(query => IsPrintedQuery(query.Trim()));

    private static bool IsPrintedQuery(string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool isNegated = words.Length > 0 && words[0].Equals("not", StringComparison.OrdinalIgnoreCase);
        string? type = words.SkipWhile(IsModifier).FirstOrDefault();

        bool isPrintType = type is null
            || type.StartsWith('(')
            || type.Equals("print", StringComparison.OrdinalIgnoreCase)
            || type.Equals("all", StringComparison.OrdinalIgnoreCase);

        return isPrintType != isNegated;
    }

    private static bool IsModifier(string word) =>
        word.Equals("not", StringComparison.OrdinalIgnoreCase) || word.Equals("only", StringComparison.OrdinalIgnoreCase);

    private sealed record Rule(ISelector Selector, ICssStyleDeclaration Style, Origin Origin, int Order);
}
