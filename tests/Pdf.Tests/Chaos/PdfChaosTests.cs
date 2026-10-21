using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf.Tests.Chaos;

/// <summary>Inputs and failures a PDF converter in a web server meets, which it must survive.</summary>
public sealed class PdfChaosTests : IDisposable
{
    private readonly FontLibrary _fonts = new FontLibrary().AddDirectory(AppContext.BaseDirectory + "Fonts");

    public void Dispose() => _fonts.Dispose();

    /// <summary>
    /// Skia wrote the PDF from a native callback into the managed stream, and an exception from the stream — a
    /// client gone, a full disk, an ASP.NET body that refuses synchronous writes — crossed the native frame and
    /// aborted the process (SIGABRT, exit 134). The stream's own exception has to reach the caller instead.
    /// </summary>
    [Fact]
    public void Write_ToAStreamThatFails_ThrowsInsteadOfAbortingTheProcess()
    {
        using var stream = new MemoryStream(new byte[16], writable: false);

        Assert.Throws<NotSupportedException>(() => new HtmlToPdf(_fonts).Write("<p>text</p>", stream));
    }

    /// <summary>
    /// <c>&lt;use&gt;</c> may point at the group it stands in; eight hops are allowed but nothing caps the
    /// fan-out, so six uses in a group expand to 6⁸ shapes from 300 bytes of markup, and the drawing never
    /// looks at the cancellation token the caller passed.
    /// </summary>
    [Fact]
    public void SvgThatExpandsItself_StopsWhenTheCallerCancels()
    {
        string uses = string.Concat(Enumerable.Repeat("<use href='#a'/>", 6));
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><g id='a'><rect width='1' height='1'/>{uses}</g></svg>";
        string html = $"<img src='data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}' style='width:20pt;height:20pt'>";
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();

        try
        {
            new HtmlToPdf(_fonts).Convert(html, ct: cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"the conversion ran {clock.Elapsed.TotalSeconds:F1} s past a 1 s cancellation");
    }

    /// <summary>A weight out of range in an SVG label reaches Math.Abs(int.MinValue) when a face is chosen: OverflowException out of Convert.</summary>
    [Fact]
    public void SvgLabelWithAnAbsurdWeight_IsSetInTheNearestFace()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><text x='0' y='15' font-weight='-2147483248'>A</text></svg>";
        string html = $"<img src='data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}'>";

        byte[] pdf = new HtmlToPdf(_fonts).Convert(html);

        Assert.NotEmpty(pdf);
    }

    /// <summary>
    /// The transform pattern matches «(» up to the next «)» from every opening parenthesis, so an attribute of
    /// nested calls with no closing one costs quadratic time — and the drawing is parsed again on every page.
    /// </summary>
    [Fact]
    public void SvgTransformWithoutClosingParenthesis_RendersInLinearTime()
    {
        TimeSpan unclosed = Timed(string.Concat(Enumerable.Repeat("scale(", 100_000)));
        TimeSpan closed = Timed(string.Concat(Enumerable.Repeat("scale(1)", 75_000)));

        Assert.True(unclosed < (closed * 3) + TimeSpan.FromSeconds(0.5), $"600 KB of unclosed transform took {unclosed.TotalSeconds:F2} s against {closed.TotalSeconds:F2} s closed");
    }

    private TimeSpan Timed(string transform)
    {
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='5' height='5' transform='{transform}'/></svg>";
        string html = $"<img src='data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}'>";
        var clock = Stopwatch.StartNew();

        new HtmlToPdf(_fonts).Convert(html);

        return clock.Elapsed;
    }

    /// <summary>
    /// SkiaSharp embeds a face whole while naming it a subset: two letters carried all 443 KB of PT Sans, and
    /// every document was as heavy as its fonts. The faces are now cut down to the glyphs drawn before Skia sees them.
    /// </summary>
    [Fact]
    public void TwoLetters_EmbedASubsetOfTheFace()
    {
        byte[] pdf = new HtmlToPdf(_fonts).Convert("<p>Hi</p>");

        Assert.True(pdf.Length < 100_000, $"a PDF of two letters weighs {pdf.Length} bytes");
    }

    /// <summary>
    /// A picture is decoded whole at the size its header declares: a few hundred kilobytes of PNG declaring
    /// 8 000 × 8 000 pixels become 256 MB of bitmap for a 100-point frame, and nothing limits the size or says so.
    /// </summary>
    [Fact]
    public void PictureDeclaringAHugeBitmap_IsRefusedWithANote()
    {
        string source = $"data:image/png;base64,{Convert.ToBase64String(BlankPng(8000, 8000))}";
        using var output = new MemoryStream();

        Composition composition = new HtmlToPdf(_fonts).Write($"<img src='{source}' style='width:100pt'>", output);

        Assert.Contains(composition.Notes, note => note.Kind == NoteKind.MissingPicture);
    }

    private static byte[] BlankPng(int width, int height)
    {
        using var png = new MemoryStream();

        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 0, 0, 0, 0]);

        using var raw = new MemoryStream();

        using (var deflate = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = new byte[width + 1];

            for (int line = 0; line < height; line++)
            {
                deflate.Write(row);
            }
        }

        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);

        png.Write(BigEndian(data.Length));
        png.Write(name);
        png.Write(data);
        png.Write(BigEndian((int)Crc([.. name, .. data])));
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in bytes)
        {
            crc ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
