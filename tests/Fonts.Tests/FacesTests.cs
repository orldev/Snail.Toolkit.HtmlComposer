using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

public sealed class FacesTests
{
    /// <summary>
    /// Measuring through a library that has been disposed used to answer a different width rather than fail:
    /// the same run measured 53.41 points before the disposal and 54.70 after it, because the text had quietly
    /// moved to the machine's default typeface. A width that changes under the layout is worse than an error.
    /// </summary>
    [Fact]
    public void For_AfterTheLibraryIsDisposed_Refuses()
    {
        FontLibrary fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        using var faces = new Faces(fonts);

        fonts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => faces.For(new Font("PT Sans", 12)));
    }

    [Fact]
    public void Measure_AfterTheFacesAreDisposed_Refuses()
    {
        using FontLibrary fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        var faces = new Faces(fonts);

        faces.Dispose();

        Assert.Throws<ObjectDisposedException>(() => faces.Measure("text", new Font("PT Sans", 12)));
    }

    [Fact]
    public void Dispose_CalledTwice_IsHarmless()
    {
        using FontLibrary fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        var faces = new Faces(fonts);

        faces.Dispose();
        faces.Dispose();
    }
}
