namespace Snail.Toolkit.HtmlComposer.Engine.Styling;

/// <summary>A computed length: a fixed part in points plus a share of the containing block's width.</summary>
/// <param name="Points">The fixed part.</param>
/// <param name="Percent">The share of the containing block's width, in percent.</param>
/// <remarks>
/// Percentages of margins, padding and widths refer to a width that is only known during layout, so the
/// computed style keeps them unresolved and layout resolves them against the block it is filling.
/// </remarks>
internal readonly record struct Length(float Points, float Percent = 0)
{
    public float Resolve(float basis) => Points + (Percent * basis / 100f);
}
