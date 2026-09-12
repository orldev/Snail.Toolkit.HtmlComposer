namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>The vertical metrics of a face at one size, in points, all of them positive.</summary>
/// <param name="Ascent">Distance from the baseline up to the top of the tallest glyphs.</param>
/// <param name="Descent">Distance from the baseline down to the bottom of the deepest glyphs.</param>
/// <param name="LineGap">Extra space the face asks for between consecutive lines.</param>
public readonly record struct FontMetrics(float Ascent, float Descent, float LineGap)
{
    /// <summary>The line height CSS calls <c>normal</c> for this face.</summary>
    public float NormalLineHeight => Ascent + Descent + LineGap;
}
