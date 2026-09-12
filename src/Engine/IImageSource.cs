namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>Reads the picture an <c>img</c> element points at.</summary>
/// <remarks>
/// Implemented by the renderer, which owns a decoder: the engine needs the natural size to reserve room but
/// never the pixels. Answering <see langword="null"/> is an ordinary outcome: the picture is left out and the
/// composition carries a note naming the source that could not be read.
/// </remarks>
public interface IImageSource
{
    /// <summary>The picture behind a <c>src</c> attribute, or <see langword="null"/> when it cannot be read.</summary>
    Image? Load(string source);
}
