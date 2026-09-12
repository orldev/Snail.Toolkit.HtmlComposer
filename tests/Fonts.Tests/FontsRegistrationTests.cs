using Microsoft.Extensions.DependencyInjection;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

public sealed class FontsRegistrationTests
{
    private static readonly string FontsFolder = Path.Combine(AppContext.BaseDirectory, "Fonts");

    [Fact]
    public void AddFonts_LoadsOneLibraryAndSharesIt()
    {
        int loads = 0;
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(fonts =>
            {
                loads++;
                fonts.AddDirectory(FontsFolder);
            })
            .BuildServiceProvider();

        FontLibrary first = provider.GetRequiredService<FontLibrary>();
        FontLibrary second = provider.GetRequiredService<FontLibrary>();

        Assert.Same(first, second);
        Assert.Equal(1, loads);
        Assert.Contains("PT Sans", first.Families);
    }

    /// <summary>The container owns the library it made, so the faces are released with the application.</summary>
    /// <remarks>
    /// Proved by the library refusing, not by its families coming back empty. An empty list was what a
    /// disposed library used to answer while it went on setting documents in the machine's default face, so
    /// it said nothing about whether the faces had been released.
    /// </remarks>
    [Fact]
    public void AddFonts_DisposesTheLibraryWithTheContainer()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddFonts(fonts => fonts.AddDirectory(FontsFolder))
            .BuildServiceProvider();
        FontLibrary fonts = provider.GetRequiredService<FontLibrary>();

        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fonts.Families);
    }

    [Fact]
    public void AddFonts_Twice_KeepsTheFirstLibrary()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFonts(fonts => fonts.AddDirectory(FontsFolder))
            .AddFonts(_ => { })
            .BuildServiceProvider();

        Assert.Contains("PT Sans", provider.GetRequiredService<FontLibrary>().Families);
    }
}
