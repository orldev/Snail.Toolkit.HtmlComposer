using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Pictures the application already holds, kept under the source the markup names them with.</summary>
/// <remarks>
/// For pictures that live in storage or behind a service. The application fetches them first — on its own
/// terms, asynchronously, with its own timeouts and allow-lists — and hands the bytes over under the very
/// string its templates write in <c>src</c>, so one template serves the screen and the document alike. A
/// source not held here is read as a <c>data:</c> URI, or as a file when a base directory is given, by
/// <see cref="LocalPictures"/>. Nothing is fetched from anywhere.
/// <para>
/// Filled before it is used, and only read from then on, exactly as <see cref="FontLibrary"/> is: the
/// pictures are held in a plain dictionary, so holding one more while another thread is setting a document
/// is not safe. That makes this the source for a converter built for one document. An application serving
/// many documents from a store of many pictures — where each document names only a few — is better served
/// by an <see cref="IImageSource"/> over the store, built once and shared, which is asked only for the
/// sources each document mentions.
/// </para>
/// </remarks>
public sealed class MemoryPictures(string? baseDirectory = null) : IImageSource
{
    private readonly Dictionary<string, byte[]> _held = new(StringComparer.Ordinal);

    private readonly LocalPictures _local = new(baseDirectory);

    /// <summary>Holds a picture under a source; the same source held again is replaced.</summary>
    public MemoryPictures Add(string source, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);

        _held[source] = content;

        return this;
    }

    /// <inheritdoc/>
    public Image? Load(string source) =>
        _held.TryGetValue(source, out byte[]? content) ? Decoding.Read(content) : _local.Load(source);
}
