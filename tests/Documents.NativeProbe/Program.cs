using Snail.Toolkit.HtmlComposer.Fonts;
using Snail.Toolkit.HtmlComposer.Pdf;
using Snail.Toolkit.HtmlComposer.Pptx;

const string html = "<h1 style=\"font-family:'PT Sans'\">Native</h1><p style=\"font-family:'PT Sans'\">A page laid out and drawn under Native AOT.</p>";

using var fonts = new FontLibrary().AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
var pdf = new HtmlToPdf(fonts).Convert(html);
var deck = new HtmlToPptx(fonts).Convert(html);
var isPdf = pdf.AsSpan().StartsWith("%PDF"u8);
var isDeck = deck.AsSpan().StartsWith("PK"u8);

Console.WriteLine($"pdf {pdf.Length} bytes, deck {deck.Length} bytes");

return isPdf && isDeck ? 0 : 1;
