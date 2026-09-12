namespace Snail.Toolkit.HtmlComposer.Engine.Tests;

/// <summary>Answers a fixed set of sources with pictures of known pixel sizes, and every other source with nothing.</summary>
internal sealed class Pictures(params (string Source, int Width, int Height)[] known) : IImageSource
{
    public Image? Load(string source) =>
        known.Where(picture => picture.Source.Equals(source, StringComparison.Ordinal))
            .Select(picture => new Image([], picture.Width, picture.Height))
            .FirstOrDefault();
}
