using System.Diagnostics;
using System.Text;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Snail.Toolkit.HtmlComposer.Pdf.Tests;

public sealed class HtmlToPdfTests : IDisposable
{
    private const string Pangram = "Съешь же ещё этих мягких французских булок, да выпей чаю.";

    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(FontPath(""));

    public void Dispose() => _fonts.Dispose();

    /// <summary>A TrueType face is embedded as a real font; anything the writer cannot embed that way becomes Type 3.</summary>
    [Fact]
    public void Convert_CyrillicText_EmbedsItsFontAsTrueType()
    {
        byte[] pdf = new HtmlToPdf(_fonts).Convert($"<p style=\"font-family:'PT Sans'\">{Pangram}</p>");

        Assert.True(Count(pdf, "/FontFile2") > 0);
        Assert.True(Count(pdf, "/ToUnicode") > 0);
        Assert.Equal(0, Count(pdf, "/Type3"));
    }

    [Fact]
    public void Convert_CyrillicText_ReadsBackFromThePdf()
    {
        byte[] pdf = new HtmlToPdf(_fonts).Convert($"<p>{Pangram}</p>");

        Assert.Contains("французских", Assert.Single(PageTexts(pdf)), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_LongDocument_DrawsEveryPageTheEngineSet()
    {
        using var output = new MemoryStream();
        string lines = string.Join("<br>", Enumerable.Range(1, 120).Select(number => $"Line{number}"));

        Composition composition = new HtmlToPdf(_fonts).Write($"<p>{lines}</p>", output);
        List<string> texts = PageTexts(output.ToArray());

        Assert.True(composition.Pages.Count > 1);
        Assert.Equal(composition.Pages.Count, texts.Count);
        Assert.Contains("Line120", texts[^1], StringComparison.Ordinal);
    }

    /// <summary>The engine breaks lines with the widths it measured; the PDF has to draw the glyphs exactly that wide.</summary>
    [Fact]
    public void Convert_MeasuredWidth_MatchesWhereThePdfDrawsTheText()
    {
        const string html = "<p>Line width</p>";
        var converter = new HtmlToPdf(_fonts);
        TextSpan span = Assert.Single(converter.Typeset(html).Pages[0].Spans);

        using var document = PdfDocument.Open(converter.Convert(html));
        IReadOnlyList<Letter> letters = document.GetPage(1).Letters;

        Assert.Equal(span.X, letters[0].StartBaseLine.X, 0.5);
        Assert.Equal(span.X + span.Width, letters[^1].EndBaseLine.X, 0.5);
    }

    /// <summary>A tracked run is placed glyph by glyph, and has to land exactly where it was measured.</summary>
    /// <remarks>
    /// The measured width carries the tracking that follows the last glyph, as CSS letter spacing does, so the
    /// last glyph ends one tracking short of it. Type 3 is counted as well: placing glyphs by hand must not
    /// cost the embedded TrueType face, or the text would stop being searched and copied.
    /// </remarks>
    [Fact]
    public void Convert_TrackedText_DrawsTheGlyphsWhereTheyWereMeasured()
    {
        const float tracking = 1.5f;
        const string html = "<p style='letter-spacing:1.5pt'>Tracking</p>";
        var converter = new HtmlToPdf(_fonts);
        TextSpan span = Assert.Single(converter.Typeset(html).Pages[0].Spans);
        byte[] pdf = converter.Convert(html);

        using var document = PdfDocument.Open(pdf);
        UglyToad.PdfPig.Content.Page page = document.GetPage(1);

        Assert.Equal(span.X, page.Letters[0].StartBaseLine.X, 0.5);
        Assert.Equal(span.X + span.Width - tracking, page.Letters[^1].EndBaseLine.X, 0.5);
        Assert.Equal(0, Count(pdf, "/Type3"));
        Assert.Contains("Tracking", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_DataUriPicture_IsEmbeddedInThePage()
    {
        string source = $"data:image/png;base64,{Convert.ToBase64String(RedSquare())}";

        using var document = PdfDocument.Open(new HtmlToPdf(_fonts).Convert($"<img src='{source}' width='40'>"));

        Assert.Single(document.GetPage(1).GetImages());
    }

    [Fact]
    public void Convert_PictureInsideTheBaseDirectory_IsRead()
    {
        using var scratch = new Scratch();
        scratch.Write("site/logo.png", RedSquare());
        using var output = new MemoryStream();

        var converter = new HtmlToPdf(_fonts, new PdfOptions { BaseDirectory = Path.Combine(scratch.Root, "site") });
        Composition composition = converter.Write("<img src='logo.png'>", output);

        Assert.Single(composition.Pages[0].Images);
        Assert.Empty(composition.Notes);
    }

    /// <summary>User-written HTML must not be able to copy files from elsewhere on the server into a document.</summary>
    [Fact]
    public void Convert_PictureOutsideTheBaseDirectory_IsNeverRead()
    {
        using var scratch = new Scratch();
        scratch.Write("secret.png", RedSquare());
        scratch.Write("site/index.txt", []);
        using var output = new MemoryStream();

        var converter = new HtmlToPdf(_fonts, new PdfOptions { BaseDirectory = Path.Combine(scratch.Root, "site") });
        Composition composition = converter.Write("<img src='../secret.png'>", output);

        Note note = Assert.Single(composition.Notes);

        Assert.Empty(composition.Pages[0].Images);
        Assert.Equal(NoteKind.MissingPicture, note.Kind);
        Assert.Equal("../secret.png", note.Subject);
    }

    [Fact]
    public void Convert_BoldWithoutABoldFace_SetsRegularAndSaysSo()
    {
        using FontLibrary regular = new FontLibrary().AddFile(FontPath("PT_Sans-Web-Regular.ttf"));
        using var output = new MemoryStream();

        Composition composition = new HtmlToPdf(regular).Write("<p><b>Bold</b></p>", output);

        Assert.Contains(composition.Notes, note => note is { Kind: NoteKind.BoldSubstituted, Subject: "PT Sans" });
        Assert.Equal(0, Count(output.ToArray(), "/Type3"));
    }

    [Fact]
    public void Convert_ItalicWithoutAnItalicFace_StillEmbedsTrueType()
    {
        using FontLibrary regular = new FontLibrary().AddFile(FontPath("PT_Sans-Web-Regular.ttf"));

        byte[] pdf = new HtmlToPdf(regular).Convert("<p><i>Italic</i></p>");

        Assert.True(Count(pdf, "/FontFile2") > 0);
        Assert.Equal(0, Count(pdf, "/Type3"));
    }

    /// <summary>PT Sans has no arrow, and a missing glyph prints as a crossed box with nothing in the file to explain it.</summary>
    [Fact]
    public void Convert_CharacterTheFaceCannotSet_NamesItInTheNotes()
    {
        using var output = new MemoryStream();

        Composition composition = new HtmlToPdf(_fonts).Write("<p>points → to the card</p>", output);

        Note note = Assert.Single(composition.Notes, note => note.Kind == NoteKind.MissingGlyph);

        Assert.Contains("→", note.Text, StringComparison.Ordinal);
        Assert.Equal(note.Text, note.ToString());
    }

    [Fact]
    public void Convert_TextTheFaceCoversEntirely_SaysNothing()
    {
        using var output = new MemoryStream();

        Composition composition = new HtmlToPdf(_fonts).Write($"<p>{Pangram}</p>", output);

        Assert.Empty(composition.Notes);
    }

    [Fact]
    public void Convert_UnknownFamily_FallsBackToTheFirstLoadedAndSaysSo()
    {
        using var output = new MemoryStream();

        Composition composition = new HtmlToPdf(_fonts).Write("<p style='font-family:Garamond'>x</p>", output);

        Assert.Contains(composition.Notes, note => note is { Kind: NoteKind.FamilySubstituted, Subject: "Garamond" });
    }

    /// <summary>Pages laid out once can be written later, and the file holds exactly the pages the layout came to.</summary>
    [Fact]
    public void Render_PagesTypesetEarlier_WritesThemWithoutSettingThemAgain()
    {
        var converter = new HtmlToPdf(_fonts);
        Composition composition = converter.Typeset($"<p>{Pangram}</p>");
        using var output = new MemoryStream();

        converter.Render(composition, output);

        List<string> texts = PageTexts(output.ToArray());
        Assert.Equal(composition.Pages.Count, texts.Count);
        Assert.Contains("французских", texts[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_WithACancelledToken_StopsInsteadOfWriting()
    {
        var converter = new HtmlToPdf(_fonts);

        Assert.Throws<OperationCanceledException>(() => converter.Convert("<p>x</p>", ct: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Convert_WithATitle_WritesItIntoTheDocumentInformation()
    {
        byte[] pdf = new HtmlToPdf(_fonts, new PdfOptions { Title = "Report" }).Convert("<p>x</p>");

        using var document = PdfDocument.Open(pdf);

        Assert.Equal("Report", document.Information.Title);
    }

    private static string FontPath(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    private static int Count(byte[] pdf, string token)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int count = 0;

        for (int index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static List<string> PageTexts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        return [.. document.GetPages().Select(page => page.Text)];
    }

    private static byte[] RedSquare()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);

        return png.ToArray();
    }

    /// <summary>A folder of its own for one test, removed with everything in it afterwards.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo _folder = new(Folder());

        public string Root => _folder.FullName;

        public void Write(string relative, byte[] content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public void Dispose() => _folder.Delete(recursive: true);

        /// <summary>A new folder named as Snail.Sdk.Mcp.Testing names a test's, so a cleaner can prove a crashed run's is a leftover.</summary>
        /// <remarks>
        /// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>, the start in Unix seconds as the system reports it: the tests of a
        /// library do not reference the package, so they name the folder themselves, as the standard asks of a project without
        /// it. <see cref="Dispose"/> deletes it when the test ends.
        /// </remarks>
        private static string Folder()
        {
            using var process = Process.GetCurrentProcess();
            long start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
            string folder = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{start}-{Path.GetRandomFileName()[..8]}");

            Directory.CreateDirectory(folder);

            return folder;
        }
    }
}
