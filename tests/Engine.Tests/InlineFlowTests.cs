
namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class InlineFlowTests
{
    /// <summary>«a» is six points wide, so eight points of margin start the span at 24.</summary>
    [Fact]
    public void MarginOnASpan_HoldsTheTextBesideItOff()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span style='margin-left:8pt'>b</span></p>").Pages[0];

        Assert.Equal([10, 24], [.. page.Spans.Select(span => MathF.Round(span.X, 2))]);
    }

    [Fact]
    public void MarginAfterASpan_HoldsTheTextThatFollowsOff()
    {
        Page page = Typeset.Html("<p style='margin:0'><span style='margin-right:8pt'>a</span>b</p>").Pages[0];

        Assert.Equal([10, 24], [.. page.Spans.Select(span => MathF.Round(span.X, 2))]);
    }

    /// <summary>A third of a 12pt em is 3.96 up, so the line grows to 15.96 and its baseline falls to 23.56.</summary>
    /// <remarks>
    /// The line has to be made taller by exactly what the run is raised by, or the raised text would be drawn
    /// above the room reserved for it and collide with the line above. One function answers both questions.
    /// </remarks>
    [Fact]
    public void SuperscriptRun_RisesAndTakesTheLineUpWithIt()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span style='vertical-align:super'>b</span></p>").Pages[0];

        Assert.Equal([23.56, 19.6], [.. page.Spans.Select(span => Math.Round(span.Baseline, 2))]);
    }

    /// <summary>A fifth of a 12pt em is 2.4 down, so the line keeps its ascent and grows below.</summary>
    [Fact]
    public void SubscriptRun_DropsAndTakesTheLineDownWithIt()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span style='vertical-align:sub'>b</span></p>").Pages[0];

        Assert.Equal([19.6, 22], [.. page.Spans.Select(span => Math.Round(span.Baseline, 2))]);
    }

    /// <summary>A span set like the text beside it is not merely on the same baseline: it is the same span.</summary>
    [Fact]
    public void RunWithoutAnAlignment_JoinsTheTextBesideIt()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span>b</span></p>").Pages[0];

        TextSpan span = Assert.Single(page.Spans);

        Assert.Equal("ab", span.Text);
        Assert.Equal(19.6, Math.Round(span.Baseline, 2));
    }

    /// <summary>The tags that mean raised and lowered say so themselves, as they do in a browser.</summary>
    /// <remarks>
    /// Told in distances rather than numbers on purpose: the user agent sheet also makes these tags
    /// <c>smaller</c>, and pinning the arithmetic here would tie the test to how that keyword resolves
    /// rather than to what is being claimed — that the tag alone moves the run off the line.
    /// </remarks>
    [Fact]
    public void SupAndSubTags_MoveTheirRunsWithoutAnInlineStyle()
    {
        Page page = Typeset.Html("<p style='margin:0'>m<sup>2</sup><sub>3</sub></p>").Pages[0];

        (TextSpan? ground, TextSpan? raised, TextSpan? dropped) = (page.Spans[0], page.Spans[1], page.Spans[2]);

        Assert.Equal(["m", "2", "3"], [.. page.Spans.Select(span => span.Text)]);
        Assert.True(raised.Baseline < ground.Baseline, $"raised {raised.Baseline} is not above {ground.Baseline}");
        Assert.True(dropped.Baseline > ground.Baseline, $"dropped {dropped.Baseline} is not below {ground.Baseline}");
    }

    /// <summary>The defect the baseline test would otherwise hide: a raised run must not be merged back down.</summary>
    [Fact]
    public void SuperscriptRunInTheSameFace_IsNotJoinedToTheTextBesideIt()
    {
        Page page = Typeset.Html("<p style='margin:0'>a<span style='vertical-align:super'>b</span></p>").Pages[0];

        Assert.Equal(["a", "b"], [.. page.Spans.Select(span => span.Text)]);
    }
}
