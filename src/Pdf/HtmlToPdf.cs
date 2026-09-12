using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf;

/// <summary>Turns HTML into PDF: lays the markup out into pages and draws them with their fonts embedded.</summary>
public sealed class HtmlToPdf(FontLibrary fonts, PdfOptions? options = null)
    : Converter<PdfOptions>(fonts, options ?? new PdfOptions())
{
    /// <inheritdoc/>
    protected override void Write(Composition composition, Stream output, Faces faces, CancellationToken ct) =>
        Press.Print(composition, output, faces, Options, ct);
}
