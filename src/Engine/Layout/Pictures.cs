namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Reads each picture once per composition, however many galleys ask for it.</summary>
/// <remarks>
/// A running head is set again on every page, each time as a galley of its own, and a cache per galley read a
/// logo in it once per page: forty pages handed the writer forty pictures with the same bytes. The PDF press
/// decodes and stores a picture once per object it is handed, so it stored the logo forty times. Kept for the
/// whole composition, the same object reaches every page and the press stores it once. A cell of a table is
/// measured before it is set, and a picture that cannot be read is still read once, not twice.
/// </remarks>
internal sealed class Pictures(IImageSource? source)
{
    private readonly Dictionary<string, Image?> _read = new(StringComparer.Ordinal);

    /// <summary>Whether the typesetter was given a source to read pictures from at all.</summary>
    public bool HasSource => source is not null;

    /// <summary>The picture behind a <c>src</c> attribute, or <see langword="null"/> when it cannot be read.</summary>
    public Image? Read(string name)
    {
        if (_read.TryGetValue(name, out Image? cached))
        {
            return cached;
        }

        Image? image = name.Length > 0 ? source?.Load(name) : null;
        _read[name] = image;

        return image;
    }
}
