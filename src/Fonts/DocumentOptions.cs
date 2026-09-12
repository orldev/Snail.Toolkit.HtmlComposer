using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>What every document has in common: where its pictures come from and what it says about itself.</summary>
/// <remarks>
/// A renderer adds what its format alone can express, such as the creator of a PDF or whether a deck carries
/// its faces. The common part lives here once, so the two converters cannot drift apart in what they accept.
/// </remarks>
public abstract record DocumentOptions
{
    /// <summary>The folder relative picture sources are read from; <see langword="null"/> reads no files at all.</summary>
    /// <remarks>
    /// Files outside this folder are never read, whatever the markup asks for. HTML is often written by users,
    /// and without the fence <c>&lt;img src="../../keys.png"&gt;</c> would copy any picture the process can read
    /// into the document.
    /// </remarks>
    public string? BaseDirectory { get; init; }

    /// <summary>Reads pictures in place of the built-in source of <c>data:</c> URIs and files.</summary>
    /// <remarks>
    /// The built-in source never goes to the network. Supply one here when pictures live in storage or behind
    /// a URL, with the timeouts and allow-lists the application needs.
    /// </remarks>
    public IImageSource? Images { get; init; }

    /// <summary>What a reader shows as the name of the document.</summary>
    public string? Title { get; init; }

    /// <summary>Who is named as the author of the document.</summary>
    public string? Author { get; init; }

    /// <summary>What the document is about, in a sentence.</summary>
    public string? Subject { get; init; }

    /// <summary>Search terms for the document, separated by commas.</summary>
    public string? Keywords { get; init; }
}
