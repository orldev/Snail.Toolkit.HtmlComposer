using System.Collections.Concurrent;
using AngleSharp.Css.Parser;
using AngleSharp.Html.Parser;

namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>An HTML and a CSS parser, lent to one composition at a time and kept for the next.</summary>
/// <remarks>
/// Each parser builds a browsing context of its own, with a fresh set of element, selector and event
/// factories: about 150 KB and 140 µs a pair, a tenth of what a two-page quotation allocates. One pair for the
/// whole process would save it every time, but AngleSharp's parsers are not safe on two threads at once. The
/// CSS parser sets a nested rule (<c>&amp; .x { }</c>) by registering a selector for <c>&amp;</c> in its
/// context's attribute selector factory, a plain dictionary, and removing it again afterwards; two threads
/// doing so at once throw "an item with the same key has already been added", and the key left behind breaks
/// the parser for good — measured: 8 threads sharing one parser, 22 826 of 24 000 sheets threw, none on one
/// thread. The HTML parser writes every named frame into its context's dictionary of children. So a pair is
/// lent to one composition, which uses it on one thread, and returned when that composition is set.
/// <para>
/// A composition that fails does not return its pair, so a parser stopped halfway never serves another
/// document. A pair is retired after <see cref="Uses"/> compositions, which bounds what its context keeps
/// between documents — the frames it was told about, the selectors it has cached — and no more pairs are
/// kept idle than there are processors to use them.
/// </para>
/// </remarks>
internal sealed class Parsers
{
    /// <summary>How many compositions one pair serves before it is let go.</summary>
    private const int Uses = 256;

    private static readonly ConcurrentQueue<Parsers> Idle = new();

    private static int _idleCount;

    private int _used;

    private Parsers()
    {
    }

    public HtmlParser Html { get; } = new();

    public CssParser Css { get; } = new();

    /// <summary>A pair no other composition is using.</summary>
    public static Parsers Rent()
    {
        if (Idle.TryDequeue(out Parsers? idle))
        {
            Interlocked.Decrement(ref _idleCount);

            return idle;
        }

        return new Parsers();
    }

    /// <summary>Gives the pair back once its composition is set, unless it has served its turn.</summary>
    public void Return()
    {
        _used++;

        if (_used >= Uses)
        {
            return;
        }

        if (Interlocked.Increment(ref _idleCount) > Environment.ProcessorCount)
        {
            Interlocked.Decrement(ref _idleCount);
            return;
        }

        Idle.Enqueue(this);
    }
}
