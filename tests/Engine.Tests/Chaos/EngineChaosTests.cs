namespace Snail.Toolkit.HtmlComposer.Engine.Tests.Chaos;

/// <summary>Markup a user can write that the engine must survive: the README promises it is safe to feed user-authored HTML.</summary>
public sealed class EngineChaosTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A heading marked to resume a block is set again on every fragment, and a fragment that takes only that
    /// heading leaves a tail that is again heading plus the rest: the paginator's progress check compares
    /// references, a new tail is a new object, so the loop never ends and memory grows by gigabytes.
    /// </summary>
    [Fact]
    public void ResumedHeading_BeforeAForcedBreak_PaginatesInsteadOfLooping()
    {
        const string html = "<div><aside resumed>Continued</aside><p>A</p><p style='break-before:page'>B</p></div>";
        using var cancel = new CancellationTokenSource(Budget);

        Composition composition = new Typesetter(new FixedPitch()).Compose(html, Typeset.Sheet, cancel.Token);

        Assert.InRange(composition.Pages.Count, 1, 4);
    }

    /// <summary>The same loop reached three other ways: a block that will not break and is taller than a page, a resumed heading taller than a page, and both inside a table cell.</summary>
    [Theory]
    [InlineData("<div><aside resumed>Continued</aside><p>A</p><div style='height:400pt;break-inside:avoid'>tall</div></div>")]
    [InlineData("<div><aside resumed><div style='height:400pt'>tall heading</div></aside><p>A</p><p style='break-before:page'>B</p></div>")]
    [InlineData("<table><tr><td><aside resumed>Continued</aside><p>a</p><div style='height:300pt;break-inside:avoid'>tall</div></td></tr></table>")]
    public void ResumedHeading_InEveryWayOfReachingTheLoop_Paginates(string html)
    {
        using var cancel = new CancellationTokenSource(Budget);

        Composition composition = new Typesetter(new FixedPitch()).Compose(html, Typeset.Sheet, cancel.Token);

        Assert.InRange(composition.Pages.Count, 1, 12);
    }

    /// <summary>
    /// A roman marker is built at any length: a list starting at two billion writes two million «M» per item,
    /// 231 MB for twenty items. Browsers fall back to decimal outside 1..3999.
    /// </summary>
    [Fact]
    public void RomanMarker_FromAHugeStart_StaysShort()
    {
        const string html = "<ol start='2000000000' style='list-style-type:upper-roman'><li>a</li><li>b</li><li>c</li></ol>";

        Page page = Typeset.Html(html).Pages[0];

        Assert.All(page.Spans, span => Assert.True(span.Text.Length < 64, $"a marker of {span.Text.Length} characters"));
    }

    /// <summary>
    /// A length past float's range parses to infinity and multiplies into NaN: the coordinates every renderer
    /// draws with stop being numbers.
    /// </summary>
    [Theory]
    [InlineData("<p style='font-size:1e39px'>x</p>")]
    [InlineData("<p style='margin-top:1e39pt'>x</p>")]
    public void LengthBeyondFloat_GivesFiniteCoordinates(string html)
    {
        Page page = Typeset.Html(html).Pages[0];

        Assert.All(page.Spans, span => Assert.True(float.IsFinite(span.X) && float.IsFinite(span.Baseline), $"{span.Text} at {span.X}, {span.Baseline}"));
    }

    /// <summary>A negative padding or width is not a box a browser would draw; read as none, the text stays inside the page.</summary>
    [Theory]
    [InlineData("<p style='padding:-5pt;margin:0'>inside</p>")]
    [InlineData("<div style='width:-5pt'>inside</div>")]
    public void NegativeBoxSizes_KeepTheTextOnThePage(string html)
    {
        Page page = Typeset.Html(html).Pages[0];

        Assert.Equal(["inside"], [.. page.Lines]);
        Assert.All(page.Spans, span => Assert.InRange(span.X, Typeset.Sheet.Margins.Left, Typeset.Sheet.Width));
    }

    /// <summary>A picture told to cover a frame of no size divided nothing by nothing and handed NaN to the renderer as the part to show.</summary>
    [Fact]
    public void CoverIntoAnEmptyFrame_GivesNoNaNToTheRenderer()
    {
        var pictures = new Pictures(("x.png", 40, 20));

        Page page = Typeset.Html("<img src='x.png' style='width:0;height:0;object-fit:cover'>", pictures).Pages[0];

        Assert.All(page.Images, image => Assert.True(image.Source is null || float.IsFinite(image.Source.Value.Width), $"source {image.Source}"));
    }

    /// <summary>An infinite sheet passed the check that its margins leave room, and every coordinate came out NaN.</summary>
    [Fact]
    public void InfiniteSheet_IsRefusedAtTheDoor()
    {
        var setup = new PageSetup(float.PositiveInfinity, 200, Edges.All(10));

        Assert.Throws<ArgumentException>(() => new Typesetter(new FixedPitch()).Compose("x", setup));
    }

    /// <summary>Doubling the size at every level reaches infinity after about 130 levels, and NaN below it.</summary>
    [Fact]
    public void RelativeSizesCompounded_GiveFiniteCoordinates()
    {
        string html = string.Concat(Enumerable.Repeat("<span style='font-size:2em'>", 140)) + "x";

        Page page = Typeset.Html(html).Pages[0];

        Assert.All(page.Spans, span => Assert.True(float.IsFinite(span.Baseline) && float.IsFinite(span.Font.Size), $"size {span.Font.Size}"));
    }

    /// <summary>A browser drops a negative font size as invalid; the engine measures with it and sets text left of the margin.</summary>
    [Fact]
    public void NegativeFontSize_IsDroppedLikeABrowserDoes()
    {
        Page page = Typeset.Html("<p style='margin:0'>hello <span style='font-size:-5pt'>world</span> again</p>").Pages[0];

        Assert.All(page.Spans, span => Assert.True(span.X >= Typeset.Sheet.Margins.Left - 0.01f, $"«{span.Text}» at {span.X}"));
    }

    /// <summary>
    /// A shift is read off each run's own vertical-align, which does not inherit, so a footnote link inside
    /// <c>sup</c> — the commonest superscript there is — stays on the baseline.
    /// </summary>
    [Fact]
    public void SuperscriptAroundALink_RaisesTheLink()
    {
        Page page = Typeset.Html("<p>x<sup><a>1</a></sup></p>").Pages[0];

        TextSpan text = page.Spans.Single(span => span.Text == "x");
        TextSpan mark = page.Spans.Single(span => span.Text == "1");

        Assert.True(mark.Baseline < text.Baseline, $"the mark sits at {mark.Baseline}, the text at {text.Baseline}");
    }

    /// <summary>
    /// Capitalising every character after a non-letter, one text node at a time, turned «don't» into «Don'T», a
    /// word split by a tag into «HelLo», and a decomposed «cafés» into «CaféS».
    /// </summary>
    [Theory]
    [InlineData("don't stop", "Don't Stop")]
    [InlineData("<b>hel</b>lo world", "Hello World")]
    [InlineData("cafe\u0301s open", "Cafe\u0301s Open")]
    [InlineData("(quoted) co-op", "(Quoted) Co-Op")]
    public void Capitalize_RaisesTheFirstLetterOfEachWordOnly(string html, string expected)
    {
        Page page = Typeset.Html($"<p style='text-transform:capitalize'>{html}</p>").Pages[0];

        Assert.Equal(expected, Assert.Single(page.Lines));
    }

    /// <summary>
    /// Layout recursed once per element, and the stack ran out at about 2 000 nested blocks on a thread-pool
    /// thread — about 10 KB of markup. A stack overflow is not an exception: it ended the process, a web server
    /// with it. Below the cap the words are kept as plain text and a note says so.
    /// </summary>
    [Theory]
    [InlineData("<div>", 20_000)]
    [InlineData("<span>", 20_000)]
    [InlineData("<table><tr><td>", 3_000)]
    public async Task DeeplyNestedElements_AreSetWithTheirTextAndANote(string open, int depth)
    {
        string html = string.Concat(Enumerable.Repeat(open, depth)) + "deepest";

        Composition composition = await Task.Run(() => Typeset.Html(html));

        Assert.Contains(composition.Notes, note => note.Kind == NoteKind.NestingCapped);
        Assert.Contains(composition.Pages.SelectMany(page => page.Spans), span => span.Text.Contains("deepest", StringComparison.Ordinal));
    }

    /// <summary>A first line hung left by a negative indent was taken for a list marker and torn out of its paragraph.</summary>
    [Fact]
    public void HangingFirstLine_StaysInItsParagraph()
    {
        Page page = Typeset.Structured("<p style='text-indent:-24pt;padding-left:24pt;margin:0'>hanging words of a paragraph that wraps onto more lines</p>").Pages[0];

        TextFrame frame = Assert.Single(page.Texts);
        int framed = frame.Paragraphs.SelectMany(paragraph => paragraph.Lines).Sum(line => line.Spans.Count);

        Assert.Equal(page.Spans.Count, framed);
    }

    /// <summary>A continued foot and a plain one: the plain foot was taken for the continued one and the totals vanished.</summary>
    [Fact]
    public void ContinuedFootBesideAPlainOne_KeepsTheTotals()
    {
        const string html = "<table><tfoot continued><tr><td>CONT</td></tr></tfoot><tfoot><tr><td>FOOT</td></tr></tfoot><tbody><tr><td>row</td></tr></tbody></table>";

        Page page = Typeset.Html(html).Pages[0];

        Assert.Contains("FOOT", page.Lines);
        Assert.DoesNotContain("CONT", page.Lines);
    }

    /// <summary>
    /// The suspicion that a shared typesetter mixes documents between threads: one instance, many documents at
    /// once, every layout compared with the one set alone. Passing withdraws the finding.
    /// </summary>
    [Fact]
    public void SharedTypesetter_InParallel_SetsEveryDocumentAsAlone()
    {
        var typesetter = new Typesetter(new FixedPitch());
        string[] documents = [.. Enumerable.Range(1, 16).Select(number => $"<h1>Title {number}</h1><p>{Typeset.Numbered(number * 3)}</p>")];
        string[] alone = [.. documents.Select(html => Signature(typesetter.Compose(html, Typeset.Sheet)))];
        var mismatches = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 512, new ParallelOptions { MaxDegreeOfParallelism = 16 }, index =>
        {
            int document = index % documents.Length;

            if (Signature(typesetter.Compose(documents[document], Typeset.Sheet)) != alone[document])
            {
                mismatches.Add(document);
            }
        });

        Assert.Empty(mismatches);
    }

    private static string Signature(Composition composition) =>
        string.Join('|', composition.Pages.SelectMany(page => page.Spans).Select(span => $"{span.Text}@{span.X:F2},{span.Baseline:F2}"));
}
