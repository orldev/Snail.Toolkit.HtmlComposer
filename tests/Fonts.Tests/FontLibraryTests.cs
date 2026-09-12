namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

public sealed class FontLibraryTests
{
    [Fact]
    public void AddDirectory_WithEveryFaceOfOneFamily_KnowsThatFamilyOnce()
    {
        using FontLibrary fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));

        Assert.Equal("PT Sans", Assert.Single(fonts.Families));
    }

    [Fact]
    public void AddResource_EmbeddedFont_LoadsItsFamily()
    {
        using FontLibrary fonts = new FontLibrary().AddResource(typeof(FontLibraryTests).Assembly, "Fonts.PT_Sans-Web-Bold.ttf");

        Assert.Equal("PT Sans", Assert.Single(fonts.Families));
    }

    [Fact]
    public void AddResource_NameTheAssemblyDoesNotEmbed_Refuses()
    {
        using var fonts = new FontLibrary();

        Assert.Throws<ArgumentException>(() => fonts.AddResource(typeof(FontLibraryTests).Assembly, "Fonts.Missing.ttf"));
    }

    [Fact]
    public void Add_BytesThatAreNotAFont_Refuses()
    {
        using var fonts = new FontLibrary();
        using var stream = new MemoryStream([1, 2, 3, 4]);

        Assert.Throws<ArgumentException>(() => fonts.Add(stream));
    }

    /// <summary>
    /// A disposed library used to go on answering: the faces were gone, so every font fell back to the
    /// machine's default typeface and documents were set, without a word, in something other than what they
    /// asked for. A container disposing the library while a conversion runs has to be an error, not a silent
    /// change of face.
    /// </summary>
    [Fact]
    public void Families_AfterDispose_Refuses()
    {
        var fonts = new FontLibrary();
        fonts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fonts.Families);
    }

    [Fact]
    public void AddFile_AfterDispose_Refuses()
    {
        var fonts = new FontLibrary();
        fonts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fonts.AddFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "PT_Sans-Web-Regular.ttf")));
    }

    /// <summary>A container that owns the library may dispose it more than once, which is not an error.</summary>
    [Fact]
    public void Dispose_CalledTwice_IsHarmless()
    {
        var fonts = new FontLibrary();

        fonts.Dispose();
        fonts.Dispose();
    }
}
