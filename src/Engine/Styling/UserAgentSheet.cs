using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;

namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>The defaults every document starts from before its own stylesheets apply.</summary>
/// <remarks>
/// Follows the HTML rendering section of the WHATWG specification with one deliberate change: <c>body</c>
/// has no margin. Browsers keep 8px there to hold text off the window edge, which on paper is the page
/// margin's job, and keeping both would push every document inwards twice. Headings also avoid a break
/// after them and inside them, so a title never ends a page alone or splits in two.
/// </remarks>
internal static class UserAgentSheet
{
    private const string Text = """
        html, body, div, p, h1, h2, h3, h4, h5, h6, ul, ol, li, dl, dt, dd, blockquote, pre, figure, figcaption,
        section, article, aside, header, footer, main, nav, address, hr, form, fieldset, legend, details, summary,
        table, caption, thead, tbody, tfoot, tr, center { display: block }
        li { display: list-item }
        head, style, script, title, meta, link, base, template, noscript { display: none }
        [hidden] { display: none }
        h1 { font-size: 2em; margin: 0.67em 0 }
        h2 { font-size: 1.5em; margin: 0.83em 0 }
        h3 { font-size: 1.17em; margin: 1em 0 }
        h4 { margin: 1.33em 0 }
        h5 { font-size: 0.83em; margin: 1.67em 0 }
        h6 { font-size: 0.67em; margin: 2.33em 0 }
        h1, h2, h3, h4, h5, h6 { font-weight: bold; break-after: avoid; break-inside: avoid }
        p, ul, ol, dl, pre, blockquote, figure { margin: 1em 0 }
        blockquote, figure { margin-left: 40px; margin-right: 40px }
        dd { margin-left: 40px }
        ul, ol { padding-left: 40px }
        ul { list-style-type: disc }
        ol { list-style-type: decimal }
        ul ul, ol ul { list-style-type: circle }
        ul ul ul, ul ol ul, ol ul ul, ol ol ul { list-style-type: square }
        ul ul, ul ol, ol ul, ol ol { margin-top: 0; margin-bottom: 0 }
        b, strong, th { font-weight: bolder }
        i, em, cite, var, dfn, address { font-style: italic }
        u, ins { text-decoration: underline }
        s, strike, del { text-decoration: line-through }
        a { color: #0000ee; text-decoration: underline }
        pre, code, kbd, samp, tt { font-family: monospace }
        pre { white-space: pre }
        small, sub, sup { font-size: smaller }
        sub { vertical-align: sub }
        sup { vertical-align: super }
        big { font-size: larger }
        center, th { text-align: center }
        hr { border-top: 1px solid #808080; margin: 0.5em 0 }
        """;

    public static ICssStyleSheet Sheet { get; } = new CssParser().ParseStyleSheet(Text);
}
