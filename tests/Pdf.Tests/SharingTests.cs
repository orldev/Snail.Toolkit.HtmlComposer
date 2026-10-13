using System.Collections.Concurrent;
using System.Text;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf.Tests;

/// <summary>
/// The README and <see cref="Converter{TOptions}"/> both promise that one converter, once its faces are
/// loaded, may be shared between threads. Nothing held them to that promise until now.
/// </summary>
public sealed class SharingTests : IDisposable
{
    private const string Html = "<p style=\"font-family:'PT Sans'\">Sharing one converter between threads</p>";

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public void Dispose() => _fonts.Dispose();

    /// <summary>
    /// A race in measuring would not announce itself. Every conversion makes its own faces, but they are made
    /// over typefaces the whole library shares, and a width taken wrongly on one thread breaks that thread's
    /// lines somewhere else instead of throwing.
    /// </summary>
    [Fact]
    public void Typeset_OnManyThreadsAtOnce_MeasuresEveryDocumentAlike()
    {
        var converter = new HtmlToPdf(_fonts);
        float[] alone = Widths(converter.Typeset(Html));
        var together = new ConcurrentBag<float[]>();

        Parallel.For(0, 64, _ => together.Add(Widths(converter.Typeset(Html))));

        Assert.Equal(64, together.Count);
        Assert.All(together, widths => Assert.Equal(alone, widths));
    }

    [Fact]
    public void Convert_OnManyThreadsAtOnce_WritesAWholeDocumentEveryTime()
    {
        var converter = new HtmlToPdf(_fonts);
        var together = new ConcurrentBag<byte[]>();

        Parallel.For(0, 32, _ => together.Add(converter.Convert(Html)));

        Assert.Equal(32, together.Count);
        Assert.All(together, document => Assert.Equal("%PDF", Encoding.ASCII.GetString(document, 0, 4)));
    }

    /// <summary>One library serves every converter, so the two of them are shared as readily as one is.</summary>
    [Fact]
    public void TwoConverters_OverOneLibraryAtOnce_BothWriteTheirOwnDocument()
    {
        var pdf = new HtmlToPdf(_fonts);
        var also = new HtmlToPdf(_fonts, new PdfOptions { Title = "Second" });
        var together = new ConcurrentBag<byte[]>();

        Parallel.For(0, 32, number => together.Add(number % 2 == 0 ? pdf.Convert(Html) : also.Convert(Html)));

        Assert.Equal(32, together.Count);
        Assert.All(together, document => Assert.Equal("%PDF", Encoding.ASCII.GetString(document, 0, 4)));
    }

    /// <summary>
    /// Compositions borrow their parsers from a pool, and a parser is not safe on two threads: setting a nested
    /// rule registers a selector in the parser's own context and removes it again. A pair shared by two
    /// compositions at once threw for nearly every one of them, so this sets nested rules on many threads.
    /// </summary>
    [Fact]
    public void Typeset_NestedRulesOnManyThreadsAtOnce_SetsEveryDocumentAlike()
    {
        const string nested = "<style>p { font-family: 'PT Sans'; margin: 0; & b { font-size: 14pt } & + p { margin-top: 6pt } }</style>"
            + "<p>Nested <b>rules</b> set</p><p>on many threads</p>";
        var converter = new HtmlToPdf(_fonts);
        float[] alone = Widths(converter.Typeset(nested));
        var together = new ConcurrentBag<float[]>();

        Parallel.For(0, 64, _ => together.Add(Widths(converter.Typeset(nested))));

        Assert.Equal(64, together.Count);
        Assert.All(together, widths => Assert.Equal(alone, widths));
    }

    private static float[] Widths(Composition composition) =>
        [.. composition.Pages.SelectMany(page => page.Spans).Select(span => span.Width)];
}
