using AngleSharp.Css.Parser;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Snail.Toolkit.HtmlComposer.Engine.Layout;
using Snail.Toolkit.HtmlComposer.Engine.Pagination;
using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Sets HTML and CSS into pages: parses the markup, lays it out and cuts it into pages.</summary>
/// <remarks>
/// Knows no output format. Text is measured by the <see cref="ITextMeasurer"/> the renderer supplies and
/// pictures are read by its <see cref="IImageSource"/>, so the pages that come back hold nothing but
/// positioned boxes, spans and pictures, ready for any renderer to draw. A typesetter keeps no state between
/// documents and can be shared between threads whenever its measurer and image source can.
/// <para>
/// Cancellation is looked at once per element laid out and once per page made up, so a document that a host
/// gives up on stops within a line or a page of being asked to.
/// </para>
/// </remarks>
public sealed class Typesetter(ITextMeasurer measurer, IImageSource? images = null)
{
    /// <summary>Sets a document into pages of the given size and margins.</summary>
    /// <exception cref="ArgumentException">The margins leave no room for content on the page.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the document was set.</exception>
    public Composition Compose(string html, PageSetup setup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(setup);
        MustFit(setup, nameof(setup));

        return Compose([new Section(html, setup)], ct);
    }

    /// <summary>Sets a document of several sections, each on its own sheet, into one run of pages.</summary>
    /// <remarks>
    /// Numbering runs through the whole document rather than restarting with each section, and the running
    /// lines are set last, when the number of pages of every section is known.
    /// </remarks>
    /// <exception cref="ArgumentException">There are no sections, or a section's margins leave no room.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the document was set.</exception>
    public Composition Compose(IReadOnlyList<Section> sections, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sections);

        if (sections.Count == 0)
        {
            throw new ArgumentException("A document needs at least one section.", nameof(sections));
        }

        Parsers parsers = Parsers.Rent();
        Composition composition = Compose(sections, parsers.Html, parsers.Css, ct);

        parsers.Return();

        return composition;
    }

    private static void MustFit(PageSetup setup, string parameter)
    {
        if (setup.Content is not { Width: > 0, Height: > 0 })
        {
            throw new ArgumentException("The margins leave no room for content on the page.", parameter);
        }
    }

    /// <summary>Sets the sections with parsers no other composition is using, as a composition is set on one thread.</summary>
    private Composition Compose(IReadOnlyList<Section> sections, HtmlParser html, CssParser css, CancellationToken ct)
    {
        var measurements = new Measurements(measurer);
        var pictures = new Pictures(images);
        List<Note> notes = [];
        List<IReadOnlyList<Page>> runs = [];
        int first = 1;

        foreach (Section section in sections)
        {
            MustFit(section.Setup, nameof(sections));

            IHtmlDocument document = html.ParseDocument(section.Html);
            BlockFlow galley = Galley.Set(document, Cascade.Of(document, css), section.Setup.Content, measurements, pictures, notes, ct);
            IReadOnlyList<Page> run = Paginator.Paginate(galley, section.Setup, first, ct);

            runs.Add(run);
            first += run.Count;
        }

        List<Page> pages = [];

        foreach ((IReadOnlyList<Page>? run, Section? section) in runs.Zip(sections))
        {
            pages.AddRange(Furniture.Around(run, section.Setup, first - 1, measurements, pictures, notes, html, css, ct));
        }

        return new Composition(pages, [.. notes.Distinct()]);
    }
}
