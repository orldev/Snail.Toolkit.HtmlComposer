namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>The face and size a run of text is set in.</summary>
/// <param name="Family">The CSS <c>font-family</c> list, most wanted first.</param>
/// <param name="Size">The em size in points.</param>
/// <param name="Weight">The CSS weight, 400 for regular and 700 for bold.</param>
/// <param name="IsItalic">Whether an italic or oblique face was asked for.</param>
/// <param name="Tracking">Space added after every glyph, in points; negative tightens the text.</param>
/// <remarks>
/// The family stays the list the stylesheet wrote rather than one resolved name, because only the renderer
/// knows which faces it can load. Resolving it here would let measurement and drawing settle on different
/// faces, and every line break would land in the wrong place.
/// <para>
/// Tracking travels with the font for the same reason: a width is never measured without it and never drawn
/// without it, and the two cannot drift apart.
/// </para>
/// </remarks>
public sealed record Font(string Family, float Size, int Weight = 400, bool IsItalic = false, float Tracking = 0)
{
    /// <summary>Whether a bold face was asked for.</summary>
    /// <remarks>600 is where CSS font matching starts preferring heavier faces over lighter ones.</remarks>
    public bool IsBold => Weight >= 600;

    /// <summary>The family names in order of preference, without quotes.</summary>
    public IReadOnlyList<string> Families =>
    [
        .. Family.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.Trim('"', '\''))
            .Where(name => name.Length > 0)
    ];
}
