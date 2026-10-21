using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Snail.Toolkit.HtmlComposer.Engine;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Running lines as slide layouts: drawn once, shown on every slide made from them.</summary>
internal static partial class Deck
{
    private const uint FirstLayoutId = 2147483649U;

    /// <summary>The id a slide number field carries; PowerPoint writes this one for every slide number it inserts.</summary>
    private const string SlideNumberField = "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}";

    /// <summary>The index PowerPoint gives the slide number placeholder of a layout, which a slide repeats to show it.</summary>
    private const uint SlideNumberIndex = 12;

    /// <summary>The widest a slide number is expected to grow, as a share of the number the page printed.</summary>
    private const float Growth = 4;

    /// <summary>The placeholder that shows a slide its number, as a slide states it: nothing of its own but the field.</summary>
    /// <remarks>
    /// A layout's slide number appears on a slide only when the slide holds a placeholder of the same kind; its
    /// place, face and colour come from the layout. The number written in it is what PowerPoint shows until it
    /// recounts, and it recounts whenever the slides move.
    /// </remarks>
    private static P.Shape SlideNumber(int number, uint id) =>
        new(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Slide number" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(SlideNumberPlaceholder())),
            new P.ShapeProperties(),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Field(new A.RunProperties { Dirty = false }, new A.Text(number.ToString(CultureInfo.InvariantCulture)))
                {
                    Id = SlideNumberField,
                    Type = "slidenum",
                })));

    private static P.PlaceholderShape SlideNumberPlaceholder() => new()
    {
        Type = P.PlaceholderValues.SlideNumber,
        Size = P.PlaceholderSizeValues.Quarter,
        Index = SlideNumberIndex,
    };

    /// <summary>A slide layout and whether it shows a slide number.</summary>
    private sealed record Layout(SlideLayoutPart Part, bool HasSlideNumber);

    /// <summary>The layouts of one deck: the blank one, and one for every distinct set of running lines.</summary>
    /// <remarks>
    /// The running lines of a section repeat on all of its pages and only their numbers change, so pages whose
    /// lines read the same once the numbers are set aside share a layout. Someone changing the manager's phone
    /// in the footer then changes it once, in the layout, for every slide.
    /// <para>
    /// The page number becomes PowerPoint's slide number, which follows the slides as they are added and moved.
    /// The page count has no counterpart — PowerPoint has no field for the number of slides — so it stays the
    /// number the engine wrote, as text of the layout, where it is corrected once if the deck grows.
    /// </para>
    /// </remarks>
    private sealed class Layouts(SlideMasterPart master, Ink ink)
    {
        private readonly Dictionary<string, Layout> _known = new(StringComparer.Ordinal);

        private Layout? _blank;

        public Layout For(Page page)
        {
            if (page.Running is not { } running)
            {
                return _blank ??= Blank();
            }

            string key = Signature(running);

            if (!_known.TryGetValue(key, out Layout? layout))
            {
                layout = Made(running);
                _known[key] = layout;
            }

            return layout;
        }

        private Layout Blank() => new(master.SlideLayoutParts.First(), HasSlideNumber: false);

        private Layout Made(Running running)
        {
            SlideLayoutPart part = master.AddNewPart<SlideLayoutPart>(NextId(master));
            P.ShapeTree tree = Tree();
            uint id = 2;
            bool hasNumber = false;

            part.AddPart(master, NextId(part));

            foreach (RenderBox box in running.Boxes.Where(box => box.IsPainted))
            {
                Append(tree, ShapesOf(box, ref id));
            }

            foreach (ImageBox image in running.Images)
            {
                tree.Append(PictureOf(image, part, ink.Pictures, id++));
            }

            foreach (TextSpan span in running.Spans)
            {
                if (span.Field == TextField.PageNumber)
                {
                    tree.Append(NumberOf(span, running, id++, isPlaceholder: !hasNumber));
                    hasNumber = true;
                    continue;
                }

                tree.Append(SpanOf(span, ink, id++));
            }

            part.SlideLayout = new P.SlideLayout(
                new P.CommonSlideData(tree) { Name = "Running lines" },
                new P.ColorMapOverride(new A.MasterColorMapping()))
            {
                Type = P.SlideLayoutValues.Blank,
                Preserve = true,
            };

            P.SlideLayoutIdList list = master.SlideMaster!.SlideLayoutIdList!;

            list.Append(new P.SlideLayoutId
            {
                Id = FirstLayoutId + (uint)list.ChildElements.Count,
                RelationshipId = master.GetIdOfPart(part),
            });

            return new Layout(part, hasNumber);
        }

        /// <summary>The layout's slide number, standing where the page number was printed and set as it was.</summary>
        /// <remarks>
        /// A number grows as the deck does, and it grows away from whatever follows it on its line: a number
        /// with text after it — "3 / 12" — is aligned on its right edge and grows to the left, one ending its
        /// line grows to the right.
        /// <para>
        /// A layout holds one slide number placeholder, the one a slide repeats to show it. A second page number —
        /// the header and the footer both print it — is a text box of the layout holding the same field, which
        /// PowerPoint fills in on every slide all the same; written as text it stayed the first page's number.
        /// </para>
        /// </remarks>
        private P.Shape NumberOf(TextSpan span, Running running, uint id, bool isPlaceholder)
        {
            (FontMetrics metrics, string face) = ink.Typefaces.Of(span.Font);
            bool isFollowed = running.Spans.Any(other =>
                !ReferenceEquals(other, span)
                && Math.Abs(other.Baseline - span.Baseline) < 0.5f
                && other.X >= span.X + span.Width - 0.5f);
            float width = Math.Max(span.Width, 1) * Growth;
            float left = isFollowed ? span.X + span.Width - width : span.X;

            var look = new A.DefaultRunProperties(
                new A.SolidFill(Colour(span.Color)),
                new A.LatinFont { Typeface = face },
                new A.ComplexScriptFont { Typeface = face })
            {
                FontSize = SizeOf(span.Font.Size),
                Bold = span.Font.Weight >= 600,
                Italic = span.Font.IsItalic,
            };

            var field = new A.Field(RunProperties(span, face, ink.Language), new A.Text("‹#›"))
            {
                Id = SlideNumberField,
                Type = "slidenum",
            };

            var alignment = isFollowed ? A.TextAlignmentTypeValues.Right : A.TextAlignmentTypeValues.Left;
            var application = isPlaceholder
                ? new P.ApplicationNonVisualDrawingProperties(SlideNumberPlaceholder())
                : new P.ApplicationNonVisualDrawingProperties();
            var naming = new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Slide number" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                application);

            var frame = new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(left), Y = Emu(span.Baseline - metrics.Ascent) },
                    new A.Extents { Cx = Emu(width), Cy = Emu(metrics.Ascent + metrics.Descent) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill());

            var body = new P.TextBody(
                Unwrapped(),
                new A.ListStyle(new A.Level1ParagraphProperties(look) { Alignment = alignment }),
                new A.Paragraph(new A.ParagraphProperties { Alignment = alignment }, field));

            var shape = new P.Shape(naming, frame, body);

            return shape;
        }

        /// <summary>What the running lines of a page look like with their numbers set aside.</summary>
        /// <remarks>
        /// The runs are compared by what they say and the line they stand on, not by where along it they start:
        /// a number one digit longer moves the text beside it, and page 10 would otherwise want a layout of its own.
        /// </remarks>
        private static string Signature(Running running)
        {
            var key = new StringBuilder();

            foreach (TextSpan span in running.Spans)
            {
                key.Append(span.Field == TextField.None ? span.Text : $"#{span.Field}")
                    .Append('@').Append(Rounded(span.Baseline))
                    .Append(span.Font).Append(span.Color).Append(';');
            }

            foreach (RenderBox box in running.Boxes)
            {
                key.Append(box.Bounds).Append(box.Background).Append(box.Border).Append(';');
            }

            foreach (ImageBox image in running.Images)
            {
                key.Append(image.Bounds).Append(image.Image.Content.Length).Append(';');
            }

            return key.ToString();
        }

        private static string Rounded(float value) => value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
