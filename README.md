# Snail.Toolkit.HtmlComposer

Turn HTML and CSS into a paged **PDF** or a **PowerPoint deck** — from .NET, with no browser, no headless
Chrome and no network access.

```csharp
using var fonts = new FontLibrary().AddDirectory("fonts");
var pdf = new HtmlToPdf(fonts);

byte[] bytes = pdf.Convert("<h1>Hello</h1><p>A real, paginated PDF.</p>");
```

## Is this for you?

**Good fit.** Invoices, quotations, reports, certificates, contracts, one-pagers — documents you design once as
an HTML template, fill with data and hand to a user. You get the same template rendered as a PDF *and* as an
editable PowerPoint deck, laid out identically.

**Bad fit.** Rendering arbitrary web pages. There is no JavaScript, no floats, no flexbox and no grid. If you
need a real browser engine, use one.

| What you get | What you don't |
| --- | --- |
| Real pagination: page breaks, orphans/widows, repeating table headers | JavaScript, floats, `position`, flex, grid |
| Running headers/footers with `{page} / {pages}` | `@page`, `@font-face`, `@media (min-width: …)` |
| Fonts embedded in the file (Cyrillic included, searchable and copyable) | Arabic/Devanagari shaping, per-glyph font fallback |
| Tables that split across pages, `colspan`/`rowspan`, `border-collapse` | SVG gradients, masks, filters and `<style>` sheets |
| SVG drawn as vectors, its labels as real text in the document's fonts | |
| No network calls, ever, and [hard limits](#limits-on-what-a-document-may-ask) on what markup may ask — safe to feed user-authored HTML | Fetching `http://` images for you |

---

## Install

Install only the renderer you need; the engine and font packages come with it.

```bash
dotnet add package Snail.Toolkit.HtmlComposer.Pdf     # PDF
dotnet add package Snail.Toolkit.HtmlComposer.Pptx    # PowerPoint
```

Requires **.NET 10**. Works on Windows, macOS and Linux — including slim containers, see [Linux](#linux-and-containers).

<details>
<summary>The four packages and how they relate</summary>

| Package | What it is | Depends on |
| --- | --- | --- |
| `…HtmlComposer.Engine` | Parses HTML/CSS, lays out the document, cuts it into pages of positioned boxes, text and pictures. Knows nothing about PDF or PowerPoint. | AngleSharp |
| `…HtmlComposer.Fonts` | Loads typefaces, measures text with them, reads pictures. The shared half of every renderer. | Engine, SkiaSharp |
| `…HtmlComposer.Pdf` | Draws those pages into a PDF, fonts embedded. | Engine, Fonts |
| `…HtmlComposer.Pptx` | Writes those pages as PowerPoint slides. | Engine, Fonts, DocumentFormat.OpenXml |

Four packages is about dependency hygiene, not about four installs: a PDF consumer never pulls in the Open XML
SDK, and someone writing their own renderer can take `Engine` alone. Install `Pdf` and you have all you need.

</details>

---

## Quick start

### 1. Load fonts — this is not optional

The library ships **no fonts** and never reads the ones installed on the machine. If you load nothing, text is
set in whatever the OS default typeface is, which on a Linux container is nothing at all.

```csharp
using var fonts = new FontLibrary()
    .AddDirectory("fonts")                                          // every .ttf/.otf in a folder
    .AddFile("fonts/Brand-Bold.ttf")                                // one file
    .AddResource(typeof(Program).Assembly, "App.Fonts.Brand.ttf")   // an embedded resource
    .Add(stream);                                                   // any stream
```

Three rules that save a lot of debugging:

- **Prefer `.ttf` (TrueType).** A `.otf` file with CFF outlines is embedded as Type 3 glyph procedures: it
  prints without hinting and cannot be searched or copied, and a PowerPoint deck cannot carry it at all.
- **Load the bold and italic faces too**, not just the regular one. There is no synthetic bold — a family with
  no bold face is simply set in its regular face. (A missing *italic* is faked by slanting the regular face,
  which is fine.)
- **Load everything before the first conversion.** After that the library is read-only and safe to share
  between threads.

A PDF carries each face **cut down to the glyphs it draws**: a page of two letters in PT Sans weighs a few
kilobytes of font rather than 440. Glyph numbers and advances are kept, so the text is drawn exactly as it was
measured and still searched and copied. A face whose maker forbids subsetting (bit 8 of `fsType`) is embedded
whole, as the licence asks; so is a CFF `.otf`, which is drawn as Type 3 anyway.

Asking for a family that is not loaded falls back to the **first family you loaded**, and you get a note about
it. Generic names — `sans-serif`, `monospace` — resolve to that same fallback silently.

### 2. Convert

```csharp
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using Snail.Toolkit.HtmlComposer.Pdf;

using var fonts = new FontLibrary().AddDirectory("fonts");

var pdf = new HtmlToPdf(fonts, new PdfOptions
{
    BaseDirectory = "wwwroot",        // where <img src="..."> may read files from
    Title = "Quarterly report",
});

byte[] bytes = pdf.Convert(html);                          // A4, 20 mm margins
byte[] letter = pdf.Convert(html, PageSetup.Letter);
byte[] wide = pdf.Convert(html, PageSetup.A4.Landscape());

await File.WriteAllBytesAsync("report.pdf", bytes);
```

### 3. Check what could not be rendered

A conversion **never throws** because a picture is missing or a glyph is absent. It renders the document and
tells you. Use `Write` instead of `Convert` to get that report back:

```csharp
using var file = File.Create("report.pdf");
Composition composition = pdf.Write(html, file, PageSetup.A4);

foreach (Note note in composition.Notes)
{
    logger.LogWarning("{Note}", note);        // note.ToString() is a human sentence
}

if (composition.Notes.Any(n => n.Kind == NoteKind.MissingPicture))
{
    // decide by Kind, not by parsing the sentence
}

int pageCount = composition.Pages.Count;
```

| `NoteKind` | Meaning |
| --- | --- |
| `MissingPicture` | A picture could not be read; its `alt` text was set instead. |
| `FamilySubstituted` | No loaded family matched; the fallback family was used. |
| `BoldSubstituted` | Bold was asked of a family with no bold face. |
| `CffOutlines` | A `.otf` face that the format embeds poorly, or cannot carry at all. |
| `MissingGlyph` | A character the chosen face has no glyph for — it prints as a crossed box. |
| `NoFonts` | You loaded no fonts at all. |
| `SpanCapped` | A `colspan`/`rowspan` exceeded the limit and was capped. |
| `NestingCapped` | Markup or a drawing went deeper or larger than the [limits](#limits-on-what-a-document-may-ask); the rest was set as plain text, or left undrawn. |
| `FormatLimit` | The output format could not hold something as set — a deck has one slide size — and it was fitted. |

Each note carries `Kind`, the `Subject` it is about (the picture source, the font family) and a `Text` sentence.

---

## Using it in an ASP.NET Core app

```csharp
services
    .AddFonts(fonts => fonts
        .AddResource(typeof(Program).Assembly, "App.Fonts.Brand-Regular.ttf")
        .AddResource(typeof(Program).Assembly, "App.Fonts.Brand-Bold.ttf"))
    .AddHtmlToPdf(new PdfOptions { BaseDirectory = "wwwroot", Creator = "Acme Billing" })
    .AddHtmlToPptx(new PptxOptions { Language = "en-GB" });
```

Then inject `HtmlToPdf` or `HtmlToPptx` wherever you write a document.

Everything is registered as a **singleton**. The font library is loaded once and only read afterwards; a
converter keeps no state between documents. Both are safe to use from many requests at once — there are tests
that lay out and write the same document on 64 threads and compare every measured width against a
single-threaded run, byte for byte. The container disposes the font library with itself, and calling `AddFonts`
twice keeps the first library rather than reloading the faces.

> **Options belong to the registration, not to the call.** A singleton converter carries one `BaseDirectory`
> and one `Images` source for the life of the application. If a document needs pictures chosen per request,
> see [Pictures](#pictures) below.

---

## Page setup

`PageSetup` is the sheet: its size, its margins, and the furniture printed in those margins.

```csharp
PageSetup.A4        // 210 × 297 mm, 20 mm margins
PageSetup.A5        // 148 × 210 mm, 15 mm margins
PageSetup.Letter    // 8.5 × 11 in, 1 in margins

PageSetup.A4.Landscape()

var custom = new PageSetup(
    Width: Points.FromMillimeters(210),
    Height: Points.FromMillimeters(297),
    Margins: Edges.All(Points.FromMillimeters(15)));
```

Everything is in **points** (1/72 in). `Points.FromMillimeters`, `FromInches` and `FromPixels` convert for you.

### Headers, footers and side notes

```csharp
var setup = PageSetup.A4 with
{
    Header = "<div style='font-size:8pt;color:#888'>Riverside Towers · quotation</div>",
    Footer = "<div style='text-align:right;font-size:8pt'>p. {page} / {pages}</div>",
};
```

Each band is its own little HTML document with its own `<style>`. A header/footer is as wide as the content
area and as tall as the margin, so **give the margin enough room** — nothing is clipped, and furniture taller
than its margin runs into the text.

`{page}` and `{pages}` become the page number and the total count across the *whole* document, because running
lines are set last, once the final page exists.

A note in a side margin works the same way:

```csharp
var setup = PageSetup.A4 with
{
    Margins = new Edges(Top: 40, Right: 40, Bottom: 40, Left: 160),  // room for the note
    Side = "<div style='font-size:9pt'>Specification · prices incl. VAT</div>",
    SideAt = PageSide.Left,
};
```

The side band repeats on every page and takes no part in the layout — the content flows past it. If the note
has to *change* from page to page rather than repeat, make it a section of its own.

### Sections: a cover that differs from the body

A `Section` is one run of pages with its own sheet. Page numbers run continuously through all of them.

```csharp
var cover = PageSetup.A4 with
{
    Margins = Edges.All(0),                      // bleed to the paper edge
    Background = new Rgba(0xD5, 0x23, 0x21),     // sheet colour, painted edge to edge
};

var body = PageSetup.A4 with
{
    Footer = "<div style='text-align:right'>p. {page} / {pages}</div>",
};

byte[] bytes = pdf.Convert([new Section(coverHtml, cover), new Section(bodyHtml, body)]);
```

`Background` colours the *sheet*. A `background` declared in the markup stops at the margins, so a full-bleed
cover is a property of the page setup, not of a `<div>`.

---

## Pictures

Only two sources are read, and **nothing is ever fetched over the network**:

1. `data:` URIs — `<img src="data:image/png;base64,...">`
2. Files under `BaseDirectory`

Raster pictures (PNG, JPEG, …) and **SVG drawings** — see [Drawings (SVG)](#drawings-svg).

A path that leads outside `BaseDirectory` is refused, symbolic links included. HTML is often written by users,
and without that fence `<img src="../../keys.png">` would copy any file the process can read into the document.
Leave `BaseDirectory` as `null` and no files are read at all.

### Pictures your application already holds

For pictures that live in a database or object store, fetch them yourself — asynchronously, with your own
timeouts — and hand over the bytes under the **exact string your templates put in `src`**, so one template
serves the screen and the document alike:

```csharp
var pictures = new MemoryPictures()
    .Add("/designs/8fd18287/image", bytes)
    .Add("/logo.png", logoBytes);

var pdf = new HtmlToPdf(fonts, new PdfOptions { Images = pictures });
```

`MemoryPictures` is filled **before** use and only read afterwards — it is a plain dictionary, so adding to a
shared one while another thread is rendering is not safe. That makes it right for a converter built for one
request, and wrong for a singleton.

### A picture source of your own

For a store of many pictures where each document names only a few — the usual case for a long-running service —
implement `IImageSource` once, register it once, and let each document ask for what it needs:

```csharp
public sealed class StoragePictures(IFileStorage storage) : IImageSource
{
    public Image? Load(string source)
    {
        byte[]? content = storage.Get(source);       // your cache, your timeouts, your allow-list

        if (content is null)
        {
            return null;
        }

        // The engine needs the natural size to reserve room; the pixels are decoded later, by the renderer.
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);       // SkiaSharp ships with the Fonts package

        return codec is null ? null : new Image(content, codec.Info.Width, codec.Info.Height);
    }
}

services.AddFonts(…).AddHtmlToPdf(new PdfOptions { Images = new StoragePictures(storage) });
```

Returning `null` is an ordinary outcome: the picture is left out, its `alt` text stands in and a
`MissingPicture` note names the source.

The built-in sources refuse a raster past 50 million pixels; a source of your own is trusted with what it
returns. A renderer decodes the whole raster to draw it, so check `codec.Info` against a limit of your own before
handing back a picture someone else uploaded.

### Sizing and fitting

- A picture takes its natural size at 96 dpi, or whatever `width`/`height` (attribute or CSS) asks for, and is
  scaled down to fit the line and the page.
- `object-fit: fill` (the CSS default) stretches; `cover` keeps the frame and crops from the middle; `contain`
  keeps the whole picture and shrinks the frame to match.
- `vertical-align: middle` centres a picture against the text of its line — what an icon beside a heading needs.

---

## Drawings (SVG)

An SVG is a picture like any other: write it straight into the markup, or point an `<img>` at it — a file under
`BaseDirectory`, a `data:image/svg+xml;base64,…` URI, or a plain-text one such as
`data:image/svg+xml,%3Csvg…`. It takes room exactly as an `<img>` does, from `width`/`height`, CSS, or its
natural size.

```html
<p>The shaft, as the block sees it:</p>
<svg viewBox="0 0 120 60" style="width:240pt">
  <rect x="10" y="10" width="40" height="40" fill="#0a0a0a"/>
  <line x1="60" y1="30" x2="110" y2="30" stroke="#ff3b00" stroke-width="2"/>
  <text x="60" y="55" font-family="Suisse Intl" font-size="8">Floor 5</text>
</svg>
```

- **In a PDF it stays a drawing.** Shapes are written as paths, sharp at any zoom, and every label is set as
  text in the faces of the `FontLibrary` — searched, copied, and falling back to the first loaded family like
  the rest of the page. Nothing is turned into pixels.
- **In a deck it becomes a picture.** A slide holds pictures, not markup, so the drawing is rendered to PNG at
  four pixels to each of its own, no side longer than 4096.
- **Natural size** is read the way a browser reads the root: absolute `width`/`height` (`px`, `pt`, `mm`, `cm`,
  `in`, `pc`), the `viewBox` proportions for a missing side, the `viewBox` itself in CSS pixels when neither is
  given, and 300 × 150 when there is nothing to go by.
- **Drawn:** `rect` (with `rx`/`ry`), `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`, `text` with
  `tspan`, `g`, nested `svg`, `use`; `fill`, `stroke`, `stroke-width`, `stroke-dasharray`, `stroke-linecap`,
  `stroke-linejoin`, `fill-rule`, `opacity`, `fill-opacity`, `stroke-opacity`, `transform`, `viewBox` with
  `preserveAspectRatio`, `font-family`, `font-size`, `font-weight`, `font-style`, `letter-spacing`,
  `text-anchor`, `visibility`, `display`, `currentColor` — as presentation attributes or in the `style`
  attribute.
- **Not drawn:** gradients, patterns, masks, clip paths, filters, markers, pictures inside the drawing and
  `<style>` sheets. A fill that names a gradient falls back to the colour written after it, or to none.

A drawing is drawn within a budget: elements nested deeper than 256 and anything past 20 000 drawn elements —
each `use` counting as drawn again — are left undrawn, and a `NestingCapped` note names the picture. Groups with
an `opacity` are composited through at most four layers at once; deeper ones fade their own shapes instead.

`Drawings.Draw` and `Drawings.Rasterized` in the Fonts package draw the same thing onto any Skia canvas, for a
renderer of your own. Both take a `CancellationToken`, looked at while the drawing is walked.

---

## What CSS is supported

### Elements

`div` · `p` · `h1`–`h6` · `img` · `br` · `ul` `ol` `li` · `blockquote` · `pre` · sectioning elements ·
inline `span` `b` `strong` `i` `em` `u` `s` `a` `small` `sub` `sup` `code`

List bullets are drawn as shapes, not typed as glyphs — many text faces have no `•` or `▪` and would print a
crossed box. Numbered markers stay text.

### Text

`font-family` · `font-size` · `font-weight` · `font-style` · `color` · `line-height` · `text-align` (including
`justify`) · `text-indent` · `text-decoration` · `letter-spacing` · `text-transform` · `white-space` · soft
hyphens · zero-width spaces

`letter-spacing` given as a percentage is read as a share of the font size, which is how design tools state it.

### Box model

`margin` (with collapsing between siblings and through parents) · `padding` · `border` (`solid`, `dashed`,
`dotted`) · `border-radius` (one radius for all corners) · `background-color` · `width` · `max-width` ·
`height` · `box-sizing` · `box-decoration-break` · `margin: 0 auto`

A box cut by a page boundary is drawn as one box opened on the first page and closed on the last, so the cut
sides carry neither border nor padding — that is `box-decoration-break: slice`, the default. Say `clone` and
every fragment is whole instead, which is what a section carried over needs when the air above its content
belongs on every page it reaches.

An inline `span` carries its own `background`, `border` and `border-radius`, painted as tall as its own run —
so a small highlight inside large text stays the size of its letters.

### Tables

`table` `thead` `tbody` `tfoot` `tr` `td` `th`, and:

- **Column widths.** A width on a cell holds for its whole column. A column without one is measured from its
  content: it starts at its longest word, grows towards its unbroken width, then shares whatever is left, so
  the table fills the space it was given.
- **Splitting across pages.** A table too tall for the page is cut between rows, with `thead` repeated on each
  continuation. A single row too tall to fit is cut *across*: every cell is cut at the same height, no rule is
  drawn at the cut, and the grid lines up on both pages.
- **`tfoot`.** The foot of a table is set last, wherever it is written — HTML 4 required it before the body and
  HTML 5 allows it at either end, so its place in the markup says nothing about where it belongs. It is set
  once, where the table ends.
- **`<tfoot continued>`.** A foot that is set on every page the table crosses *except the last*, for the line
  that says the document goes on:

  ```html
  <tfoot continued><tr><td colspan="4">Continued on the next page.</td></tr></tfoot>
  ```

  A table that fits on one page never shows it, so the sentence is never a lie. Pagination holds back the room
  for it only once it knows the table is cut, so a table that fits exactly is not split to make space for a
  promise it was not going to need. Use a plain `tfoot` alongside it for the totals that belong at the end.

### Headings that say "continued"

`<aside resumed>` inside any block is set at the top of every fragment of that block **except the first** —
the mirror of `<tfoot continued>`:

```html
<td class="marginal">
  Specification
  <aside resumed>Specification · continued</aside>
</td>
```

It is what a margin column needs. A short note beside a long table runs out on the first page, and the
continuation would otherwise carry an empty box exactly where the reader needs telling what they are looking
at. A block that is never cut does not show it at all.

It is taken out of the flow by the user agent sheet rather than by the layout, so while drafting you can put
it back and see what it holds:

```css
aside[resumed] { display: block }
```
- **`colspan` / `rowspan`.** A cell spanning columns is measured last and only adds what those columns did not
  already ask for, so a wide heading does not dictate the shape of the table. A page never breaks inside a
  `rowspan`. (Spans are capped at 1000 — beyond that you get a `SpanCapped` note instead of an out-of-memory
  crash.)
- **`border-collapse: collapse`.** A cell gives up the rule its neighbour *also draws*, so a grid of 1 pt lines
  is 1 pt thick everywhere instead of 2 pt between cells. A rule no neighbour draws is kept — cells that
  declare `border-bottom` and nothing else keep a rule under every row, not just the last. Where both sides of
  an edge are declared, the thicker survives.
- **`vertical-align`.** Cells sit on a shared baseline by default, as CSS has it — a 13 pt label beside a 28 pt
  number reads as one line. `top`, `middle` and `bottom` place the cell content in the row instead.

### Pagination

`break-before` · `break-after` · `break-inside` (and the `page-break-*` forerunners) · `orphans` · `widows` ·
`@media print`

Headings are kept with what follows them, and every page is guaranteed to make progress.

### Units

`px` · `pt` · `pc` · `in` · `cm` · `mm` · `q` · `em` · `rem` · `%`

### Not supported

- **Layout:** floats, `position`, flex and grid — all laid out as plain blocks. Use tables for columns.
- **At-rules:** `@page`, `@font-face` (load fonts through `FontLibrary`), media features like `min-width`.
- **Text:** hyphenation, complex-script shaping (Arabic, Devanagari), per-glyph fallback to another family.
  A face has to cover the text it sets — a monospaced face with no Cyrillic will not quietly borrow letters
  from elsewhere the way a design tool does.
- **Pictures:** vector formats other than SVG, and the parts of SVG listed under [Drawings (SVG)](#drawings-svg).

---

## PowerPoint

Same engine, same fonts, a different writer: **one slide per page**, sized like the sheet.

```csharp
var deck = new HtmlToPptx(fonts, new PptxOptions
{
    BaseDirectory = "wwwroot",
    Title = "Quarterly report",
    Language = "en-GB",      // BCP 47 tag; runs are marked so the reader proofs in the right language
    EmbedFonts = true,       // default
});

byte[] pptx = deck.Convert(html);
```

**What to expect.** The deck is written in PowerPoint's own objects wherever the engine can say what a thing
is, so it is edited the way any deck is — a price changed in a cell stays in its cell, a sentence added to a
paragraph wraps inside its box:

- **A table is a PowerPoint table** (`a:tbl`): a column per width the page settled, rows as tall as the page
  made them, `colspan` as merged cells. A cell draws only the rules above and below it — where two rows
  disagree, the heavier rule — and nothing at its sides; its padding is the cell's margins and `vertical-align`
  its anchor (`baseline` reads as top, at the height the baseline put it). Each cell is one text body: every
  block or `<br>` inside is a paragraph of its own, in its own size and colour. A picture alone in a cell is the
  cell's **fill**, inset by the cell's padding, so it moves with its row. The plain table style ("No Style, No
  Grid") is used, so PowerPoint adds no banding of its own.
- **A table cut by the page** continues as a *new* table on the next slide, opening with its `thead` again.
  A table with a `rowspan` or a table inside a cell has no place in a grid of simple rows and is drawn as boxes
  and runs instead.
- **A block of text is one text box** holding its paragraphs, wrapping at the width of the block; the line
  spacing is the line height the page used, and the space between paragraphs is what the page left between them.
  A block whose lines carry pictures or painted inline boxes keeps its runs placed one by one, each a text box
  with wrapping off where the engine set it.
- **Running lines go onto the slide layout.** The header and footer are drawn once and shown on every slide
  made from them; `{page}` becomes PowerPoint's own slide number, which follows the slides as they are moved.
  PowerPoint has no field for the number of slides, so `{pages}` stays the number written, as text of the
  layout — corrected in one place (View → Slide Master) if the deck grows.
- A border side drawn on its own — a rule under a heading — is a line, not a thin rectangle.
- Pictures are stored once however many slides show them, cropped where `object-fit` cropped them.
- Every slide states its own background — the sheet colour, or white — so nothing depends on the reader's
  system colours, and the running lines of the layout stay visible on top of it.
- The faces travel inside the deck, grouped by family (regular/bold/italic/bold-italic), and only the faces the
  text actually uses. Only TrueType can travel — a CFF `.otf` is named in the notes instead. Set
  `EmbedFonts = false` when a licence forbids passing a face on, or when every reader has the fonts anyway.
- `Typefaces` names a family after another face in the deck: measure in Arimo, which is drawn to Arial's
  widths, and hand the reader `Arial` — `Typefaces = new Dictionary<string, string> { ["Arimo"] = "Arial" }`.
  A renamed family is not embedded; its text asks for the face every reader already has.

**Caveats worth knowing before you ship.** A deck is an *editable document*, not a proof:

- **Keynote ignores embedded fonts** and substitutes as though the file carried none. This was measured, not
  assumed. If the setting has to survive everywhere, ship a PDF.
- A slide places a text box by its top edge while the engine places text on its baseline, so the top is derived
  from the ascent of the measured face — exact for that face, approximate for a substitute.
- PowerPoint and Keynote each import with their own engine. Neither is this one.
- **A deck has one slide size.** Sections of different sheets are set on the first one's slide, and a
  `FormatLimit` note says so; PowerPoint's own limits on font size, line spacing and slide size are kept to the
  same way.

---

## API reference

Both converters offer the same four operations, and every one of them takes a `CancellationToken` that is
checked between elements and between pages.

| Method | Returns | Use it when |
| --- | --- | --- |
| `Convert(html, setup?, ct)` | `byte[]` | You just want the bytes. |
| `Convert(sections, ct)` | `byte[]` | The document has a cover, or several sheet setups. |
| `Write(html, stream, setup?, ct)` | `Composition` | You want to stream to a file/response *and* read the notes. |
| `Write(sections, stream, ct)` | `Composition` | Both of the above. |
| `Typeset(html, setup?, ct)` | `Composition` | Lay out without writing — e.g. to count pages, or to inspect notes before committing. |
| `Render(composition, stream, ct)` | `void` | Write pages you laid out earlier. |

```csharp
// Count the pages before deciding anything
Composition draft = pdf.Typeset(html, PageSetup.A4, ct);
if (draft.Pages.Count > 50) return TooLong();

pdf.Render(draft, response.Body, ct);
```

`Composition` is `Pages` + `Notes`. A `Page` carries `Boxes`, `Images` and `Spans`, already in paint order,
every coordinate in points from the top-left corner.

### Cancellation

```csharp
Composition composition = pdf.Write(html, stream, PageSetup.A4, ct);
// OperationCanceledException if ct fires — what reached the stream by then is not a document
```

Nothing reaches the stream until the whole document is drawn: the PDF is written into memory first and copied
out at the end. A stream that fails — closed, read-only, a client gone — throws its own `IOException` from
`Write` and leaves the process standing.

### Limits on what a document may ask

Markup can come from anyone, so the engine sets bounds on what it will do for one document. Past each one the
document is still written, and a note says what was given up:

| Limit | Past it |
| --- | --- |
| Elements nested 256 deep | The words below are set as plain text in their block; `NestingCapped`. |
| A length of 1 000 000 units, a font size of 3 000 points | Cut to the limit; a value that is not a finite number is not read. |
| A raster of 50 000 000 pixels (built-in picture sources) | Left out like an unreadable picture; `MissingPicture`. |
| An SVG 256 deep or of 20 000 drawn elements | The rest is undrawn; `NestingCapped`. |
| Roman list numbers past 3 999 | Written in figures, as a browser does. |
| A `colspan`/`rowspan` of more than 1 000 | Capped at 1 000; `SpanCapped`. |

A sheet that is not a finite size is refused with an `ArgumentException`: that is a caller's mistake, not a
document's.

---

## Linux and containers

`SkiaSharp.NativeAssets.Linux.NoDependencies` ships with the Fonts package, so a container needs **neither
fontconfig nor any installed fonts**. Every face comes from your `FontLibrary`. A plain
`mcr.microsoft.com/dotnet/aspnet` image works as-is.

---

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Blank pages, or squares instead of letters | No fonts loaded. Check for a `NoFonts` note, then `AddDirectory`/`AddResource`. |
| Text is in the wrong typeface | That family is not loaded; the first loaded family took over. Check `FamilySubstituted` notes and `fonts.Families`. |
| Bold text does not look bold | No bold face loaded for that family — there is no synthetic bold. Check `BoldSubstituted`. |
| Cyrillic (or any script) prints as crossed boxes | The chosen face has no glyphs for it. Check `MissingGlyph` notes; there is no per-glyph fallback. |
| PDF text cannot be searched or copied | A CFF `.otf` face — load the `.ttf` version. Check `CffOutlines`. |
| Images are missing | `data:`/local files only, never the network; the path must sit under `BaseDirectory`. Check `MissingPicture`. |
| Columns are stacked instead of side by side | Flex and grid are laid out as plain blocks. Use a table. |
| Header/footer overlaps the text | The band is as tall as the margin and nothing is clipped — widen the margin. |
| `ObjectDisposedException` from the font library | The library was disposed while a conversion was still running, or a `using` closed too early. |

---

## How it works

1. **Parse.** AngleSharp builds the DOM; AngleSharp.Css parses stylesheets, expands shorthands and matches
   selectors — and that is all it is trusted with.
2. **Style.** The engine runs the cascade itself (user agent, author, inline, `!important`) and computes values:
   units to points, `em`, `rem`, percentages, keywords, inheritance.
3. **Galley.** Blocks get their final widths; inline content is broken into lines, every width measured by the
   renderer's own fonts.
4. **Paginate.** The galley is cut into pages between lines and between blocks, honouring forced breaks,
   `break-inside: avoid`, orphans and widows.
5. **Render.** A renderer draws each page: backgrounds and borders, then pictures, then text.

The engine contains no line of PDF or PowerPoint. It knows exactly two seams:

- **`ITextMeasurer`** — measures text with the very fonts that will draw it. A width taken from one face and
  drawn with another puts every line break in the wrong place.
- **`IImageSource`** — reads a picture and its natural size. The engine only reserves room; decoding belongs to
  the renderer.

### Writing your own renderer

Take `Engine` + `Fonts` and inherit `Converter<TOptions>`. You get the whole pipeline; you supply the writing.

```csharp
public sealed record SvgOptions : DocumentOptions;

public sealed class HtmlToSvg(FontLibrary fonts, SvgOptions? options = null)
    : Converter<SvgOptions>(fonts, options ?? new SvgOptions())
{
    protected override void Write(Composition composition, Stream output, Faces faces, CancellationToken ct)
    {
        // draw composition.Pages; faces.For(span.Font) is the font object the run was measured with.
        // A format that embeds the faces it draws with draws with faces.Subset(composition) instead.
    }

    // optional: a note for whatever your format cannot carry
    protected override IEnumerable<Note> NotesFor(Composition composition) => [];
}
```

What a writer receives, per page:

- **`RenderBox`** — a rectangle with a background and a border. `Kind` says what it is: `Sheet` (the sheet
  colour), `Marker` (a list bullet), `Inline` (a run's box), `Element` (whose tag name is then in `Element`).
  A box cut by a page boundary carries `IsSplitAbove`/`IsSplitBelow` and draws no rule on that side.
- **`ImageBox`** — a picture and its frame, with `Source` naming the visible part when `object-fit` cropped it.
- **`TextSpan`** — one run at `X` on its `Baseline`, with its `Font`, colour, decoration and measured width.
  `Field` marks the runs a running line wrote for `{page}` and `{pages}`.

And, for a writer whose own objects reflow — a deck, a word processor — the same page told as structure. Every
box, run and picture named here is also in the lists above, so a writer that uses it skips what it has drawn:

- **`Texts`** — blocks of text as `TextFrame`s: the block's content box and its paragraphs, each line with its
  runs and whether it wrapped at a space.
- **`Tables`** — tables as `TableFrame`s: column widths, rows, and cells with their span, padding, alignment,
  the rule on each horizontal edge, their paragraphs and a picture that fills them.
- **`Running`** — what the header, footer and side note put on the page, told apart from the content.

Telling the page as structure costs time and memory, so the engine does it only for a writer that asks:
override `IsStructured` to return `true`. Otherwise `Texts` and `Tables` stay empty. A table drawn as one object
of its own covers what lies inside its cells; `TableFrame.CellBoxes` and `CellImages` are those boxes and
pictures, for drawing after the table.

```csharp
protected override bool IsStructured => true;
```

---

## Repository

```
src/Engine             → Snail.Toolkit.HtmlComposer.Engine
src/Fonts              → Snail.Toolkit.HtmlComposer.Fonts
src/Pdf                → Snail.Toolkit.HtmlComposer.Pdf
src/Pptx               → Snail.Toolkit.HtmlComposer.Pptx
tests/Engine.Tests     → layout arithmetic against a fixed-pitch measurer
tests/Fonts.Tests      → the font library and the picture sources
tests/Pdf.Tests        → real PDFs read back with PdfPig
tests/Pptx.Tests       → real decks read back with the Open XML SDK
tests/Documents.Tests  → whole documents rendered both ways — the corpus a change is checked against
tests/Assets           → PT Sans, the face the tests set documents in, under the OFL
benchmarks/Documents.Benchmarks → the same corpus timed end to end
```

Project folders are short so the solution reads well in an IDE; the names that reach a consumer (`PackageId`,
`AssemblyName`, `RootNamespace`) are derived from the folder name in `Directory.Build.props`. Package versions
are pinned centrally in `Directory.Packages.props`.

### Building

```bash
dotnet build -warnaserror
dotnet test
```

The build is the gate: warnings are errors, the `.editorconfig` style rules are enforced by the compiler, and
every project generates documentation, so no public member may go undocumented. The document tests render two
synthetic documents both ways and hold them to a known result — the report on three pages, the quotation on
two, no notes, every deck valid Open XML — leaving the files under `out` beside the test assembly so you can
open them after a run.

```bash
dotnet run -c Release --project benchmarks/Documents.Benchmarks -- --filter '*' --job short
```

Weigh any change to the cascade, the line breaker or the paginator against these before and after.

### Packaging

```bash
dotnet pack                            # 0.3.0
dotnet pack -p:VersionSuffix=local     # 0.3.0-local, for a local feed
```

The version is a *prefix* in `Directory.Build.props`, so a suffix reaches the package. A release is a tag:
pushing `vX.Y.Z` runs `.github/workflows/publish.yml`, which restores, builds and tests in Release, packs every
package at the version the tag names and pushes them to NuGet.

## License

MIT — see [LICENSE](LICENSE). PT Sans, the face the tests set documents in, is by ParaType under the SIL Open
Font License 1.1 (`tests/Assets/OFL.txt`).
