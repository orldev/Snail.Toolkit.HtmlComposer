namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>What a box on a page stands for.</summary>
/// <remarks>
/// A renderer draws every kind the same way — a rectangle with a background and a border — and needs the kind
/// only to know what it is looking at. Until now the kind travelled inside the element name as a string no
/// element could have, which every reader of a page had to know by heart.
/// </remarks>
public enum BoxKind
{
    /// <summary>The box of an element — a block, a cell, a row — whose tag name is in <see cref="RenderBox.Element"/>.</summary>
    Element,

    /// <summary>The sheet itself, painted edge to edge beneath everything set on it.</summary>
    Sheet,

    /// <summary>The bullet of a list item, drawn as a shape rather than set as a glyph.</summary>
    Marker,

    /// <summary>The background and border of one run of inline content.</summary>
    Inline,
}
