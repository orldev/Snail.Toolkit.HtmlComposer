using System.Security.Cryptography;
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
/// A plain reader of the composition, like the PDF press: every position was settled by the engine, and this
/// only turns boxes into shapes, pictures into pictures and runs into text boxes. Each run gets a text box of
/// its own with wrapping off, because the engine has already broken the lines — letting PowerPoint re-flow the
/// text would throw away the layout the deck is carrying.
/// <para>
/// A slide places a text box by its top edge while the engine places text on its baseline, so the top is the
/// baseline less the ascent of the face the run was measured in. That is exact for the face it was measured
/// with and approximate for anything PowerPoint substitutes: a deck is an editable document, not a proof.
/// </para>
/// </remarks>
internal static class Deck
{
    private const long EmuPerPoint = 12700;

    private const int Hundredths = 100;

    private const int PerCent = 100000;

    /// <summary>Slack on the width of a text box, so a face wider than the measured one is not clipped.</summary>
    private const float Slack = 1.15f;

    public static void Write(Composition composition, Stream output, Faces fonts, FontLibrary library, PptxOptions options, CancellationToken ct)
    {
        using var document = PresentationDocument.Create(output, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        PresentationPart presentationPart = document.AddPresentationPart();

        presentationPart.Presentation = new P.Presentation();

        SlideMasterPart masterPart = Skeleton(presentationPart);
        SlideLayoutPart layoutPart = masterPart.SlideLayoutParts.First();
        var slides = new P.SlideIdList();
        var pictures = new Pictures();
        var typefaces = new Typefaces(fonts);
        uint number = 256;

        foreach (Page page in composition.Pages)
        {
            ct.ThrowIfCancellationRequested();

            SlidePart slidePart = presentationPart.AddNewPart<SlidePart>();

            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(TreeOf(page, slidePart, typefaces, pictures, options.Language)) { Background = Paper() },
                new P.ColorMapOverride(new A.MasterColorMapping()));

            slidePart.AddPart(layoutPart);
            slides.Append(new P.SlideId { Id = number++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }

        (float, float) sheet = composition.Pages.Count > 0 ? (composition.Pages[0].Width, composition.Pages[0].Height) : (595f, 842f);

        presentationPart.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(masterPart) }),
            slides,
            new P.SlideSize { Cx = (int)Emu(sheet.Item1), Cy = (int)Emu(sheet.Item2) },
            new P.NotesSize { Cx = Emu(sheet.Item2), Cy = Emu(sheet.Item1) });

        if (options.EmbedFonts)
        {
            Embed(presentationPart, composition, library);
        }

        Describe(document, options);
        presentationPart.Presentation.Save();
    }

    /// <summary>Carries the faces the document is set in inside the deck, so a reader without them keeps the setting.</summary>
    /// <remarks>
    /// Only the faces the text actually uses, so a deck is not fattened by every face the application loaded,
    /// and only TrueType, which is all the format can carry. One entry per family holds up to four faces —
    /// regular, bold, italic and both — because that is how a reader looks a face up: by family and by what
    /// the run asks of it. A family whose faces cannot travel is named in the notes instead, by the converter.
    /// </remarks>
    private static void Embed(PresentationPart presentationPart, Composition composition, FontLibrary library)
    {
        var used = composition.Pages.SelectMany(page => page.Spans).Select(span => span.Font).Distinct().ToList();
        var faces = library.FacesOf(used).ToList();

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
        FontPart part = presentationPart.AddFontPart(FontPartType.FontData);

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

    private static void Describe(PresentationDocument document, PptxOptions options)
    {
        document.PackageProperties.Title = options.Title;
        document.PackageProperties.Creator = options.Author;
        document.PackageProperties.Subject = options.Subject;
        document.PackageProperties.Keywords = options.Keywords;
    }

    /// <summary>The master, layout and theme a deck cannot be opened without, all of them blank.</summary>
    /// <remarks>
    /// Every slide here carries its own shapes at their own positions, so the master holds no placeholders and
    /// the layout is the blank one. The theme is still required by the format, and PowerPoint refuses a deck
    /// whose master has none.
    /// </remarks>
    private static SlideMasterPart Skeleton(PresentationPart presentationPart)
    {
        SlideMasterPart masterPart = presentationPart.AddNewPart<SlideMasterPart>();
        SlideLayoutPart layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
        ThemePart themePart = masterPart.AddNewPart<ThemePart>();

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
                Id = 2147483649U,
                RelationshipId = masterPart.GetIdOfPart(layoutPart),
            }));

        themePart.Theme = Theme();

        return masterPart;
    }

    private static A.Theme Theme() => new(
        new A.ThemeElements(
            new A.ColorScheme(
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
            { Name = "Office" },
            new A.FontScheme(
                new A.MajorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
                new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }))
            { Name = "Office" },
            new A.FormatScheme(
                new A.FillStyleList(Solid(), Solid(), Solid()),
                new A.LineStyleList(Outline(), Outline(), Outline()),
                new A.EffectStyleList(Effect(), Effect(), Effect()),
                new A.BackgroundFillStyleList(Solid(), Solid(), Solid()))
            { Name = "Office" }),
        new A.ObjectDefaults(),
        new A.ExtraColorSchemeList())
    {
        Name = "Office Theme",
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
    private static P.Background Paper() =>
        new(new P.BackgroundProperties(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" }), new A.EffectList()));

    private static P.ShapeTree Tree() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new A.TransformGroup()));

    private static P.ShapeTree TreeOf(Page page, SlidePart part, Typefaces typefaces, Pictures pictures, string? language)
    {
        ShapeTree tree = Tree();
        uint id = 2;

        foreach (RenderBox? box in page.Boxes.Where(box => box.IsPainted))
        {
            foreach (P.Shape shape in ShapesOf(box, ref id))
            {
                tree.Append(shape);
            }
        }

        foreach (ImageBox image in page.Images)
        {
            tree.Append(PictureOf(image, part, pictures, id++));
        }

        foreach (TextSpan span in page.Spans)
        {
            tree.Append(SpanOf(span, typefaces, id++, language));
        }

        return tree;
    }

    /// <summary>One box as shapes: its background, and a thin rectangle for each rule it draws.</summary>
    /// <remarks>
    /// A shape in a deck carries one outline for all four sides, but the engine draws sides on their own — a
    /// rule under a table row is a bottom border and nothing else. Each visible side therefore becomes a
    /// filled rectangle as thick as the rule, which is what the PDF press draws as a line.
    /// </remarks>
    private static List<P.Shape> ShapesOf(RenderBox box, ref uint id)
    {
        List<P.Shape> shapes = [];
        Rect bounds = box.Bounds;

        if (box.Background is { IsVisible: true } background)
        {
            shapes.Add(Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, background, id++, box.Radius));
        }

        Border border = box.Border;

        if (border.Top.IsVisible)
        {
            shapes.Add(Rectangle(bounds.X, bounds.Y, bounds.Width, border.Top.Width, border.Top.Color, id++, 0));
        }

        if (border.Bottom.IsVisible)
        {
            shapes.Add(Rectangle(bounds.X, bounds.Bottom - border.Bottom.Width, bounds.Width, border.Bottom.Width, border.Bottom.Color, id++, 0));
        }

        if (border.Left.IsVisible)
        {
            shapes.Add(Rectangle(bounds.X, bounds.Y, border.Left.Width, bounds.Height, border.Left.Color, id++, 0));
        }

        if (border.Right.IsVisible)
        {
            shapes.Add(Rectangle(bounds.Right - border.Right.Width, bounds.Y, border.Right.Width, bounds.Height, border.Right.Color, id++, 0));
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

    /// <summary>A rounded corner is stated as a share of the shorter side, which is what the preset expects.</summary>
    private static A.AdjustValueList Adjusted(float radius, float width, float height)
    {
        float shorter = Math.Min(width, height);
        int value = shorter <= 0 ? 0 : (int)Math.Round(Math.Min(0.5f, radius / shorter) * PerCent);

        return new A.AdjustValueList(new A.ShapeGuide { Name = "adj", Formula = $"val {value}" });
    }

    private static P.Picture PictureOf(ImageBox box, SlidePart part, Pictures pictures, uint id)
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

    private static P.Shape SpanOf(TextSpan span, Typefaces typefaces, uint id, string? language)
    {
        (FontMetrics metrics, string face) = typefaces.Of(span.Font);
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
                new A.BodyProperties
                {
                    Wrap = A.TextWrappingValues.None,
                    LeftInset = 0,
                    TopInset = 0,
                    RightInset = 0,
                    BottomInset = 0,
                    Anchor = A.TextAnchoringTypeValues.Top,
                },
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties(new A.LineSpacing(new A.SpacingPercent { Val = PerCent })),
                    new A.Run(RunProperties(span, face, language), new A.Text(span.Text)))));

        return shape;
    }

    /// <summary>What a run is set in, and the language it is proofed in when the options name one.</summary>
    private static A.RunProperties RunProperties(TextSpan span, string face, string? language)
    {
        var properties = new A.RunProperties(
            new A.SolidFill(Colour(span.Color)),
            new A.LatinFont { Typeface = face },
            new A.ComplexScriptFont { Typeface = face })
        {
            Dirty = false,
            FontSize = (int)Math.Round(span.Font.Size * Hundredths),
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

    private static long Emu(float points) => (long)Math.Round(points * EmuPerPoint);

    /// <summary>The name and the height of each font of one deck, asked of the faces once per font.</summary>
    /// <remarks>
    /// A typeface tells its family name through Skia, as a native string the collector has to finalize, and the
    /// deck asked for it on every run: 1 894 names, and as many finalizers, for forty pages set in one font.
    /// Neither the name nor the metrics change within a document, so each font is asked once.
    /// </remarks>
    private sealed class Typefaces(Faces fonts)
    {
        private readonly Dictionary<Engine.Font, (FontMetrics Metrics, string Face)> _known = [];

        public (FontMetrics Metrics, string Face) Of(Engine.Font font)
        {
            if (_known.TryGetValue(font, out (FontMetrics Metrics, string Face) known))
            {
                return known;
            }

            known = (fonts.MetricsOf(font), fonts.For(font).Typeface.FamilyName);
            _known[font] = known;

            return known;
        }
    }

    /// <summary>Every picture of one deck, stored once however many slides show it.</summary>
    /// <remarks>
    /// A logo in a running head is read again for every page, so the same bytes arrive as a different object
    /// each time: the pictures are therefore held by what they contain, not by which object carries them.
    /// Slides after the first are related to the part that already holds the bytes rather than being given a
    /// copy, which is the same reason the PDF press decodes each picture once per document.
    /// </remarks>
    private sealed class Pictures
    {
        private readonly Dictionary<string, ImagePart> _parts = [];

        private readonly Dictionary<(SlidePart Slide, ImagePart Part), string> _ids = [];

        public string IdOf(Image image, SlidePart slide)
        {
            string key = Convert.ToHexString(SHA256.HashData(image.Content));

            if (!_parts.TryGetValue(key, out ImagePart? part))
            {
                part = slide.AddImagePart(TypeOf(image.Content));

                using var content = new MemoryStream(image.Content);
                part.FeedData(content);

                _parts[key] = part;
                _ids[(slide, part)] = slide.GetIdOfPart(part);
            }
            else if (!_ids.ContainsKey((slide, part)))
            {
                _ids[(slide, part)] = slide.CreateRelationshipToPart(part);
            }

            return _ids[(slide, part)];
        }
    }
}
