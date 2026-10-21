namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>Sets markup on a small sheet with fixed-pitch measurements, so every position in a test is plain arithmetic.</summary>
/// <remarks>
/// The sheet is 200 points square with 10 point margins: 180 points of content, which holds fifteen lines of
/// 12pt text of thirty characters each.
/// </remarks>
internal static class Typeset
{
    public static PageSetup Sheet { get; } = new(200, 200, Edges.All(10));

    public static Composition Html(string html, IImageSource? images = null) =>
        new Typesetter(new FixedPitch(), images).Compose(html, Sheet);

    /// <summary>Sets markup with its text and tables kept as structure, as a deck asks for them.</summary>
    public static Composition Structured(string html, IImageSource? images = null) =>
        new Typesetter(new FixedPitch(), images) { IsStructured = true }.Compose(html, Sheet);

    public static string Numbered(int count, string prefix = "line") =>
        string.Join("<br>", Enumerable.Range(1, count).Select(number => $"{prefix}{number}"));

    extension(Page page)
    {
        public IReadOnlyList<string> Lines =>
        [
            .. page.Spans
                .GroupBy(span => MathF.Round(span.Baseline, 2))
                .OrderBy(line => line.Key)
                .Select(line => string.Concat(line.OrderBy(span => span.X).Select(span => span.Text))),
        ];
    }
}
