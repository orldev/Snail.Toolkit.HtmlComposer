namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class SectionTests
{
    private static readonly PageSetup Cover =
        new(200, 200, Edges.All(0)) { Background = new Rgba(213, 35, 33) };

    private static readonly PageSetup Body =
        new(200, 200, Edges.All(20)) { Footer = "<div style='margin:0'>{page}/{pages}</div>" };

    /// <summary>A cover bleeds to the paper edge and has no running line; the pages after it have both.</summary>
    [Fact]
    public void EachSection_IsSetOnItsOwnSheet()
    {
        IReadOnlyList<Page> pages = Document().Pages;

        Assert.Equal(BoxKind.Sheet, pages[0].Boxes[0].Kind);
        Assert.DoesNotContain(pages[1].Boxes, box => box.Kind == BoxKind.Sheet);
        Assert.Equal(0, Assert.Single(pages[0].Spans).X, 0.01);
        Assert.Equal(20, pages[1].Spans[0].X, 0.01);
    }

    [Fact]
    public void PageNumbers_RunThroughTheWholeDocument()
    {
        IReadOnlyList<Page> pages = Document().Pages;

        Assert.True(pages.Count > 2);
        Assert.Equal([.. Enumerable.Range(1, pages.Count)], [.. pages.Select(page => page.Number)]);
    }

    /// <summary>The cover is a page of the document, so the pages it is counted in include it.</summary>
    [Fact]
    public void RunningLine_CountsEveryPageOfEverySection()
    {
        IReadOnlyList<Page> pages = Document().Pages;
        int total = pages.Count;

        Assert.Contains($"2/{total}", pages[1].Lines);
        Assert.Contains($"{total}/{total}", pages[^1].Lines);
    }

    [Fact]
    public void SectionWithoutARunningLine_CarriesNone()
    {
        Page cover = Document().Pages[0];

        Assert.Equal("cover", Assert.Single(cover.Spans).Text);
    }

    [Fact]
    public void DocumentOfNoSections_IsRefused()
    {
        var typesetter = new Typesetter(new FixedPitch());

        Assert.Throws<ArgumentException>(() => typesetter.Compose([]));
    }

    private static Composition Document() =>
        new Typesetter(new FixedPitch()).Compose(
        [
            new Section("<p style='margin:0'>cover</p>", Cover),
            new Section($"<p style='margin:0'>{Typeset.Numbered(30)}</p>", Body),
        ]);
}
