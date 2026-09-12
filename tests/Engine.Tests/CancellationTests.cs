namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

public sealed class CancellationTests
{
    /// <summary>A token cancelled before the document is set stops it before the first page, as a host that gave up expects.</summary>
    [Fact]
    public void Compose_WithACancelledToken_StopsBeforeSettingAnything()
    {
        var typesetter = new Typesetter(new FixedPitch());

        Assert.Throws<OperationCanceledException>(() => typesetter.Compose("<p>x</p>", Typeset.Sheet, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Compose_WithATokenNobodyCancels_SetsTheDocument()
    {
        using var cancellation = new CancellationTokenSource();

        Composition composition = new Typesetter(new FixedPitch()).Compose("<p>x</p>", Typeset.Sheet, cancellation.Token);

        Assert.Single(composition.Pages);
    }

    /// <summary>
    /// A table is one element, so the check made between elements does not come round again until the whole
    /// table is set: a table of hundreds of rows went on being measured long after the host had given up.
    /// </summary>
    /// <remarks>
    /// The bound is deliberately generous. What it holds to is that the work stops within a handful of cells,
    /// not that it stops at an exact one; measured here, cancelling after five measurements left fewer than
    /// ten to come, against the three hundred rows the table still had.
    /// </remarks>
    [Fact]
    public void Compose_ATableTheHostGivesUpOn_StopsWithoutMeasuringTheRestOfIt()
    {
        using var cancellation = new CancellationTokenSource();
        var measurer = new Impatient(cancellation, after: 5);
        string rows = string.Concat(Enumerable.Range(0, 300).Select(number => $"<tr><td>cell{number}</td></tr>"));

        Assert.Throws<OperationCanceledException>(
            () => new Typesetter(measurer).Compose($"<table>{rows}</table>", Typeset.Sheet, cancellation.Token));

        Assert.InRange(measurer.MeasuredAfterGivingUp, 0, 40);
    }

    /// <summary>A paragraph is one element too, and its lines are broken in a loop of its own.</summary>
    [Fact]
    public void Compose_ALongParagraphTheHostGivesUpOn_StopsWithoutSettingTheRestOfIt()
    {
        using var cancellation = new CancellationTokenSource();
        var measurer = new Impatient(cancellation, after: 5);

        Assert.Throws<OperationCanceledException>(
            () => new Typesetter(measurer).Compose($"<p>{Typeset.Numbered(500)}</p>", Typeset.Sheet, cancellation.Token));

        Assert.InRange(measurer.MeasuredAfterGivingUp, 0, 40);
    }

    /// <summary>Measures as <see cref="FixedPitch"/> does, and gives up on the document part way through it.</summary>
    private sealed class Impatient(CancellationTokenSource cancellation, int after) : ITextMeasurer
    {
        private int _measured;

        /// <summary>How much more was measured after the host asked for the document to stop.</summary>
        public int MeasuredAfterGivingUp { get; private set; }

        public float Measure(string text, Font font)
        {
            _measured++;

            if (_measured == after)
            {
                cancellation.Cancel();
            }

            if (_measured > after)
            {
                MeasuredAfterGivingUp++;
            }

            return text.Length * ((font.Size / 2) + font.Tracking);
        }

        public FontMetrics MetricsOf(Font font) => new(font.Size * 0.8f, font.Size * 0.2f, 0);
    }
}
