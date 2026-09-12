
namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class LineBreakingTests
{
    private const string Words = "abcdefghi abcdefghi abcdefghi abcdefghi abcdefghi abcdefghi";

    [Fact]
    public void Paragraph_ShorterThanTheLine_StaysOnOneLineAtTheTopLeftOfTheContent()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0'>Hello world</p>").Pages[0].Spans);

        Assert.Equal("Hello world", span.Text);
        Assert.Equal(10, span.X, 0.01);
        Assert.Equal(19.6, span.Baseline, 0.01);
        Assert.Equal(66, span.Width, 0.01);
    }

    [Fact]
    public void Paragraph_LongerThanTheLine_WrapsAtTheLastSpaceThatFits()
    {
        IReadOnlyList<string> lines = Typeset.Html($"<p style='margin:0'>{Words}</p>").Pages[0].Lines;

        string[] expected = ["abcdefghi abcdefghi abcdefghi", "abcdefghi abcdefghi abcdefghi"];
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void Word_WiderThanTheLine_IsCutBetweenCharacters()
    {
        IReadOnlyList<string> lines = Typeset.Html($"<p style='margin:0'>{new string('x', 40)}</p>").Pages[0].Lines;

        string[] expected = [new string('x', 30), new string('x', 10)];
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void WhiteSpace_BetweenWords_CollapsesToOneSpace()
    {
        IReadOnlyList<string> lines = Typeset.Html("<p style='margin:0'>  one   \n\t  two  </p>").Pages[0].Lines;

        Assert.Equal("one two", Assert.Single(lines));
    }

    [Fact]
    public void NoBreakSpace_KeepsTwoWordsOnTheSameLine()
    {
        string joined = $"ab{(char)0xA0}cd";
        IReadOnlyList<string> lines = Typeset.Html($"<p style='margin:0'>{new string('x', 25)} ab&nbsp;cd</p>").Pages[0].Lines;

        Assert.Equal(joined, lines[1]);
    }

    [Fact]
    public void LineBreakElement_EndsTheLineEarly()
    {
        IReadOnlyList<string> lines = Typeset.Html("<p style='margin:0'>one<br>two</p>").Pages[0].Lines;

        string[] expected = ["one", "two"];
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void TwoLineBreaks_LeaveAnEmptyLineBetween()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html("<p style='margin:0'>one<br><br>two</p>").Pages[0].Spans;

        Assert.Equal(24, spans[1].Baseline - spans[0].Baseline, 0.01);
    }

    [Fact]
    public void CenteredText_SitsInTheMiddleOfTheLine()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;text-align:center'>abcd</p>").Pages[0].Spans);

        Assert.Equal(88, span.X, 0.01);
    }

    [Fact]
    public void RightAlignedText_EndsAtTheRightEdgeOfTheContent()
    {
        TextSpan span = Assert.Single(Typeset.Html("<p style='margin:0;text-align:right'>abcd</p>").Pages[0].Spans);

        Assert.Equal(190, span.X + span.Width, 0.01);
    }

    [Fact]
    public void JustifiedText_FillsEveryLineButTheLast()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html($"<p style='margin:0;text-align:justify'>{Words}</p>").Pages[0].Spans;
        var first = spans.Where(span => span.Baseline == spans[0].Baseline).ToList();
        var last = spans.Where(span => span.Baseline != spans[0].Baseline).ToList();

        Assert.Equal(3, first.Count);
        Assert.Equal(190, first[^1].X + first[^1].Width, 0.01);
        Assert.Equal("abcdefghi abcdefghi abcdefghi", Assert.Single(last).Text);
    }

    [Fact]
    public void TextIndent_ShiftsOnlyTheFirstLine()
    {
        IReadOnlyList<TextSpan> spans = Typeset.Html($"<p style='margin:0;text-indent:12pt'>{Words}</p>").Pages[0].Spans;

        Assert.Equal(22, spans[0].X, 0.01);
        Assert.Equal(10, spans[1].X, 0.01);
    }
}
