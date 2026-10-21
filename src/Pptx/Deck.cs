using System.Security.Cryptography;
using System.Text;
using System.Xml;
using SkiaSharp;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Snail.Toolkit.HtmlComposer.Engine;
using Snail.Toolkit.HtmlComposer.Fonts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Writes composed pages into a PowerPoint deck, one slide per page.</summary>
/// <remarks>
/// A plain reader of the composition, like the PDF press, but one that writes PowerPoint's own objects wherever
/// the engine can say what a thing is: a table becomes a table, a block of text becomes one text box holding
/// its paragraphs, and the running header and footer go onto the slide layout, with PowerPoint's slide number
/// where the page number stood. A deck is a document people edit — a price changed in a cell stays in its
/// cell, a line added to a paragraph wraps inside its box. Only what the engine cannot give back whole is
/// placed run by run, where it was set.
/// <para>
/// A placed run is a text box of its own with wrapping off, positioned by its top edge while the engine
/// places text on its baseline: the top is the baseline less the ascent of the face it was measured in. That
/// is exact for that face and approximate for anything PowerPoint substitutes.
/// </para>
/// </remarks>
internal static partial class Deck
{
    private const long EmuPerPoint = 12700;

    private const int Hundredths = 100;

    private const int PerCent = 100000;

    /// <summary>Slack on the width of a placed run, so a face wider than the measured one is not clipped.</summary>
    private const float Slack = 1.15f;

    public static void Write(Composition composition, Stream output, Faces fonts, FontLibrary library, PptxOptions options, CancellationToken ct)
    {
        long start = output.CanSeek ? output.Position : -1;

        Package(composition, output, fonts, library, options, ct);

        if (start >= 0 && output.CanRead)
        {
            Settle(output, start);
        }
    }

    private static void Package(Composition composition, Stream output, Faces fonts, FontLibrary library, PptxOptions options, CancellationToken ct)
    {
        using var document = PresentationDocument.Create(output, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        PresentationPart presentationPart = document.AddNewPart<PresentationPart>(PresentationContentType, NextId(document));

        presentationPart.Presentation = new P.Presentation();

        SlideMasterPart masterPart = Skeleton(presentationPart);
        var ink = new Ink(new Typefaces(fonts, options.Typefaces), new Pictures(fonts, ct), options.Language);
        var layouts = new Layouts(masterPart, ink);
        var slides = new P.SlideIdList();
        uint number = 256;

        foreach (Page page in composition.Pages)
        {
            ct.ThrowIfCancellationRequested();

            SlidePart slidePart = presentationPart.AddNewPart<SlidePart>(NextId(presentationPart));
            Layout layout = layouts.For(page);

            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(TreeOf(page, layout, slidePart, ink)) { Background = Paper(SheetOf(page)) },
                new P.ColorMapOverride(new A.MasterColorMapping()));

            slidePart.AddPart(layout.Part, NextId(slidePart));
            slides.Append(new P.SlideId { Id = number++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }

        (float, float) sheet = composition.Pages.Count > 0 ? (composition.Pages[0].Width, composition.Pages[0].Height) : (595f, 842f);

        int across = SlideSide(sheet.Item1);
        int down = SlideSide(sheet.Item2);

        presentationPart.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(masterPart) }),
            slides,
            new P.SlideSize { Cx = across, Cy = down },
            new P.NotesSize { Cx = down, Cy = across });

        if (options.EmbedFonts)
        {
            Embed(presentationPart, composition, library, options.Typefaces);
        }

        Styles(presentationPart);
        Describe(document, options);
        presentationPart.Presentation.Save();
    }

    /// <summary>Carries the faces the document is set in inside the deck, so a reader without them keeps the setting.</summary>
    /// <remarks>
    /// Only the faces the text actually uses, so a deck is not fattened by every face the application loaded,
    /// and only TrueType, which is all the format can carry. One entry per family holds up to four faces —
    /// regular, bold, italic and both — because that is how a reader looks a face up: by family and by what
    /// the run asks of it. A family whose faces cannot travel is named in the notes instead, by the converter.
    /// A family the deck names after another face is not carried: its text asks for the other face, which the
    /// reader is expected to have.
    /// </remarks>
    private static void Embed(PresentationPart presentationPart, Composition composition, FontLibrary library, IReadOnlyDictionary<string, string> renamed)
    {
        var used = composition.Pages.SelectMany(page => page.Spans).Select(span => span.Font).Distinct().ToList();
        var faces = library.FacesOf(used).Where(face => !renamed.ContainsKey(face.Family)).ToList();

        if (faces.Count == 0)
        {
            return;
        }

        var carried = new P.EmbeddedFontList();

        foreach (IGrouping<string, FontLibrary.EmbeddedFace> family in faces.GroupBy(face => face.Family, StringComparer.OrdinalIgnoreCase))
        {
            var entry = new P.EmbeddedFont(new P.Font { Typeface = family.Key });

            foreach (FontLibrary.EmbeddedFace face in family)
            {
                Slot(entry, face, Stored(presentationPart, face.Content));
            }

            carried.Append(entry);
        }

        presentationPart.Presentation!.Append(carried);
    }

    /// <summary>The relationship id of a face written into the package as its own part.</summary>
    private static string Stored(PresentationPart presentationPart, byte[] content)
    {
        FontPart part = presentationPart.AddFontPart(FontPartType.FontData, NextId(presentationPart));

        using var bytes = new MemoryStream(content);
        part.FeedData(bytes);

        return presentationPart.GetIdOfPart(part);
    }

    /// <summary>Puts a face in the slot the reader will look for it in, leaving a slot already taken alone.</summary>
    private static void Slot(P.EmbeddedFont entry, FontLibrary.EmbeddedFace face, string id)
    {
        switch (face)
        {
            case { IsBold: true, IsItalic: true } when entry.BoldItalicFont is null:
                entry.BoldItalicFont = new P.BoldItalicFont { Id = id };
                break;
            case { IsBold: true, IsItalic: false } when entry.BoldFont is null:
                entry.BoldFont = new P.BoldFont { Id = id };
                break;
            case { IsBold: false, IsItalic: true } when entry.ItalicFont is null:
                entry.ItalicFont = new P.ItalicFont { Id = id };
                break;
            case { IsBold: false, IsItalic: false } when entry.RegularFont is null:
                entry.RegularFont = new P.RegularFont { Id = id };
                break;
        }
    }

    /// <summary>The table styles of the deck, with the plain one as the default.</summary>
    /// <remarks>
    /// Every table here is drawn by its own cells — their rules, their fills — so the style it names has to add
    /// nothing: no banded rows, no coloured heading. PowerPoint knows the plain style by its id; the part only
    /// says it is the one a new table starts from.
    /// </remarks>
    private static void Styles(PresentationPart presentationPart)
    {
        TableStylesPart part = presentationPart.AddNewPart<TableStylesPart>(NextId(presentationPart));

        part.TableStyleList = new A.TableStyleList { Default = PlainTable };
    }

    private static string? Printed(string? text) => text is null ? null : Printable(text);

    /// <summary>The title, author, subject and keywords, in the part every reader takes them from.</summary>
    /// <remarks>
    /// Written as <c>docProps/core.xml</c> under a counted id, as PowerPoint writes it. Set through the package's
    /// own properties they went into a part named by a fresh GUID on every run, and the same document never
    /// gave the same deck twice.
    /// </remarks>
    private static void Describe(PresentationDocument document, PptxOptions options)
    {
        (string Prefix, string Name, string? Value)[] properties =
        [
            ("dc", "title", Printed(options.Title)),
            ("dc", "creator", Printed(options.Author)),
            ("dc", "subject", Printed(options.Subject)),
            ("cp", "keywords", Printed(options.Keywords)),
        ];

        if (properties.All(property => property.Value is null))
        {
            return;
        }

        CoreFilePropertiesPart part = document.AddNewPart<CoreFilePropertiesPart>(NextId(document));
        using Stream stream = part.GetStream(FileMode.Create);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });

        writer.WriteStartElement("cp", "coreProperties", CoreProperties);
        writer.WriteAttributeString("xmlns", "dc", null, DublinCore);

        foreach ((string prefix, string name, string? value) in properties.Where(property => property.Value is not null))
        {
            writer.WriteElementString(prefix, name, prefix == "dc" ? DublinCore : CoreProperties, value);
        }

        writer.WriteEndElement();
    }

    /// <summary>The master, the blank layout and the theme a deck cannot be opened without.</summary>
    /// <remarks>
    /// Every slide carries its own shapes at their own positions, so the master holds no placeholders and the
    /// first layout is the blank one, for pages without running lines. The theme is still required by the
    /// format, and PowerPoint refuses a deck whose master has none.
    /// </remarks>
    private static SlideMasterPart Skeleton(PresentationPart presentationPart)
    {
        SlideMasterPart masterPart = presentationPart.AddNewPart<SlideMasterPart>(NextId(presentationPart));
        SlideLayoutPart layoutPart = masterPart.AddNewPart<SlideLayoutPart>(NextId(masterPart));
        ThemePart themePart = masterPart.AddNewPart<ThemePart>(NextId(masterPart));

        layoutPart.AddPart(masterPart, NextId(layoutPart));
        layoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(Tree()),
            new P.ColorMapOverride(new A.MasterColorMapping()))
        {
            Type = P.SlideLayoutValues.Blank,
        };

        masterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(Tree()),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(new P.SlideLayoutId
            {
                Id = FirstLayoutId,
                RelationshipId = masterPart.GetIdOfPart(layoutPart),
            }));

        themePart.Theme = Theme();

        return masterPart;
    }

    /// <summary>The Office theme every deck carries: the master and the layouts name its colours and fonts.</summary>
    private static A.Theme Theme()
    {
        var elements = new A.ThemeElements(Colours(), Fonts(), Formats());
        var theme = new A.Theme(elements, new A.ObjectDefaults(), new A.ExtraColorSchemeList()) { Name = "Office Theme" };

        return theme;
    }

    private static A.ColorScheme Colours() => new(
        new A.Dark1Color(new A.RgbColorModelHex { Val = "000000" }),
        new A.Light1Color(new A.RgbColorModelHex { Val = "FFFFFF" }),
        new A.Dark2Color(new A.RgbColorModelHex { Val = "1F497D" }),
        new A.Light2Color(new A.RgbColorModelHex { Val = "EEECE1" }),
        new A.Accent1Color(new A.RgbColorModelHex { Val = "4F81BD" }),
        new A.Accent2Color(new A.RgbColorModelHex { Val = "C0504D" }),
        new A.Accent3Color(new A.RgbColorModelHex { Val = "9BBB59" }),
        new A.Accent4Color(new A.RgbColorModelHex { Val = "8064A2" }),
        new A.Accent5Color(new A.RgbColorModelHex { Val = "4BACC6" }),
        new A.Accent6Color(new A.RgbColorModelHex { Val = "F79646" }),
        new A.Hyperlink(new A.RgbColorModelHex { Val = "0000FF" }),
        new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "800080" }))
    {
        Name = "Office",
    };

    private static A.FontScheme Fonts() => new(
        new A.MajorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
        new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }))
    {
        Name = "Office",
    };

    private static A.FormatScheme Formats() => new(
        new A.FillStyleList(Solid(), Solid(), Solid()),
        new A.LineStyleList(Outline(), Outline(), Outline()),
        new A.EffectStyleList(Effect(), Effect(), Effect()),
        new A.BackgroundFillStyleList(Solid(), Solid(), Solid()))
    {
        Name = "Office",
    };

    private static A.SolidFill Solid() => new(new A.SchemeColor { Val = A.SchemeColorValues.PhColor });

    private static A.Outline Outline() => new(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 9525 };

    private static A.EffectStyle Effect() => new(new A.EffectList());

    /// <summary>The paper a slide is set on, stated in the slide itself.</summary>
    /// <remarks>
    /// A slide that states no background takes the master's, and the master's comes from a theme colour. A
    /// theme colour named for the system palette — "window" and "windowText" — is resolved by the viewer, and
    /// a Mac in dark mode resolves it to black: a document of dark grey text on white paper then arrives as
    /// dark grey on black, which is how this was found. Paper is white unless the document paints it, and the
    /// sheet colour the engine emits is a box of its own that covers this.
    /// </remarks>
    /// <remarks>
    /// The sheet's own colour is the slide's background rather than a shape the size of the slide: the running
    /// lines live on the layout, beneath every shape of the slide, and a sheet drawn as a shape covered the
    /// header, the footer and the slide number of every page it coloured.
    /// </remarks>
    private static P.Background Paper(Rgba? sheet) =>
        new(new P.BackgroundProperties(new A.SolidFill(sheet is { } colour ? Colour(colour) : new A.RgbColorModelHex { Val = "FFFFFF" }), new A.EffectList()));

    private static Rgba? SheetOf(Page page) =>
        page.Boxes.FirstOrDefault(box => box.Kind == BoxKind.Sheet)?.Background;

    /// <summary>The smallest and the largest a slide side may be, in EMU: one inch and fifty-six.</summary>
    private const int SmallestSlide = 914400;

    private const int LargestSlide = 51206400;

    /// <summary>A side of the slide in EMU, within what the format allows; the converter notes a sheet it had to change.</summary>
    internal static int SlideSide(float points) => (int)Math.Clamp(Emu(points), SmallestSlide, LargestSlide);

    /// <summary>A font size in hundredths of a point, within the 1 to 4 000 points the format allows.</summary>
    /// <remarks>A run of <c>font-size:0</c> — common in markup that hides a gap — wrote a size the schema refuses, and PowerPoint offers to repair such a file.</remarks>
    private static int SizeOf(float points) => Math.Clamp((int)Math.Round(points * Hundredths), 100, 400000);

    /// <summary>A spacing in hundredths of a point, within the 0 to 1 584 points the format allows.</summary>
    private static int SpacingOf(float points) => Math.Clamp((int)Math.Round(points * Hundredths), 0, 158400);

    private static P.ShapeTree Tree() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new A.TransformGroup()));

    /// <summary>One page as a slide: what the layout does not already show, in the order the page paints it.</summary>
    /// <remarks>
    /// Boxes go first and text last, as CSS paints normal flow. A box, run or picture that a table or a text
    /// frame already holds is left out, as is everything the running lines put on the page: those are drawn
    /// by the table, the frame and the layout.
    /// </remarks>
    private static P.ShapeTree TreeOf(Page page, Layout layout, OpenXmlPart part, Ink ink)
    {
        ShapeTree tree = Tree();
        var held = Held(page);
        uint id = 2;

        if (layout.HasSlideNumber)
        {
            tree.Append(SlideNumber(page.Number, id++));
        }

        foreach (RenderBox box in page.Boxes)
        {
            if (box.IsPainted && box.Kind != BoxKind.Sheet && !held.Contains(box))
            {
                Append(tree, ShapesOf(box, ref id));
            }
        }

        foreach (ImageBox image in page.Images)
        {
            if (!held.Contains(image))
            {
                tree.Append(PictureOf(image, part, ink.Pictures, id++));
            }
        }

        foreach (TableFrame table in page.Tables)
        {
            tree.Append(TableOf(table, part, ink, id++));
            Overlay(tree, table, part, ink, ref id);
        }

        foreach (TextFrame text in page.Texts)
        {
            tree.Append(TextBoxOf(text, ink, id++));
        }

        foreach (TextSpan span in page.Spans)
        {
            if (!held.Contains(span))
            {
                tree.Append(SpanOf(span, ink, id++));
            }
        }

        return tree;
    }

    /// <summary>What the cells of a table hold beyond their text, drawn over the table as it lies over the cells on the page.</summary>
    private static void Overlay(ShapeTree tree, TableFrame table, OpenXmlPart part, Ink ink, ref uint id)
    {
        foreach (RenderBox box in table.CellBoxes)
        {
            if (box.IsPainted)
            {
                Append(tree, ShapesOf(box, ref id));
            }
        }

        foreach (ImageBox image in table.CellImages)
        {
            tree.Append(PictureOf(image, part, ink.Pictures, id++));
        }
    }

    private static void Append(ShapeTree tree, IEnumerable<DocumentFormat.OpenXml.OpenXmlElement> shapes)
    {
        foreach (DocumentFormat.OpenXml.OpenXmlElement shape in shapes)
        {
            tree.Append(shape);
        }
    }

    /// <summary>Everything on a page that something other than its own shape draws: tables, text frames and the running lines.</summary>
    private static HashSet<object> Held(Page page)
    {
        var held = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (TableFrame table in page.Tables)
        {
            held.UnionWith(table.Boxes);
            held.UnionWith(table.CellBoxes);
            held.UnionWith(table.CellImages);

            foreach (Engine.TableCell cell in table.Rows.SelectMany(row => row.Cells))
            {
                held.UnionWith(cell.Paragraphs.SelectMany(paragraph => paragraph.Lines).SelectMany(line => line.Spans));

                if (cell.Picture is not null)
                {
                    held.Add(cell.Picture);
                }
            }
        }

        held.UnionWith(page.Texts.SelectMany(text => text.Paragraphs).SelectMany(paragraph => paragraph.Lines).SelectMany(line => line.Spans));

        if (page.Running is { } running)
        {
            held.UnionWith(running.Boxes);
            held.UnionWith(running.Images);
            held.UnionWith(running.Spans);
        }

        return held;
    }

    /// <summary>One box as shapes: its background, and a line for each side it draws.</summary>
    /// <remarks>
    /// A shape in a deck carries one outline for all four sides, but the engine draws sides on their own — a
    /// rule under a heading is a bottom border and nothing else. Each visible side therefore becomes a line of
    /// its own, drawn through the middle of the band the border occupies, which is what someone editing the
    /// deck expects to find under a heading: a line, not a very thin rectangle.
    /// </remarks>
    private static List<DocumentFormat.OpenXml.OpenXmlElement> ShapesOf(RenderBox box, ref uint id)
    {
        List<DocumentFormat.OpenXml.OpenXmlElement> shapes = [];
        Rect bounds = box.Bounds;

        if (box.Background is { IsVisible: true } background)
        {
            shapes.Add(Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, background, id++, box.Radius));
        }

        Border border = box.Border;

        if (border.Top.IsVisible)
        {
            float y = bounds.Y + (border.Top.Width / 2);
            shapes.Add(Line(bounds.X, y, bounds.Right, y, border.Top, id++));
        }

        if (border.Bottom.IsVisible)
        {
            float y = bounds.Bottom - (border.Bottom.Width / 2);
            shapes.Add(Line(bounds.X, y, bounds.Right, y, border.Bottom, id++));
        }

        if (border.Left.IsVisible)
        {
            float x = bounds.X + (border.Left.Width / 2);
            shapes.Add(Line(x, bounds.Y, x, bounds.Bottom, border.Left, id++));
        }

        if (border.Right.IsVisible)
        {
            float x = bounds.Right - (border.Right.Width / 2);
            shapes.Add(Line(x, bounds.Y, x, bounds.Bottom, border.Right, id++));
        }

        return shapes;
    }

    private static P.Shape Rectangle(float x, float y, float width, float height, Rgba fill, uint id, float radius)
    {
        PresetGeometry geometry = radius > 0
            ? new A.PresetGeometry(Adjusted(radius, width, height)) { Preset = A.ShapeTypeValues.RoundRectangle }
            : new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle };

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"box{id}" },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(x), Y = Emu(y) },
                    new A.Extents { Cx = Emu(width), Cy = Emu(height) }),
                geometry,
                new A.SolidFill(Colour(fill)),
                new A.Outline(new A.NoFill())),
            new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
    }

    /// <summary>A straight line from one point to another, as thick and as coloured as the side it draws.</summary>
    private static P.ConnectionShape Line(float x1, float y1, float x2, float y2, BorderSide side, uint id)
    {
        var outline = new A.Outline(new A.SolidFill(Colour(side.Color))) { Width = (int)Emu(side.Width) };

        if (side.Style != BorderStyle.Solid)
        {
            outline.Append(new A.PresetDash { Val = side.Style == BorderStyle.Dashed ? A.PresetLineDashValues.Dash : A.PresetLineDashValues.SystemDot });
        }

        return new P.ConnectionShape(
            new P.NonVisualConnectionShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"rule{id}" },
                new P.NonVisualConnectorShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(x1), Y = Emu(y1) },
                    new A.Extents { Cx = Emu(x2 - x1), Cy = Emu(y2 - y1) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Line },
                outline));
    }

    /// <summary>A rounded corner is stated as a share of the shorter side, which is what the preset expects.</summary>
    private static A.AdjustValueList Adjusted(float radius, float width, float height)
    {
        float shorter = Math.Min(width, height);
        int value = shorter <= 0 ? 0 : (int)Math.Round(Math.Min(0.5f, radius / shorter) * PerCent);

        return new A.AdjustValueList(new A.ShapeGuide { Name = "adj", Formula = $"val {value}" });
    }

    private static P.Picture PictureOf(ImageBox box, OpenXmlPart part, Pictures pictures, uint id)
    {
        var fill = new P.BlipFill(new A.Blip { Embed = pictures.IdOf(box.Image, part) });

        if (box.Source is { } source)
        {
            fill.Append(Cropped(source, box.Image));
        }

        fill.Append(new A.Stretch(new A.FillRectangle()));

        return new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"picture{id}" },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            fill,
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(box.Bounds.X), Y = Emu(box.Bounds.Y) },
                    new A.Extents { Cx = Emu(box.Bounds.Width), Cy = Emu(box.Bounds.Height) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    /// <summary>The part of a picture that is seen, stated as what is cut from each edge.</summary>
    private static A.SourceRectangle Cropped(Rect source, Image image) => new()
    {
        Left = Share(source.X, image.Width),
        Top = Share(source.Y, image.Height),
        Right = Share(image.Width - source.Right, image.Width),
        Bottom = Share(image.Height - source.Bottom, image.Height),
    };

    private static int Share(float part, float whole) =>
        whole <= 0 ? 0 : Math.Clamp((int)Math.Round(part / whole * PerCent), 0, PerCent);

    /// <summary>One run placed where the engine set it, in a text box of its own that does not wrap.</summary>
    private static P.Shape SpanOf(TextSpan span, Ink ink, uint id)
    {
        (FontMetrics metrics, string face) = ink.Typefaces.Of(span.Font);
        var shape = new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"text{id}" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(span.X), Y = Emu(span.Baseline - metrics.Ascent) },
                    new A.Extents { Cx = Emu(span.Width * Slack), Cy = Emu(metrics.Ascent + metrics.Descent) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            new P.TextBody(
                Unwrapped(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties(new A.LineSpacing(new A.SpacingPercent { Val = PerCent })),
                    new A.Run(RunProperties(span, face, ink.Language), new A.Text(Printable(span.Text))))));

        return shape;
    }

    private static A.BodyProperties Unwrapped() => new()
    {
        Wrap = A.TextWrappingValues.None,
        LeftInset = 0,
        TopInset = 0,
        RightInset = 0,
        BottomInset = 0,
        Anchor = A.TextAnchoringTypeValues.Top,
    };

    /// <summary>What a run is set in, and the language it is proofed in when the options name one.</summary>
    private static A.RunProperties RunProperties(TextSpan span, string face, string? language)
    {
        var properties = new A.RunProperties(
            new A.SolidFill(Colour(span.Color)),
            new A.LatinFont { Typeface = face },
            new A.ComplexScriptFont { Typeface = face })
        {
            Dirty = false,
            FontSize = SizeOf(span.Font.Size),
            Bold = span.Font.Weight >= 600,
            Italic = span.Font.IsItalic,
            Underline = span.Decoration.HasFlag(TextDecoration.Underline)
                ? A.TextUnderlineValues.Single
                : A.TextUnderlineValues.None,
            Strike = span.Decoration.HasFlag(TextDecoration.LineThrough)
                ? A.TextStrikeValues.SingleStrike
                : A.TextStrikeValues.NoStrike,
        };

        if (language is not null)
        {
            properties.Language = language;
        }

        if (span.Font.Tracking != 0)
        {
            properties.Spacing = (int)Math.Round(span.Font.Tracking * Hundredths);
        }

        return properties;
    }

    private static A.RgbColorModelHex Colour(Rgba colour)
    {
        var hex = new A.RgbColorModelHex { Val = $"{colour.R:X2}{colour.G:X2}{colour.B:X2}" };

        if (colour.A < byte.MaxValue)
        {
            hex.Append(new A.Alpha { Val = (int)Math.Round(colour.A / (float)byte.MaxValue * PerCent) });
        }

        return hex;
    }

    /// <summary>What kind of picture the bytes hold, read from the first of them.</summary>
    /// <remarks>The engine hands the file over untouched, so the type is whatever the markup supplied.</remarks>
    private static PartTypeInfo TypeOf(byte[] content) => content switch
    {
        [0xFF, 0xD8, ..] => ImagePartType.Jpeg,
        [(byte)'G', (byte)'I', (byte)'F', ..] => ImagePartType.Gif,
        [(byte)'B', (byte)'M', ..] => ImagePartType.Bmp,
        _ => ImagePartType.Png,
    };

    /// <summary>A picture in a format every reader of a deck opens: the bytes as they are, or the pixels again as PNG.</summary>
    /// <remarks>
    /// The engine reads whatever Skia decodes — WebP, ICO, and more — and a deck stored such a picture under a
    /// PNG content type, which a reader then shows as a broken picture. JPEG, PNG, GIF and BMP travel as they
    /// came; anything else is decoded and written again as PNG.
    /// </remarks>
    private static byte[] Carried(byte[] content)
    {
        bool isKnown = content is [0xFF, 0xD8, ..] or [(byte)'G', (byte)'I', (byte)'F', ..] or [(byte)'B', (byte)'M', ..] or [0x89, (byte)'P', (byte)'N', (byte)'G', ..];

        if (isKnown)
        {
            return content;
        }

        using SKImage? decoded = SKImage.FromEncodedData(content);
        using SKData? png = decoded?.Encode(SKEncodedImageFormat.Png, 100);

        return png?.ToArray() ?? content;
    }

    private static long Emu(float points) => (long)Math.Round(points * EmuPerPoint);

    /// <summary>What every part of one deck is drawn with: its faces, its pictures and the language of its text.</summary>
    private sealed record Ink(Typefaces Typefaces, Pictures Pictures, string? Language);

    /// <summary>The name and the height of each font of one deck, asked of the faces once per font.</summary>
    /// <remarks>
    /// A typeface tells its family name through Skia, as a native string the collector has to finalize, and the
    /// deck asked for it on every run: 1 894 names, and as many finalizers, for forty pages set in one font.
    /// Neither the name nor the metrics change within a document, so each font is asked once. A family the
    /// options rename is named as they say, so the text asks the reader for that face.
    /// </remarks>
    private sealed class Typefaces(Faces fonts, IReadOnlyDictionary<string, string> renamed)
    {
        private readonly Dictionary<Engine.Font, (FontMetrics Metrics, string Face)> _known = [];

        public (FontMetrics Metrics, string Face) Of(Engine.Font font)
        {
            if (_known.TryGetValue(font, out (FontMetrics Metrics, string Face) known))
            {
                return known;
            }

            string family = fonts.For(font).Typeface.FamilyName;

            known = (fonts.MetricsOf(font), renamed.TryGetValue(family, out string? name) ? name : family);
            _known[font] = known;

            return known;
        }
    }

    /// <summary>Every picture of one deck, stored once however many slides show it.</summary>
    /// <remarks>
    /// A logo in a running head is read again for every page, so the same bytes arrive as a different object
    /// each time: the pictures are therefore held by what they contain, not by which object carries them.
    /// Parts after the first are related to the part that already holds the bytes rather than being given a
    /// copy, which is the same reason the PDF press decodes each picture once per document.
    /// </remarks>
    private sealed class Pictures(Faces fonts, CancellationToken ct)
    {
        private const int PixelsPerUnit = 4;

        private const int LargestSide = 4096;

        private readonly Dictionary<string, ImagePart> _parts = [];

        private readonly Dictionary<(OpenXmlPart Owner, ImagePart Part), string> _ids = [];

        public string IdOf(Image image, OpenXmlPart owner)
        {
            string key = Convert.ToHexString(SHA256.HashData(image.Content));

            if (!_parts.TryGetValue(key, out ImagePart? part))
            {
                byte[] bytes = image.IsVector ? Rasterized(image) : Carried(image.Content);

                part = owner switch
                {
                    SlideLayoutPart layout => layout.AddImagePart(TypeOf(bytes), NextId(layout)),
                    _ => ((SlidePart)owner).AddImagePart(TypeOf(bytes), NextId(owner)),
                };

                using var content = new MemoryStream(bytes);
                part.FeedData(content);

                _parts[key] = part;
                _ids[(owner, part)] = owner.GetIdOfPart(part);
            }
            else if (!_ids.ContainsKey((owner, part)))
            {
                _ids[(owner, part)] = owner.CreateRelationshipToPart(part, NextId(owner));
            }

            return _ids[(owner, part)];
        }

        /// <summary>A drawing as PNG pixels, four to each of its natural pixels and no side longer than 4096.</summary>
        /// <remarks>
        /// A slide holds pictures, not shapes read from markup, so a drawing reaches the deck as pixels. Four to the
        /// pixel keeps its lines sharp on a slide shown full screen; the cap keeps a large drawing from costing
        /// tens of megabytes. The crop of the slide is stated in shares of the picture, so it holds at any scale.
        /// </remarks>
        private byte[] Rasterized(Image image)
        {
            float scale = Math.Min(PixelsPerUnit, (float)LargestSide / Math.Max(image.Width, image.Height));
            int width = (int)Math.Ceiling(image.Width * scale);
            int height = (int)Math.Ceiling(image.Height * scale);

            return Drawings.Rasterized(image, width, height, fonts, ct);
        }
    }
}
