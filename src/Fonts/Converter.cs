using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Turns HTML into a document: lays the markup out into pages and hands them to a writer.</summary>
/// <remarks>
/// Every renderer converts the same way and differs only in what it writes at the end, so the conversion is
/// here once and a renderer supplies the writing. Measuring and writing one document always share the same
/// <see cref="Faces"/>, which is why layout and writing are offered together rather than left to be assembled
/// from a <see cref="Typesetter"/> and a writer that could be handed different fonts: a width taken from one
/// face and drawn with another puts every line break in the wrong place.
/// <para>
/// Shareable between threads once the library is loaded: each conversion makes its own faces and keeps
/// nothing between documents. Every operation takes a cancellation token, looked at between elements and
/// between pages, so a host that gives up on a long document is not made to wait for it; what reached the
/// stream by then is not a document.
/// </para>
/// </remarks>
/// <typeparam name="TOptions">What the renderer accepts beyond what every document has in common.</typeparam>
public abstract class Converter<TOptions>(FontLibrary library, TOptions options)
    where TOptions : DocumentOptions
{
    /// <summary>The faces documents are set in.</summary>
    protected FontLibrary Library => library;

    /// <summary>What the renderer was asked for.</summary>
    protected TOptions Options => options;

    /// <summary>The document, as bytes.</summary>
    /// <remarks>Pages are A4 with 20 mm margins unless a setup is given.</remarks>
    public byte[] Convert(string html, PageSetup? setup = null, CancellationToken ct = default) =>
        Convert([new Section(html, setup ?? PageSetup.A4)], ct);

    /// <summary>The document whose sections are set on sheets of their own, as bytes.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the document was written.</exception>
    public byte[] Convert(IReadOnlyList<Section> sections, CancellationToken ct = default)
    {
        using var output = new MemoryStream();
        Write(sections, output, ct);

        return output.ToArray();
    }

    /// <summary>Writes the document to a stream and answers how it was laid out.</summary>
    /// <remarks>
    /// The composition carries the notes: pictures left out, font families substituted, bold asked of a family
    /// with no bold face, and whatever the format could not carry.
    /// </remarks>
    public Composition Write(string html, Stream output, PageSetup? setup = null, CancellationToken ct = default) =>
        Write([new Section(html, setup ?? PageSetup.A4)], output, ct);

    /// <summary>Writes a document of several sections to a stream.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the document was written.</exception>
    public Composition Write(IReadOnlyList<Section> sections, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        using var faces = new Faces(library);
        Composition composition = Compose(sections, faces, ct);

        Write(composition, output, faces, ct);

        return composition;
    }

    /// <summary>Lays a document out into pages without writing them.</summary>
    public Composition Typeset(string html, PageSetup? setup = null, CancellationToken ct = default) =>
        Typeset([new Section(html, setup ?? PageSetup.A4)], ct);

    /// <summary>Lays a document of several sections out into pages without writing them.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the document was set.</exception>
    public Composition Typeset(IReadOnlyList<Section> sections, CancellationToken ct = default)
    {
        using var faces = new Faces(library);

        return Compose(sections, faces, ct);
    }

    /// <summary>Writes pages laid out earlier by <see cref="Typeset(string,PageSetup,CancellationToken)"/> into a stream.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the pages were written.</exception>
    public void Render(Composition composition, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(output);

        using var faces = new Faces(library);
        Write(composition, output, faces, ct);
    }

    /// <summary>Writes composed pages in the renderer's own format, with the faces they were measured in.</summary>
    /// <remarks>A writer looks at the token between pages, so a deck of hundreds of slides stops within one.</remarks>
    protected abstract void Write(Composition composition, Stream output, Faces faces, CancellationToken ct);

    /// <summary>What the renderer has to say beyond the faces: whatever its format could not carry.</summary>
    protected virtual IEnumerable<Note> NotesFor(Composition composition) => [];

    /// <summary>Whether the renderer reads the pages' text and tables as structure; see <see cref="Typesetter.IsStructured"/>.</summary>
    protected virtual bool IsStructured => false;

    private Composition Compose(IReadOnlyList<Section> sections, Faces faces, CancellationToken ct)
    {
        IImageSource pictures = options.Images ?? new LocalPictures(options.BaseDirectory);
        Composition composition = new Typesetter(faces, pictures) { IsStructured = IsStructured }.Compose(sections, ct);

        return composition with
        {
            Notes = [.. composition.Notes, .. Notes.For(composition, library, faces), .. NotesFor(composition)],
        };
    }
}
