using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pptx.Tests;

public sealed class HtmlToPptxRegistrationTests
{
    [Fact]
    public void AddHtmlToPptx_ResolvesAConverterOverTheRegisteredFonts()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(fonts => fonts.AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts")))
            .AddHtmlToPptx(new PptxOptions { Title = "Registered" })
            .BuildServiceProvider();

        using var deck = PresentationDocument.Open(
            new MemoryStream(provider.GetRequiredService<HtmlToPptx>().Convert("<p>x</p>")),
            false);

        Assert.Equal("Registered", deck.PackageProperties.Title);
    }

    [Fact]
    public void AddHtmlToPptx_SharesOneConverter()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(_ => { })
            .AddHtmlToPptx()
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<HtmlToPptx>(), provider.GetRequiredService<HtmlToPptx>());
    }
}
