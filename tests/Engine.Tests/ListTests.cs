namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class ListTests
{
    [Fact]
    public void OrderedList_NumbersItsItemsInOrder()
    {
        IEnumerable<string> texts = Typeset.Html("<ol style='margin:0'><li>a</li><li>b</li></ol>").Pages[0].Spans.Select(span => span.Text);

        string[] expected = ["1.", "a", "2.", "b"];
        Assert.Equal(expected, texts);
    }

    /// <summary>The list's 40px padding is 30pt; the bullet, a third of an em across, ends one 6pt space before the text.</summary>
    [Fact]
    public void Bullet_HangsInTheListPaddingLeftOfTheText()
    {
        Page page = Typeset.Html("<ul style='margin:0'><li>item</li></ul>").Pages[0];
        RenderBox bullet = page.Boxes.Single(box => box.Kind == BoxKind.Marker);

        Assert.Equal(40, Assert.Single(page.Spans).X, 0.01);
        Assert.Equal(34, bullet.Bounds.Right, 0.01);
        Assert.Equal(4, bullet.Bounds.Width, 0.01);
    }

    /// <summary>Bullets are drawn rather than typed: many text faces have no glyph for them and would print a crossed box.</summary>
    [Fact]
    public void Bullets_OfEachNestingLevel_AreDiscCircleAndSquareShapes()
    {
        Page page = Typeset.Html("<ul><li>disc<ul><li>circle<ul><li>square</li></ul></li></ul></li></ul>").Pages[0];
        var bullets = page.Boxes.Where(box => box.Kind == BoxKind.Marker).ToList();

        string[] texts = ["disc", "circle", "square"];
        Assert.Equal(texts, page.Spans.Select(span => span.Text));
        Assert.Equal(3, bullets.Count);
        Assert.True(bullets[0] is { Radius: > 0, Background: not null });
        Assert.True(bullets[1] is { Radius: > 0, Background: null, Border.Top.IsVisible: true });
        Assert.True(bullets[2] is { Radius: 0, Background: not null });
    }

    [Fact]
    public void OrderedList_WithAStartAttribute_CountsFromIt()
    {
        IEnumerable<string> texts = Typeset.Html("<ol start='5'><li>a</li></ol>").Pages[0].Spans.Select(span => span.Text);

        Assert.Contains("5.", texts);
    }

    [Fact]
    public void UpperRomanList_CountsInRomanNumerals()
    {
        IEnumerable<string> texts = Typeset.Html("<ol style='list-style-type:upper-roman'><li>a</li><li>b</li><li>c</li><li>d</li></ol>")
            .Pages[0].Spans.Select(span => span.Text);

        Assert.Contains("IV.", texts);
    }

    [Fact]
    public void ListWithoutMarkers_ShowsOnlyItsText()
    {
        Page page = Typeset.Html("<ul style='list-style-type:none'><li>a</li></ul>").Pages[0];

        Assert.Equal("a", Assert.Single(page.Spans).Text);
        Assert.DoesNotContain(page.Boxes, box => box.Kind == BoxKind.Marker);
    }

    /// <summary>The nested list is laid out after the item's own text, and must not take the item's marker with it.</summary>
    [Fact]
    public void ListItem_WithANestedList_KeepsItsOwnMarker()
    {
        Page page = Typeset.Html("<ul><li>outer<ul><li>inner</li></ul></li></ul>").Pages[0];
        var bullets = page.Boxes.Where(box => box.Kind == BoxKind.Marker).ToList();
        float outer = page.Spans.Single(span => span.Text == "outer").Baseline;

        Assert.Equal(2, bullets.Count);
        Assert.InRange(bullets[0].Bounds.Bottom, outer - 12, outer);
    }

    [Fact]
    public void NestedList_FollowsItsItemWithoutAGap()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html("<ul><li>outer<ul><li>inner</li></ul></li></ul>").Pages[0].Spans;

        float outer = spans.Single(span => span.Text == "outer").Baseline;
        float inner = spans.Single(span => span.Text == "inner").Baseline;

        Assert.Equal(12, inner - outer, 0.01);
    }

    /// <summary>The marker belongs to the item, and an item that sets no line has nowhere to hang it.</summary>
    /// <remarks>
    /// The marker used to wait in the galley for the next line set anywhere, so an empty item put its bullet
    /// on the paragraph after the list.
    /// </remarks>
    [Fact]
    public void EmptyListItem_LeavesNoMarkerForWhatFollowsTheList()
    {
        Page page = Typeset.Html("<ul><li></li></ul><p>after</p>").Pages[0];

        Assert.DoesNotContain(page.Boxes, box => box.Kind == BoxKind.Marker);
    }

    /// <summary>The first line of an item is where its marker hangs, however deep inside the item that line is.</summary>
    [Fact]
    public void ListItem_WhoseTextIsInANestedParagraph_HangsTheMarkerOnThatLine()
    {
        Page page = Typeset.Html("<ul><li><div></div><p style='margin:0'>text</p></li></ul>").Pages[0];
        RenderBox bullet = Assert.Single(page.Boxes, box => box.Kind == BoxKind.Marker);
        float baseline = Assert.Single(page.Spans).Baseline;

        Assert.InRange(bullet.Bounds.Bottom, baseline - 12, baseline);
    }
}
