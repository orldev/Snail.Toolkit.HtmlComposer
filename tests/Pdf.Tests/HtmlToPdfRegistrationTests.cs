using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.HtmlComposer.Fonts;
using UglyToad.PdfPig;

namespace Snail.Toolkit.HtmlComposer.Pdf.Tests;

public sealed class HtmlToPdfRegistrationTests
{
    [Fact]
    public void AddHtmlToPdf_ResolvesAConverterOverTheRegisteredFonts()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(fonts => fonts.AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts")))
            .AddHtmlToPdf(new PdfOptions { Title = "Registered" })
            .BuildServiceProvider();

        using var document = PdfDocument.Open(provider.GetRequiredService<HtmlToPdf>().Convert("<p>x</p>"));

        Assert.Equal("Registered", document.Information.Title);
    }

    [Fact]
    public void AddHtmlToPdf_SharesOneConverter()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(_ => { })
            .AddHtmlToPdf()
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<HtmlToPdf>(), provider.GetRequiredService<HtmlToPdf>());
    }
}
