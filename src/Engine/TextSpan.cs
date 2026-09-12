namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A run of text in one font and colour, on one line of one page.</summary>
/// <param name="Text">The characters to draw.</param>
/// <param name="X">Left edge of the run.</param>
/// <param name="Baseline">Vertical position of the baseline.</param>
/// <param name="Width">The advance width the engine measured, which a renderer uses to draw decorations.</param>
/// <param name="Font">The font the run was measured with and has to be drawn with.</param>
/// <param name="Color">The text colour.</param>
/// <param name="Decoration">Lines drawn together with the text.</param>
public sealed record TextSpan(
    string Text,
    float X,
    float Baseline,
    float Width,
    Font Font,
    Rgba Color,
    TextDecoration Decoration = TextDecoration.None);
