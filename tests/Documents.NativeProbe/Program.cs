using System.IO.Compression;
using System.Text;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using Snail.Toolkit.HtmlComposer.Pdf;
using Snail.Toolkit.HtmlComposer.Pptx;

const string drawing = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><rect width='40' height='20' fill='#d52321'/><text x='4' y='14' font-size='10'>SVG</text></svg>";
string picture = $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(drawing))}";
string html = $"""
               <style>body {"{"} font-family: 'PT Sans' {"}"} td {"{"} border-bottom: 0.5pt solid #ccc; padding: 4pt {"}"}</style>
               <h1>Native</h1>
               <p>A page laid out and drawn under Native AOT.</p>
               <table><thead><tr><th style='width:60pt'>No</th><th>Name</th></tr></thead>
               <tbody><tr><td>01</td><td><div><b>Lift</b></div><div>details</div></td></tr>
               <tr><td colspan='2'>Total</td></tr></tbody></table>
               <img src='{picture}' style='width:80pt'>
               {drawing}
               """;
PageSetup setup = PageSetup.A4 with { Footer = "<p style=\"font-family:'PT Sans';text-align:right\">{page} / {pages}</p>" };
Section[] sections = [new Section(html, setup)];

using var fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
using var pdf = new MemoryStream();
using var deck = new MemoryStream();

Composition printed = new HtmlToPdf(fonts).Write(sections, pdf);
Composition slides = new HtmlToPptx(fonts).Write(sections, deck);

bool isPdf = pdf.ToArray().AsSpan().StartsWith("%PDF"u8);
string slide = Entry(deck, "ppt/slides/slide1.xml");
string layout = Entry(deck, "ppt/slideLayouts/slideLayout2.xml");
bool hasTable = slide.Contains("<a:tbl>", StringComparison.Ordinal) && slide.Contains("gridSpan=\"2\"", StringComparison.Ordinal);
bool hasNumber = layout.Contains("type=\"slidenum\"", StringComparison.Ordinal);
bool hasDrawing = deck.ToArray().Length > 0 && slides.Pages.Sum(page => page.Images.Count) == 2;
bool isClean = printed.Notes.Count == 0 && slides.Notes.Count == 0;

Console.WriteLine($"pdf {pdf.Length} bytes, deck {deck.Length} bytes; table {hasTable}, slide number {hasNumber}, drawings {hasDrawing}, notes {printed.Notes.Count + slides.Notes.Count}");

return isPdf && hasTable && hasNumber && hasDrawing && isClean ? 0 : 1;

static string Entry(MemoryStream package, string name)
{
    using var zip = new ZipArchive(new MemoryStream(package.ToArray()), ZipArchiveMode.Read);
    using var reader = new StreamReader(zip.GetEntry(name)?.Open() ?? Stream.Null);

    return reader.ReadToEnd();
}
