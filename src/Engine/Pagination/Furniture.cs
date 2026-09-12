using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Snail.Toolkit.HtmlComposer.Engine.Layout;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Pagination;

/// <summary>Sets the running header, footer and side note into the margins of every page of a document.</summary>
/// <remarks>
/// Furniture is set after the document is paginated, because a running line says which page of how many this
/// is, and neither number exists until the last page has been made up. The markup is the same on every page
/// and the numbers in it are not, so each page is set on its own; the markup is parsed once, though, and only
/// the numbers are written into it again, because parsing it afresh for every page cost as much as the pages
/// themselves.
/// </remarks>
internal static class Furniture
{
    private const string PageToken = "{page}";

    private const string PagesToken = "{pages}";

    public static IReadOnlyList<Page> Around(
        IReadOnlyList<Page> pages,
        PageSetup setup,
        int total,
        Measurements measurements,
        IImageSource? images,
        List<Note> notes,
        CancellationToken ct)
    {
        if (setup is { Header: null, Footer: null, Side: null })
        {
            return pages;
        }

        float content = setup.Width - setup.Margins.Horizontal;
        float column = setup.Height - setup.Margins.Vertical;
        var header = new Rect(setup.Margins.Left, 0, content, setup.Margins.Top);
        var footer = new Rect(setup.Margins.Left, setup.Height - setup.Margins.Bottom, content, setup.Margins.Bottom);
        Rect side = setup.SideAt == PageSide.Right
            ? new Rect(setup.Width - setup.Margins.Right, setup.Margins.Top, setup.Margins.Right, column)
            : new Rect(0, setup.Margins.Top, setup.Margins.Left, column);

        Band?[] bands = [Band.Of(setup.Header, header), Band.Of(setup.Footer, footer), Band.Of(setup.Side, side)];

        return [.. pages.Select(page => Framed(page, bands, total, measurements, images, notes, ct))];
    }

    private static Page Framed(
        Page page,
        IReadOnlyList<Band?> bands,
        int total,
        Measurements measurements,
        IImageSource? images,
        List<Note> notes,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var sheet = new Sheet();

        foreach (Band? band in bands)
        {
            band?.Set(sheet, page.Number, total, measurements, images, notes, ct);
        }

        return page with
        {
            Boxes = [.. page.Boxes, .. sheet.Boxes],
            Images = [.. page.Images, .. sheet.Images],
            Spans = [.. page.Spans, .. sheet.Spans],
        };
    }

    private static string Numbered(string markup, int number, int total) =>
        markup
            .Replace(PageToken, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(PagesToken, total.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>One running line: its markup parsed once, its cascade decided once, and the places its numbers go.</summary>
    /// <remarks>
    /// The numbers are written into the text nodes and the attribute values that hold a token, page by page,
    /// and the same document is set again. The elements do not change, so the cascade's decisions stay
    /// good; only the widths do, which the galley measures afresh each time.
    /// </remarks>
    private sealed class Band
    {
        private readonly IHtmlDocument _document;

        private readonly Cascade _cascade;

        private readonly Rect _area;

        private readonly List<(IText Node, string Text)> _texts = [];

        private readonly List<(IElement Element, string Name, string Value)> _attributes = [];

        private Band(IHtmlDocument document, Rect area)
        {
            _document = document;
            _cascade = Cascade.Of(document);
            _area = area;

            Collect(document);
        }

        public static Band? Of(string? markup, Rect area) =>
            markup is null ? null : new Band(new HtmlParser().ParseDocument(markup), area);

        public void Set(Sheet sheet, int number, int total, Measurements measurements, IImageSource? images, List<Note> notes, CancellationToken ct)
        {
            foreach ((IText node, string text) in _texts)
            {
                node.Data = Numbered(text, number, total);
            }

            foreach ((IElement element, string name, string value) in _attributes)
            {
                element.SetAttribute(name, Numbered(value, number, total));
            }

            BlockFlow flow = Galley.Set(_document, _area, measurements, images, notes, ct, _cascade);

            sheet.Place(flow, _area.Y + flow.MarginTop);
        }

        private void Collect(INode node)
        {
            foreach (INode child in node.ChildNodes)
            {
                if (child is IText text && HasToken(text.Data))
                {
                    _texts.Add((text, text.Data));
                }

                if (child is IElement element)
                {
                    _attributes.AddRange(element.Attributes.Where(attribute => HasToken(attribute.Value)).Select(attribute => (element, attribute.Name, attribute.Value)));
                    Collect(element);
                }
            }
        }

        private static bool HasToken(string text) =>
            text.Contains(PageToken, StringComparison.Ordinal) || text.Contains(PagesToken, StringComparison.Ordinal);
    }
}
