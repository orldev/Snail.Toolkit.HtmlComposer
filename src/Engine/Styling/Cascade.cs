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
    private readonly CssParser _parser;

    private readonly Rule[] _rules;

    private readonly Dictionary<IElement, IReadOnlyDictionary<string, string>> _decided = new(ReferenceEqualityComparer.Instance);

    private readonly List<Rule> _matched = [];

    private readonly Dictionary<string, Inline> _inline = new(StringComparer.Ordinal);

    private Cascade(CssParser parser, Rule[] rules)
    {
        _parser = parser;
        _rules = rules;
    }

    private enum Origin
    {
        Agent,
        Author,
    }

    /// <summary>The cascade of a document's own sheets, read with the parser of the composition it belongs to.</summary>
    /// <remarks>
    /// One parser serves a whole composition, as a composition is set on one thread: each parser builds a
    /// browsing context of its own, and one per cascade, one more per sheet and one per running line cost a
    /// fifth of what a short report allocated.
    /// </remarks>
    public static Cascade Of(IDocument document, CssParser parser)
    {
        List<Rule> rules = [.. AgentRules];

        foreach (IElement? style in document.QuerySelectorAll("style").Where(style => IsPrinted(style.GetAttribute("media"))))
        {
            Collect(parser.ParseStyleSheet(style.TextContent).Rules, Origin.Author, rules);
        }

        return new Cascade(parser, [.. AgentRules, .. InCascadeOrder(rules.Skip(AgentRules.Length))]);
    }

    /// <summary>The rules of the user agent's sheet, read and put in cascade order once for the process.</summary>
    /// <remarks>
    /// The sheet is the same for every document, and every cascade — one per section and one per running
    /// line — read its declarations again and sorted its rules again. They come first in the cascade
    /// whatever the document says, so the document's own rules are sorted after them, numbered on from them.
    /// </remarks>
    private static Rule[] AgentRules { get; } = ReadAgentRules();

    private static Rule[] ReadAgentRules()
    {
        List<Rule> rules = [];

        Collect(UserAgentSheet.Sheet.Rules, Origin.Agent, rules);

        return [.. InCascadeOrder(rules)];
    }

    private static IEnumerable<Rule> InCascadeOrder(IEnumerable<Rule> rules) =>
        rules.OrderBy(rule => rule.Selector.Specificity).ThenBy(rule => rule.Order);

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

    /// <summary>The value of each property for an element: the normal declarations, then the important ones.</summary>
    /// <remarks>
    /// The rules carry their declarations read once, with the sheets: reading them here filtered every rule
    /// with a closure, enumerated each matched rule's declarations twice and serialised each value again for
    /// every element it matched. The matched list is kept between elements, as one cascade serves one document
    /// and is never asked from two threads. The values are given room for every declaration that matched from
    /// the start, rather than growing their table two and three times for every element.
    /// </remarks>
    private Dictionary<string, string> Decide(IElement element)
    {
        _matched.Clear();

        int declared = 0;

        foreach (Rule rule in _rules)
        {
            if (rule.Selector.Match(element, null))
            {
                _matched.Add(rule);
                declared += rule.Normal.Length + rule.Important.Length;
            }
        }

        Inline? inline = element.GetAttribute("style") is { Length: > 0 } text ? InlineOf(text) : null;
        declared += (inline?.Normal.Length ?? 0) + (inline?.Important.Length ?? 0);
        Dictionary<string, string> values = new(declared, StringComparer.OrdinalIgnoreCase);

        foreach (Rule rule in _matched)
        {
            Apply(rule.Normal, values);
        }

        Apply(inline?.Normal ?? [], values);

        foreach (Rule rule in _matched)
        {
            if (rule.Origin == Origin.Author)
            {
                Apply(rule.Important, values);
            }
        }

        Apply(inline?.Important ?? [], values);

        return values;
    }

    /// <summary>The declarations of a <c>style</c> attribute, read once for every element that carries the same text.</summary>
    /// <remarks>
    /// A template repeats one attribute on every row and every cell, and reading one cost a parse of its own
    /// — a browsing context's tokenizer and a pooled buffer that never came back to the pool, some 7.5 KB a
    /// time — and two passes with a closure each. The declarations are read as the sheets' are, normal and
    /// important apart and in the order written, so applying them is what applying the parse was.
    /// </remarks>
    private Inline InlineOf(string text)
    {
        if (_inline.TryGetValue(text, out Inline? known))
        {
            return known;
        }

        ICssStyleDeclaration? declarations = _parser.ParseDeclaration(text);
        known = declarations is null
            ? new Inline([], [])
            : new Inline(Declared(declarations, isImportant: false), Declared(declarations, isImportant: true));
        _inline[text] = known;

        return known;
    }

    private static void Apply((string Name, string Value)[] declarations, Dictionary<string, string> values)
    {
        foreach ((string name, string value) in declarations)
        {
            values[name] = value;
        }
    }

    private static (string Name, string Value)[] Declared(ICssStyleDeclaration declarations, bool isImportant) =>
        [.. declarations.Where(property => property.IsImportant == isImportant).Select(property => (property.Name, property.Value))];

    private static void Collect(IEnumerable<ICssRule> source, Origin origin, List<Rule> rules)
    {
        foreach (ICssRule rule in source)
        {
            if (rule is ICssStyleRule { Selector: { } selector } style)
            {
                rules.Add(new Rule(selector, Declared(style.Style, isImportant: false), Declared(style.Style, isImportant: true), origin, rules.Count));
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

    private sealed record Inline((string Name, string Value)[] Normal, (string Name, string Value)[] Important);

    private sealed record Rule(ISelector Selector, (string Name, string Value)[] Normal, (string Name, string Value)[] Important, Origin Origin, int Order);
}
