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
| Tables that split across pages, `colspan`/`rowspan`, `border-collapse` | SVG and other vector pictures |
| No network calls, ever — safe to feed user-authored HTML | Fetching `http://` images for you |

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

Raster only (PNG, JPEG, …). An **SVG is not read** — it is replaced by its `alt` text with a note.

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

### Sizing and fitting

- A picture takes its natural size at 96 dpi, or whatever `width`/`height` (attribute or CSS) asks for, and is
  scaled down to fit the line and the page.
- `object-fit: fill` (the CSS default) stretches; `cover` keeps the frame and crops from the middle; `contain`
  keeps the whole picture and shrinks the frame to match.
- `vertical-align: middle` centres a picture against the text of its line — what an icon beside a heading needs.

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
`height` · `box-sizing` · `margin: 0 auto`

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
- **`colspan` / `rowspan`.** A cell spanning columns is measured last and only adds what those columns did not
  already ask for, so a wide heading does not dictate the shape of the table. A page never breaks inside a
  `rowspan`. (Spans are capped at 1000 — beyond that you get a `SpanCapped` note instead of an out-of-memory
  crash.)
- **`border-collapse: collapse`.** A cell gives up the rule its neighbour also draws, so a grid of 1 pt lines is
  1 pt thick everywhere instead of 2 pt between cells.
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
- **Pictures:** SVG and any other vector format.

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

**What to expect.**

- Each box becomes a shape; each run of text becomes its own text box with wrapping **off** — the engine has
  already broken the lines, and letting PowerPoint re-flow them would throw away the layout.
- Pictures are stored once however many slides show them, cropped where `object-fit` cropped them.
- Every slide states its own white background, so nothing depends on the reader's system colours.
- The faces travel inside the deck, grouped by family (regular/bold/italic/bold-italic), and only the faces the
  text actually uses. Only TrueType can travel — a CFF `.otf` is named in the notes instead. Set
  `EmbedFonts = false` when a licence forbids passing a face on, or when every reader has the fonts anyway.

**Caveats worth knowing before you ship.** A deck is an *editable document*, not a proof:

- **Keynote ignores embedded fonts** and substitutes as though the file carried none. This was measured, not
  assumed. If the setting has to survive everywhere, ship a PDF.
- A slide places a text box by its top edge while the engine places text on its baseline, so the top is derived
  from the ascent of the measured face — exact for that face, approximate for a substitute.
- PowerPoint and Keynote each import with their own engine. Neither is this one.

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
| Images are missing | `data:`/local files only, never the network; the path must sit under `BaseDirectory`; SVG is not read. Check `MissingPicture`. |
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
        // draw composition.Pages; faces.For(span.Font) is the font object the run was measured with
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
dotnet pack                            # 0.1.0
dotnet pack -p:VersionSuffix=local     # 0.1.0-local, for a local feed
```

The version is a *prefix* in `Directory.Build.props`, so a suffix reaches the package. A release is a tag:
pushing `vX.Y.Z` runs `.github/workflows/publish.yml`, which restores, builds and tests in Release, packs every
package at the version the tag names and pushes them to NuGet.

## License

MIT — see [LICENSE](LICENSE). PT Sans, the face the tests set documents in, is by ParaType under the SIL Open
Font License 1.1 (`tests/Assets/OFL.txt`).
