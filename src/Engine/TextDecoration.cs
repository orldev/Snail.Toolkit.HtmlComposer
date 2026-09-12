namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Lines drawn together with a run of text.</summary>
[Flags]
public enum TextDecoration
{
    /// <summary>No line.</summary>
    None = 0,

    /// <summary>A line under the text.</summary>
    Underline = 1,

    /// <summary>A line through the middle of the text.</summary>
    LineThrough = 2,
}
