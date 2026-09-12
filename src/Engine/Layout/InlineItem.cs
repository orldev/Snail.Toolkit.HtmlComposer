using Snail.Toolkit.HtmlComposer.Engine.Styling;

namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>A piece of inline content waiting to be broken into lines.</summary>
internal abstract record InlineItem(Style Style);

/// <summary>Text exactly as the document holds it, white space not yet collapsed.</summary>
internal sealed record TextRun(string Text, Style Style) : InlineItem(Style);

/// <summary>A <c>br</c> element: the line ends here whatever room is left.</summary>
internal sealed record LineBreak(Style Style) : InlineItem(Style);

/// <summary>Room an inline element's side margin keeps beside its content.</summary>
/// <remarks>
/// Nothing is drawn: the line simply advances. A margin written on a span is room in the line and not part of
/// any word, which is why it travels as an item of its own rather than widening the text beside it.
/// </remarks>
internal sealed record InlineSpace(float Width, Style Style) : InlineItem(Style);

/// <summary>A picture set in the line like one large glyph, with the room its side margins keep around it.</summary>
/// <remarks>
/// The space before and after comes from the left and right margins: it advances the line without widening
/// the picture, which is how text is held off an icon by a measured amount.
/// </remarks>
internal sealed record PictureRun(
    Image Image,
    float Width,
    float Height,
    Style Style,
    float SpaceBefore = 0,
    float SpaceAfter = 0,
    Rect? Source = null) : InlineItem(Style);

/// <summary>The bullet or number a list item hangs to the left of its first line; a bullet has no text and is drawn.</summary>
internal sealed record Marker(ListMarker Kind, string Text, Style Style);

/// <summary>The marker of the list item being set, until the first line of its content takes it.</summary>
/// <remarks>
/// Shared by the item's block and every block nested in it, because the marker hangs before the first line
/// of the item wherever that line is: in a paragraph, in a table cell, in a block within a block. Scoped to
/// the item rather than the galley, so a marker no line of the item took dies with the item instead of
/// reaching the first line set after it.
/// </remarks>
internal sealed class PendingMarker(Marker? marker)
{
    private Marker? _marker = marker;

    /// <summary>The marker, once; nothing from then on.</summary>
    public Marker? Take()
    {
        Marker? taken = _marker;
        _marker = null;

        return taken;
    }
}
