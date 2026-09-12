using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf;

/// <summary>What a PDF says about itself beyond what every document does.</summary>
public sealed record PdfOptions : DocumentOptions
{
    /// <summary>The application that produced the HTML the document was made from.</summary>
    public string? Creator { get; init; }
}
