namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>An <c>aside</c> marked <c>resumed</c> opens every fragment of its block after the first.</summary>
public sealed class ResumedTests
{
    /// <summary>Hidden in the flow, so the page where the block begins is not told it is a continuation.</summary>
    [Fact]
    public void ResumedAside_IsNotSetWhereTheBlockBegins()
    {
        Composition composition = Typeset.Html(
            $"<div><aside resumed>продолжение</aside>{Typeset.Numbered(20)}</div>");

        Assert.True(composition.Pages.Count > 1);
        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "продолжение");
    }

    [Fact]
    public void ResumedAside_OpensEveryFragmentAfterTheFirst()
    {
        Composition composition = Typeset.Html(
            $"<div><aside resumed>продолжение</aside>{Typeset.Numbered(40)}</div>");

        Assert.True(composition.Pages.Count > 2);
        Assert.All(composition.Pages.Skip(1), page => Assert.Contains(page.Spans, span => span.Text == "продолжение"));
    }

    /// <summary>A block that is never cut has no continuation, so the heading is never set at all.</summary>
    [Fact]
    public void ResumedAside_IsNotSetAtAllWhenTheBlockIsNotCut()
    {
        Composition composition = Typeset.Html("<div><aside resumed>продолжение</aside>one</div>");

        Assert.Single(composition.Pages);
        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "продолжение");
    }

    /// <summary>
    /// What a note in a margin column needs: it is short beside something long, so it runs out on the first
    /// page and the continuation would otherwise carry an empty box where the reader needs a heading.
    /// </summary>
    [Fact]
    public void ResumedAside_SpeaksForAMarginColumnBesideALongerOne()
    {
        string marginal = "<td style='width:40pt'>спец<aside resumed>далее</aside></td>";
        string content = $"<td style='width:140pt'>{Typeset.Numbered(30)}</td>";

        Composition composition = Typeset.Html($"<table><tr>{marginal}{content}</tr></table>");

        Assert.True(composition.Pages.Count > 1);
        Assert.Contains(composition.Pages[0].Spans, span => span.Text == "спец");
        Assert.DoesNotContain(composition.Pages[0].Spans, span => span.Text == "далее");
        Assert.All(composition.Pages.Skip(1), page => Assert.Contains(page.Spans, span => span.Text == "далее"));
    }

    /// <summary>An author drafting the document can see what the heading holds by putting it back in the flow.</summary>
    [Fact]
    public void ResumedAside_CanBePutBackIntoTheFlowByTheAuthor()
    {
        Composition composition = Typeset.Html(
            "<div><aside resumed style='display:block'>продолжение</aside>one</div>");

        Assert.Contains(composition.Pages[0].Spans, span => span.Text == "продолжение");
    }
}
