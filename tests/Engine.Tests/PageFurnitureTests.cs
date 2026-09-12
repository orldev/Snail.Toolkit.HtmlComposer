namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class PageFurnitureTests
{
    private const string Header = "<div style='margin:0'>header</div>";

    private const string Footer = "<div style='margin:0'>footer</div>";

    private const string Note = "<div style='margin:0'>aside</div>";

    private const string Paragraph = "<p style='margin:0'>x</p>";

    [Fact]
    public void Background_PaintsTheWholeSheetBeneathTheContent()
    {
        var setup = new PageSetup(200, 200, Edges.All(10)) { Background = new Rgba(213, 35, 33) };

        RenderBox cover = Compose("<p style='margin:0'>x</p>", setup).Pages[0].Boxes[0];

        Assert.Equal(BoxKind.Sheet, cover.Kind);
        Assert.Equal(0, cover.Bounds.X, 0.01);
        Assert.Equal(0, cover.Bounds.Y, 0.01);
        Assert.Equal(200, cover.Bounds.Width, 0.01);
        Assert.Equal(200, cover.Bounds.Height, 0.01);
        Assert.Equal(new Rgba(213, 35, 33), cover.Background);
    }

    [Fact]
    public void SheetWithoutABackground_PaintsNoCover()
    {
        Page page = Compose("<p style='margin:0'>x</p>", new PageSetup(200, 200, Edges.All(10))).Pages[0];

        Assert.DoesNotContain(page.Boxes, box => box.Kind == BoxKind.Sheet);
    }

    /// <summary>The band is the top margin, so a 12pt line sits on a baseline 9.6 points down, above the content.</summary>
    [Fact]
    public void Header_IsSetInTheTopMarginOfEveryPage()
    {
        Composition composition = Compose($"<p style='margin:0'>{Typeset.Numbered(40)}</p>", Framed(Header, footer: null));

        Assert.True(composition.Pages.Count > 1);
        Assert.All(composition.Pages, page => Assert.Contains(page.Spans, span => span.Text == "header" && span.Baseline < 20));
    }

    [Fact]
    public void Footer_IsSetInTheBottomMarginOfEveryPage()
    {
        Composition composition = Compose($"<p style='margin:0'>{Typeset.Numbered(40)}</p>", Framed(header: null, Footer));

        Assert.All(composition.Pages, page => Assert.Contains(page.Spans, span => span.Text == "footer" && span.Baseline > 180));
    }

    [Fact]
    public void PageTokens_AreReplacedWithTheNumberOfThePageAndOfTheDocument()
    {
        string running = "<div style='margin:0'>{page}/{pages}</div>";

        IReadOnlyList<Page> pages = Compose($"<p style='margin:0'>{Typeset.Numbered(40)}</p>", Framed(header: null, running)).Pages;
        int total = pages.Count;

        Assert.Contains(pages[0].Spans, span => span.Text == $"1/{total}");
        Assert.Contains(pages[^1].Spans, span => span.Text == $"{total}/{total}");
    }

    /// <summary>Furniture is set into the margins, not into the flow: the content starts where it always did.</summary>
    [Fact]
    public void Furniture_LeavesTheContentWhereItWasSet()
    {
        const string html = "<p style='margin:0'>x</p>";

        Page bare = Compose(html, Framed(header: null, footer: null)).Pages[0];
        Page framed = Compose(html, Framed(Header, Footer)).Pages[0];

        Assert.Equal(29.6, Assert.Single(bare.Spans).Baseline, 0.01);
        Assert.Equal(29.6, Assert.Single(framed.Spans, span => span.Text == "x").Baseline, 0.01);
    }

    /// <summary>The left margin is 60 wide, so the note stands at the paper edge and the content starts at 60.</summary>
    [Fact]
    public void Side_IsSetInTheMarginBesideTheContentOfEveryPage()
    {
        var setup = new PageSetup(200, 200, new Edges(20, 20, 20, 60)) { Side = Note };

        Composition composition = Compose($"<p style='margin:0'>{Typeset.Numbered(40)}</p>", setup);

        Assert.True(composition.Pages.Count > 1);
        Assert.All(
            composition.Pages,
            page => Assert.Contains(page.Spans, span => span.Text == "aside" && span.X == 0 && span.Baseline is > 29.5f and < 29.7f));
    }

    /// <summary>Told to stand on the right, the note starts where the right margin begins.</summary>
    [Fact]
    public void SideOnTheRight_IsSetInTheRightMargin()
    {
        var setup = new PageSetup(200, 200, new Edges(20, 60, 20, 20)) { Side = Note, SideAt = PageSide.Right };

        Page page = Compose("<p style='margin:0'>x</p>", setup).Pages[0];

        Assert.Equal(140, Assert.Single(page.Spans, span => span.Text == "aside").X, 0.01);
    }

    /// <summary>The note is furniture, not a column: the content is set where it would be without it.</summary>
    [Fact]
    public void Side_LeavesTheContentWhereItWasSet()
    {
        var bare = new PageSetup(200, 200, new Edges(20, 20, 20, 60));

        TextSpan without = Assert.Single(Compose(Paragraph, bare).Pages[0].Spans);
        TextSpan beside = Assert.Single(Compose(Paragraph, bare with { Side = Note }).Pages[0].Spans, span => span.Text == "x");

        Assert.Equal(60, without.X, 0.01);
        Assert.Equal(without.X, beside.X, 0.01);
        Assert.Equal(without.Baseline, beside.Baseline, 0.01);
    }

    private static PageSetup Framed(string? header, string? footer) =>
        new(200, 200, Edges.All(20)) { Header = header, Footer = footer };

    private static Composition Compose(string html, PageSetup setup) =>
        new Typesetter(new FixedPitch()).Compose(html, setup);
}
