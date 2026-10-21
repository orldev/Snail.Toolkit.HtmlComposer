using System.Text;
using System.Xml;
using Snail.Toolkit.HtmlComposer.Engine;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>Text as PowerPoint keeps it: paragraphs of runs, wrapped by the box they stand in.</summary>
internal static partial class Deck
{
    /// <summary>A gap between two runs of a justified line wider than this is a space the line was stretched across.</summary>
    /// <remarks>
    /// Only a justified line loses its spaces to the stretch; in any other line a gap is a margin or a padding,
    /// and turning it into a space put a word break into <c>a&lt;span style="margin-left:10px"&gt;b&lt;/span&gt;</c>.
    /// </remarks>
    private const float Gap = 0.5f;

    /// <summary>A block of text as one text box that wraps its paragraphs as the block did.</summary>
    /// <remarks>
    /// The box is the block's content box, so the lines wrap where the engine wrapped them as long as the face is
    /// the one they were measured in, and keep wrapping sensibly when someone edits them. It grows downwards with
    /// what is added rather than letting text spill out of it.
    /// </remarks>
    private static P.Shape TextBoxOf(TextFrame frame, Ink ink, uint id)
    {
        var body = new P.TextBody(
            new A.BodyProperties(new A.ShapeAutoFit())
            {
                Wrap = A.TextWrappingValues.Square,
                LeftInset = 0,
                TopInset = 0,
                RightInset = 0,
                BottomInset = 0,
                Anchor = A.TextAnchoringTypeValues.Top,
            },
            new A.ListStyle());

        body.Append(Paragraphs(frame.Paragraphs, ink));

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"text{id}" },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = Emu(frame.Bounds.X), Y = Emu(frame.Bounds.Y) },
                    new A.Extents { Cx = Emu(frame.Bounds.Width), Cy = Emu(frame.Bounds.Height) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            body);
    }

    /// <summary>Paragraphs as PowerPoint writes them, each opened by the room the engine left above it.</summary>
    /// <remarks>
    /// The space between two paragraphs is read off the page — from the foot of one to the top of the next —
    /// rather than from the margins and paddings that made it, because those belong to blocks the deck has no
    /// shape for. The line spacing is the height the engine gave the line, stated exactly, so a paragraph takes
    /// the same height in the deck as on the page whatever the face's own line gap is.
    /// </remarks>
    private static List<A.Paragraph> Paragraphs(IReadOnlyList<TextParagraph> paragraphs, Ink ink)
    {
        List<A.Paragraph> written = [];
        float? foot = null;

        foreach (TextParagraph paragraph in paragraphs)
        {
            TextLine first = paragraph.Lines[0];
            TextLine last = paragraph.Lines[^1];
            float before = foot is { } above ? Math.Max(0, first.Top - above) : 0;

            written.Add(ParagraphOf(paragraph, before, ink));
            foot = last.Top + last.Height;
        }

        return written;
    }

    private static A.Paragraph ParagraphOf(TextParagraph paragraph, float before, Ink ink)
    {
        var properties = new A.ParagraphProperties(
            new A.LineSpacing(new A.SpacingPoints { Val = SpacingOf(paragraph.Lines[0].Height) }))
        {
            Alignment = paragraph.Alignment switch
            {
                TextAlignment.Right => A.TextAlignmentTypeValues.Right,
                TextAlignment.Center => A.TextAlignmentTypeValues.Center,
                TextAlignment.Justify => A.TextAlignmentTypeValues.Justified,
                _ => A.TextAlignmentTypeValues.Left,
            },
        };

        if (before > 0.01f)
        {
            properties.Append(new A.SpaceBefore(new A.SpacingPoints { Val = SpacingOf(before) }));
        }

        properties.Append(new A.NoBullet());

        var written = new A.Paragraph(properties);
        List<(TextSpan Look, StringBuilder Text)> runs = Runs(paragraph);

        foreach ((TextSpan look, StringBuilder text) in runs)
        {
            (_, string face) = ink.Typefaces.Of(look.Font);

            written.Append(new A.Run(RunProperties(look, face, ink.Language), new A.Text(Printable(text.ToString()))));
        }

        TextSpan end = runs[^1].Look;

        written.Append(new A.EndParagraphRunProperties
        {
            FontSize = SizeOf(end.Font.Size),
            Dirty = false,
        });

        return written;
    }

    /// <summary>The runs of a paragraph joined back across its lines, with the spaces the lines swallowed returned.</summary>
    /// <remarks>
    /// Touching runs set alike become one, so a word is not cut into runs where the engine happened to place it
    /// in two pieces, and a justified line — whose words are runs of their own with the spaces stretched out
    /// between them — gets its spaces back as characters.
    /// </remarks>
    private static List<(TextSpan Look, StringBuilder Text)> Runs(TextParagraph paragraph)
    {
        List<(TextSpan Look, StringBuilder Text)> runs = [];

        for (int index = 0; index < paragraph.Lines.Count; index++)
        {
            TextLine line = paragraph.Lines[index];
            TextSpan? previous = null;

            foreach (TextSpan span in line.Spans.OrderBy(span => span.X))
            {
                bool isApart = paragraph.Alignment == TextAlignment.Justify
                    && previous is not null
                    && span.X - (previous.X + previous.Width) > Gap
                    && !EndsInSpace(runs[^1].Text)
                    && !span.Text.StartsWith(' ');

                Add(runs, span, isApart ? " " + span.Text : span.Text);
                previous = span;
            }

            if (line.IsWrappedAtSpace && index < paragraph.Lines.Count - 1 && runs.Count > 0 && !EndsInSpace(runs[^1].Text))
            {
                runs[^1].Text.Append(' ');
            }
        }

        return runs;
    }

    private static void Add(List<(TextSpan Look, StringBuilder Text)> runs, TextSpan span, string text)
    {
        if (runs.Count > 0 && IsAlike(runs[^1].Look, span))
        {
            runs[^1].Text.Append(text);
            return;
        }

        runs.Add((span, new StringBuilder(text)));
    }

    private static bool IsAlike(TextSpan one, TextSpan other) =>
        one.Font == other.Font && one.Color == other.Color && one.Decoration == other.Decoration;

    private static bool EndsInSpace(StringBuilder text) => text.Length > 0 && text[^1] == ' ';

    /// <summary>The text with every character XML cannot hold left out: control characters and lone surrogates.</summary>
    /// <remarks>
    /// The engine keeps whatever the markup carried, and a stray <c>&amp;#1;</c> from a database reached the Open
    /// XML text element, which refuses it: the whole deck failed for one invisible character. The page drew
    /// nothing for such a character either, so leaving it out changes nothing anyone could see. A string with
    /// nothing to leave out is returned as it is, which is every string in practice.
    /// </remarks>
    private static string Printable(string text)
    {
        int first = FirstUnprintable(text);

        if (first < 0)
        {
            return text;
        }

        var kept = new StringBuilder(text.Length);
        kept.Append(text, 0, first);

        for (int index = first; index < text.Length; index++)
        {
            if (IsPair(text, index))
            {
                kept.Append(text, index, 2);
                index++;
                continue;
            }

            if (XmlConvert.IsXmlChar(text[index]))
            {
                kept.Append(text[index]);
            }
        }

        return kept.ToString();
    }

    private static int FirstUnprintable(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (IsPair(text, index))
            {
                index++;
                continue;
            }

            if (!XmlConvert.IsXmlChar(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsPair(string text, int index) =>
        index + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[index + 1], text[index]);
}
