using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using SkiaSharp;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Walks one SVG tree and draws each shape and label it holds onto the canvas.</summary>
/// <remarks>
/// The canvas carries the geometry — every transform and viewport is concatenated onto it and restored after
/// the element — while the inherited presentation travels down the walk as a value, so a group's fill reaches
/// its children without being written onto them.
/// <para>
/// A drawing is markup a user may write, so the walk keeps a budget, and what lies past it is not drawn. Each
/// limit answers something measured: 50 000 nested groups overflowed the stack and ended the process; six
/// <c>use</c> elements pointing at their own group expanded to 6⁸ shapes from 300 bytes and ran on for
/// thirteen seconds after the caller cancelled; sixty nested <c>opacity</c> groups took a layer the size of
/// the canvas each, 3.4 GB on a slide's raster. A drawing meant to be read stays far inside every one of them.
/// </para>
/// </remarks>
internal sealed partial class Sketch(SKCanvas canvas, Faces faces, CancellationToken ct = default)
{
    /// <summary>How deep elements may nest before what lies below is left undrawn.</summary>
    internal const int Deepest = 256;

    /// <summary>How many elements one drawing may draw, every <c>use</c> counting as drawn again.</summary>
    internal const int Most = 20_000;

    private const int Hops = 8;

    /// <summary>How many group layers may stand open at once; a group nested deeper fades its shapes instead.</summary>
    private const int Layers = 4;

    /// <summary>How many elements are drawn between two looks at the cancellation token.</summary>
    private const int Between = 256;

    private static readonly HashSet<string> Unpainted = new(StringComparer.OrdinalIgnoreCase)
    {
        "defs", "title", "desc", "metadata", "style", "script", "symbol", "marker",
        "clipPath", "mask", "pattern", "linearGradient", "radialGradient", "filter",
    };

    private int _hops;

    private int _depth;

    private int _drawn;

    private int _layers;

    private float _fade = 1;

    /// <summary>Whether elements nested deeper or drawn in a greater number than a drawing may hold make up part of it.</summary>
    /// <remarks>Walked with a stack of its own, so a tree too deep to draw is measured without overflowing either.</remarks>
    internal static bool IsOverBudget(IElement root)
    {
        Stack<(IElement Element, int Depth)> waiting = new([(root, 0)]);
        int counted = 0;

        while (waiting.TryPop(out (IElement Element, int Depth) next))
        {
            counted++;

            if (next.Depth > Deepest || counted > Most)
            {
                return true;
            }

            INodeList children = next.Element.ChildNodes;

            for (int index = 0; index < children.Length; index++)
            {
                if (children[index] is IElement child)
                {
                    waiting.Push((child, next.Depth + 1));
                }
            }
        }

        return false;
    }

    /// <summary>Draws the root of a drawing into a viewport of its natural size.</summary>
    public void Root(IElement root, float width, float height)
    {
        Presentation ink = Presentation.Initial.Inherit(Properties.Of(root), new Area(width, height));
        int saved = canvas.Save();
        Area area = Viewport(root, width, height);

        Children(root, ink, area);
        canvas.RestoreToCount(saved);
    }

    /// <remarks>The nodes are walked rather than the element collection, whose indexer filters the nodes again on every call: a group of 20 000 shapes took 20 000² steps to reach its own children.</remarks>
    private void Children(IElement parent, Presentation ink, Area area)
    {
        INodeList children = parent.ChildNodes;

        for (int index = 0; index < children.Length; index++)
        {
            if (children[index] is IElement child)
            {
                Element(child, ink, area);
            }
        }
    }

    private void Element(IElement element, Presentation parent, Area area)
    {
        string name = element.LocalName;

        if (Unpainted.Contains(name) || _depth >= Deepest || _drawn >= Most)
        {
            return;
        }

        _drawn++;

        if (_drawn % Between == 0)
        {
            ct.ThrowIfCancellationRequested();
        }

        Properties properties = Properties.Of(element);

        if (properties.Has("display", "none"))
        {
            return;
        }

        Presentation ink = parent.Inherit(properties, area);
        int saved = canvas.Save();
        float fade = _fade;

        Transform(properties.Get("transform"));
        bool isLayer = IsFadedInLayer(properties.Number("opacity") ?? 1);

        _depth++;
        Draw(element, name, ink, area);
        _depth--;

        canvas.RestoreToCount(saved);
        _layers -= isLayer ? 1 : 0;
        _fade = fade;
    }

    private void Draw(IElement element, string name, Presentation ink, Area area)
    {
        switch (name.ToLowerInvariant())
        {
            case "g" or "a" or "switch":
                Children(element, ink, area);
                break;
            case "svg":
                Nested(element, ink, area);
                break;
            case "use":
                Used(element, ink, area);
                break;
            case "text":
                new Lettering(canvas, faces).Draw(element, ink, area, _fade);
                break;
            default:
                Shape(element, name, ink, area);
                break;
        }
    }

    private void Shape(IElement element, string name, Presentation ink, Area area)
    {
        if (!ink.IsVisible)
        {
            return;
        }

        using SKPath? path = PathOf(element, name.ToLowerInvariant(), ink, area);

        if (path is null)
        {
            return;
        }

        path.FillType = ink.IsEvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        Fill(path, ink);
        Stroke(path, ink);
    }

    private static SKPath? PathOf(IElement element, string name, Presentation ink, Area area) => name switch
    {
        "rect" => Rectangle(element, ink, area),
        "circle" => Circle(element, ink, area),
        "ellipse" => Ellipse(element, ink, area),
        "line" => Line(element, ink, area),
        "polyline" => Poly(element, isClosed: false),
        "polygon" => Poly(element, isClosed: true),
        "path" => element.GetAttribute("d") is { Length: > 0 } data ? SKPath.ParseSvgPathData(data) : null,
        _ => null,
    };

    private static SKPath? Rectangle(IElement element, Presentation ink, Area area)
    {
        float width = ink.Length(element.GetAttribute("width"), area.Width) ?? 0;
        float height = ink.Length(element.GetAttribute("height"), area.Height) ?? 0;

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        float x = ink.Length(element.GetAttribute("x"), area.Width) ?? 0;
        float y = ink.Length(element.GetAttribute("y"), area.Height) ?? 0;
        float? rx = ink.Length(element.GetAttribute("rx"), area.Width);
        float? ry = ink.Length(element.GetAttribute("ry"), area.Height);
        float radiusX = Math.Min(rx ?? ry ?? 0, width / 2);
        float radiusY = Math.Min(ry ?? rx ?? 0, height / 2);
        using var builder = new SKPathBuilder();
        var bounds = SKRect.Create(x, y, width, height);

        if (radiusX > 0 && radiusY > 0)
        {
            builder.AddRoundRect(bounds, radiusX, radiusY, SKPathDirection.Clockwise);
        }
        else
        {
            builder.AddRect(bounds, SKPathDirection.Clockwise);
        }

        return builder.Detach();
    }

    private static SKPath? Circle(IElement element, Presentation ink, Area area)
    {
        float radius = ink.Length(element.GetAttribute("r"), area.Diagonal) ?? 0;

        if (radius <= 0)
        {
            return null;
        }

        using var builder = new SKPathBuilder();
        builder.AddCircle(
            ink.Length(element.GetAttribute("cx"), area.Width) ?? 0,
            ink.Length(element.GetAttribute("cy"), area.Height) ?? 0,
            radius,
            SKPathDirection.Clockwise);

        return builder.Detach();
    }

    private static SKPath? Ellipse(IElement element, Presentation ink, Area area)
    {
        float rx = ink.Length(element.GetAttribute("rx"), area.Width) ?? 0;
        float ry = ink.Length(element.GetAttribute("ry"), area.Height) ?? 0;

        if (rx <= 0 || ry <= 0)
        {
            return null;
        }

        float cx = ink.Length(element.GetAttribute("cx"), area.Width) ?? 0;
        float cy = ink.Length(element.GetAttribute("cy"), area.Height) ?? 0;
        using var builder = new SKPathBuilder();

        builder.AddOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry), SKPathDirection.Clockwise);

        return builder.Detach();
    }

    private static SKPath Line(IElement element, Presentation ink, Area area)
    {
        using var builder = new SKPathBuilder();

        builder.MoveTo(ink.Length(element.GetAttribute("x1"), area.Width) ?? 0, ink.Length(element.GetAttribute("y1"), area.Height) ?? 0);
        builder.LineTo(ink.Length(element.GetAttribute("x2"), area.Width) ?? 0, ink.Length(element.GetAttribute("y2"), area.Height) ?? 0);

        return builder.Detach();
    }

    private static SKPath? Poly(IElement element, bool isClosed)
    {
        float[] numbers = Numbers.Of(element.GetAttribute("points"));

        if (numbers.Length < 4)
        {
            return null;
        }

        using var builder = new SKPathBuilder();
        builder.MoveTo(numbers[0], numbers[1]);

        for (int index = 2; index + 1 < numbers.Length; index += 2)
        {
            builder.LineTo(numbers[index], numbers[index + 1]);
        }

        if (isClosed)
        {
            builder.Close();
        }

        return builder.Detach();
    }

    private void Fill(SKPath path, Presentation ink)
    {
        if (ink.Fill is not { } color)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = Presentation.Faded(color, ink.FillOpacity * _fade),
        };

        canvas.DrawPath(path, paint);
    }

    private void Stroke(SKPath path, Presentation ink)
    {
        if (ink.Stroke is not { } color || ink.StrokeWidth <= 0)
        {
            return;
        }

        using SKPathEffect? dashes = ink.Dashes is { Length: > 0 } pattern && pattern.Sum() > 0
            ? SKPathEffect.CreateDash(pattern.Length % 2 == 0 ? pattern : [.. pattern, .. pattern], 0)
            : null;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = Presentation.Faded(color, ink.StrokeOpacity * _fade),
            StrokeWidth = ink.StrokeWidth,
            StrokeCap = ink.Cap,
            StrokeJoin = ink.Join,
            StrokeMiter = ink.Miter,
            PathEffect = dashes,
        };

        canvas.DrawPath(path, paint);
    }

    /// <summary>Fades what the element draws: through a layer of its own, or, past the layers allowed, in its shapes' colours.</summary>
    /// <returns>Whether a layer was opened, which the element closes when it is drawn.</returns>
    private bool IsFadedInLayer(float opacity)
    {
        if (opacity >= 1)
        {
            return false;
        }

        float kept = Math.Clamp(opacity, 0, 1);

        if (_layers >= Layers)
        {
            _fade *= kept;

            return false;
        }

        using var layer = new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Round(kept * 255)) };

        canvas.SaveLayer(layer);
        _layers++;

        return true;
    }

    private void Nested(IElement element, Presentation ink, Area area)
    {
        float x = ink.Length(element.GetAttribute("x"), area.Width) ?? 0;
        float y = ink.Length(element.GetAttribute("y"), area.Height) ?? 0;
        float width = ink.Length(element.GetAttribute("width"), area.Width) ?? area.Width;
        float height = ink.Length(element.GetAttribute("height"), area.Height) ?? area.Height;

        canvas.Translate(x, y);
        canvas.ClipRect(SKRect.Create(width, height));
        Children(element, ink, Viewport(element, width, height));
    }

    /// <summary>Draws the element a <c>use</c> points at, in place of the <c>use</c>.</summary>
    /// <remarks>Hops are counted so a reference that leads back to itself ends instead of overflowing the stack.</remarks>
    private void Used(IElement element, Presentation ink, Area area)
    {
        string? reference = element.GetAttribute("href") ?? element.GetAttribute("xlink:href");

        if (reference is not ['#', .. var id] || element.Owner?.GetElementById(id) is not { } target || _hops >= Hops)
        {
            return;
        }

        _hops++;
        canvas.Translate(ink.Length(element.GetAttribute("x"), area.Width) ?? 0, ink.Length(element.GetAttribute("y"), area.Height) ?? 0);

        if (target.LocalName.Equals("symbol", StringComparison.OrdinalIgnoreCase))
        {
            Symbol(element, target, ink, area);
        }
        else
        {
            Element(target, ink, area);
        }

        _hops--;
    }

    /// <summary>Draws a <c>symbol</c> where a <c>use</c> places it, its <c>viewBox</c> fitted to the size the <c>use</c> gives.</summary>
    /// <remarks>
    /// A symbol is never drawn where it is written, only where something uses it, which is what an icon sprite is
    /// made of. Drawn as an ordinary element it was turned away as unpainted, and every icon came out blank.
    /// </remarks>
    private void Symbol(IElement use, IElement symbol, Presentation ink, Area area)
    {
        float width = ink.Length(use.GetAttribute("width"), area.Width) ?? area.Width;
        float height = ink.Length(use.GetAttribute("height"), area.Height) ?? area.Height;
        Presentation inner = ink.Inherit(Properties.Of(symbol), area);

        _depth++;
        Children(symbol, inner, Viewport(symbol, width, height));
        _depth--;
    }

    /// <summary>Maps the <c>viewBox</c> of an <c>svg</c> element onto its viewport, and answers the area its children measure against.</summary>
    private Area Viewport(IElement svg, float width, float height)
    {
        float[] box = Numbers.Of(svg.GetAttribute("viewBox") ?? svg.GetAttribute("viewbox"));

        if (box.Length != 4 || box[2] <= 0 || box[3] <= 0)
        {
            return new Area(width, height);
        }

        (float scaleX, float scaleY, float offsetX, float offsetY) = Fitted(svg.GetAttribute("preserveAspectRatio"), width, height, box[2], box[3]);

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-box[0], -box[1]);

        return new Area(box[2], box[3]);
    }

    private static (float ScaleX, float ScaleY, float OffsetX, float OffsetY) Fitted(string? rule, float width, float height, float boxWidth, float boxHeight)
    {
        string[] words = (rule ?? "xMidYMid meet").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string align = words.FirstOrDefault(word => !word.Equals("defer", StringComparison.Ordinal)) ?? "xMidYMid";

        if (align.Equals("none", StringComparison.Ordinal))
        {
            return (width / boxWidth, height / boxHeight, 0, 0);
        }

        bool isSliced = words.Contains("slice", StringComparer.Ordinal);
        float scale = isSliced ? Math.Max(width / boxWidth, height / boxHeight) : Math.Min(width / boxWidth, height / boxHeight);
        float spareX = width - (boxWidth * scale);
        float spareY = height - (boxHeight * scale);

        return (scale, scale, spareX * Share(align, "xMid", "xMax"), spareY * Share(align, "YMid", "YMax"));
    }

    private static float Share(string align, string middle, string end)
    {
        if (align.Contains(middle, StringComparison.Ordinal))
        {
            return 0.5f;
        }

        return align.Contains(end, StringComparison.Ordinal) ? 1 : 0;
    }

    private void Transform(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
        {
            return;
        }

        foreach (Match step in TransformStep().Matches(transform))
        {
            float[] numbers = Numbers.Of(step.Groups[2].Value);
            SKMatrix matrix = MatrixOf(step.Groups[1].Value, numbers);

            canvas.Concat(in matrix);
        }
    }

    private static SKMatrix MatrixOf(string name, float[] numbers) => (name, numbers.Length) switch
    {
        ("matrix", 6) => new SKMatrix(numbers[0], numbers[2], numbers[4], numbers[1], numbers[3], numbers[5], 0, 0, 1),
        ("translate", 1) => SKMatrix.CreateTranslation(numbers[0], 0),
        ("translate", >= 2) => SKMatrix.CreateTranslation(numbers[0], numbers[1]),
        ("scale", 1) => SKMatrix.CreateScale(numbers[0], numbers[0]),
        ("scale", >= 2) => SKMatrix.CreateScale(numbers[0], numbers[1]),
        ("rotate", 1) => SKMatrix.CreateRotationDegrees(numbers[0]),
        ("rotate", >= 3) => SKMatrix.CreateRotationDegrees(numbers[0], numbers[1], numbers[2]),
        ("skewX", 1) => SKMatrix.CreateSkew(MathF.Tan(numbers[0] * MathF.PI / 180), 0),
        ("skewY", 1) => SKMatrix.CreateSkew(0, MathF.Tan(numbers[0] * MathF.PI / 180)),
        _ => SKMatrix.Identity,
    };

    [GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^()]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformStep();
}

/// <summary>The size percentages of the current viewport are taken of.</summary>
internal readonly record struct Area(float Width, float Height)
{
    /// <summary>The length a percentage of a radius is taken of, as SVG defines it.</summary>
    public float Diagonal => MathF.Sqrt(((Width * Width) + (Height * Height)) / 2);
}

/// <summary>The numbers of an SVG list attribute, however they are separated.</summary>
internal static partial class Numbers
{
    public static float[] Of(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            return [];
        }

        MatchCollection found = Number().Matches(list);
        float[] numbers = new float[found.Count];

        for (int index = 0; index < found.Count; index++)
        {
            numbers[index] = float.Parse(found[index].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return numbers;
    }

    [GeneratedRegex(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}
