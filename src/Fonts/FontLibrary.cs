using System.Reflection;
using SkiaSharp;
using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>The typefaces documents are set in, loaded from files, streams or embedded resources.</summary>
/// <remarks>
/// One library serves every renderer, so a deck and a PDF of the same markup are set from the same faces and
/// measured the same way. A PDF embeds each face as a subset, and so reads the same on a machine that has none
/// of these fonts installed, Cyrillic included. Prefer TrueType outlines (<c>.ttf</c>): a PDF embeds them as
/// real fonts, but writes CFF-flavoured OpenType (<c>.otf</c>) as Type 3 glyph procedures that print without
/// hinting and cannot be searched or copied. A family with no bold face is set in its regular face rather than
/// emboldened, because synthetic bold is written as Type 3 as well. Load every face before the first document
/// is written; from then on the library is only read and can be shared between threads.
/// </remarks>
public sealed class FontLibrary : IDisposable
{
    private static readonly HashSet<string> GenericFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "math", "emoji",
        "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded",
    };

    private readonly List<Face> _faces = [];

    private bool _isDisposed;

    /// <summary>The family names of the loaded faces, in the order they were first loaded.</summary>
    /// <remarks>The first family is the fallback: text asking for a family that is not loaded is set in it.</remarks>
    public IReadOnlyList<string> Families
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return [.. _faces.Select(face => face.Typeface.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Loads a face from a stream, which is read to its end and stays the caller's to dispose.</summary>
    /// <exception cref="ArgumentException">The stream holds no font that can be read.</exception>
    public FontLibrary Add(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return Add(buffer.ToArray(), "The stream");
    }

    /// <summary>Loads a face from a font file.</summary>
    /// <exception cref="ArgumentException">The file holds no font that can be read.</exception>
    public FontLibrary AddFile(string path) => Add(File.ReadAllBytes(path), $"\"{path}\"");

    /// <summary>Loads every <c>.ttf</c> and <c>.otf</c> face in a folder, in file name order.</summary>
    public FontLibrary AddDirectory(string path)
    {
        foreach (string? file in Directory.EnumerateFiles(path).Where(IsFontFile).Order(StringComparer.Ordinal))
        {
            AddFile(file);
        }

        return this;
    }

    /// <summary>Loads a face embedded in an assembly as a manifest resource.</summary>
    /// <exception cref="ArgumentException">The assembly embeds no resource of that name, or it is not a font.</exception>
    public FontLibrary AddResource(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new ArgumentException($"{assembly.GetName().Name} embeds no resource named \"{name}\".", nameof(name));

        return Add(stream);
    }

    /// <summary>Releases every loaded face.</summary>
    /// <remarks>
    /// A library that has been disposed refuses to answer rather than answering wrongly. It used to keep
    /// working: the faces were gone, so every font resolved to the machine's default typeface and documents
    /// went on being set, silently, in something other than what they asked for — measured as the same run
    /// changing width from 53.41 to 54.70 points across a disposal. Disposing twice is harmless, as a
    /// container that owns the library may well do.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (Face face in _faces)
        {
            face.Dispose();
        }

        _faces.Clear();
    }

    internal Match Resolve(Font font)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return Chosen(font) is { } face
            ? new Match(face.Typeface, IsSlanted: font.IsItalic && !face.IsItalic)
            : new Match(SKTypeface.Default, IsSlanted: font.IsItalic);
    }

    /// <summary>The faces a document actually sets, with the bytes to carry them inside it.</summary>
    /// <remarks>
    /// Only the faces the document uses, so a deck is not fattened by every face the application happened to
    /// load, and only TrueType outlines: a document carries fonts as TrueType, and a CFF-flavoured OpenType
    /// face cannot travel that way. Those are named by <see cref="Unembeddable"/> instead of being written as
    /// something a reader will reject. The bytes come back from the very data the face was created over, so
    /// what travels is what the text was measured with.
    /// </remarks>
    public IEnumerable<EmbeddedFace> FacesOf(IReadOnlyCollection<Font> fonts)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return Chosen(fonts)
            .Where(face => !face.HasCffOutlines)
            .Select(face => new EmbeddedFace(
                face.Typeface.FamilyName,
                face.Typeface.FontWeight >= 600,
                face.IsItalic,
                face.Data.ToArray()));
    }

    /// <summary>The families the document sets that cannot travel inside it, because their outlines are not TrueType.</summary>
    public IEnumerable<string> Unembeddable(IReadOnlyCollection<Font> fonts)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return Chosen(fonts)
            .Where(face => face.HasCffOutlines)
            .Select(face => face.Typeface.FamilyName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    internal IEnumerable<Note> NotesFor(IReadOnlyCollection<Font> fonts)
    {
        if (fonts.Count == 0)
        {
            yield break;
        }

        if (_faces.Count == 0)
        {
            yield return new Note(
                NoteKind.NoFonts,
                null,
                "No fonts were loaded, so text is set in the default typeface of the machine, which draws nothing on a server without fonts.");
            yield break;
        }

        foreach (Font font in fonts.DistinctBy(font => font.Family).Where(font => !font.Families.Any(IsAvailable)))
        {
            yield return new Note(
                NoteKind.FamilySubstituted,
                font.Family,
                $"None of the families {font.Family} is loaded; the text is set in \"{_faces[0].Typeface.FamilyName}\" instead.");
        }

        IEnumerable<Match> bold = fonts.Where(font => font.IsBold).Select(Resolve).Where(match => match.Typeface.FontWeight < 600);

        foreach (string? family in bold.Select(match => match.Typeface.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new Note(
                NoteKind.BoldSubstituted,
                family,
                $"No bold face of \"{family}\" is loaded; bold text is set in the regular face.");
        }

        var used = fonts.Select(font => Resolve(font).Typeface).ToHashSet();
        IEnumerable<Face> outlined = _faces.Where(face => face.HasCffOutlines && used.Contains(face.Typeface));

        foreach (string? family in outlined.Select(face => face.Typeface.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new Note(
                NoteKind.CffOutlines,
                family,
                $"\"{family}\" has CFF outlines, which a PDF embeds as Type 3 glyphs that cannot be searched or copied; load a TrueType (.ttf) version of it instead.");
        }
    }

    /// <summary>The face each font is set in, once per face however many fonts chose it.</summary>
    private IEnumerable<Face> Chosen(IReadOnlyCollection<Font> fonts) =>
        fonts.Select(Chosen).OfType<Face>().Distinct();

    /// <summary>The loaded face nearest to what the font asks for, or nothing when no family matches.</summary>
    private Face? Chosen(Font font)
    {
        List<Face> family = FamilyOf(font);

        return family.Count == 0 ? null : family.MinBy(candidate => Distance(candidate, font));
    }

    private static int Distance(Face face, Font font) =>
        (face.IsItalic == font.IsItalic ? 0 : 1000) + Math.Abs(face.Typeface.FontWeight - font.Weight);

    private static bool IsFontFile(string path) =>
        Path.GetExtension(path) is var extension
        && (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase));

    private static bool HasCffOutlinesIn(byte[] content) => content is [(byte)'O', (byte)'T', (byte)'T', (byte)'O', ..];

    private FontLibrary Add(byte[] content, string origin)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var data = SKData.CreateCopy(content);
        var typeface = SKTypeface.FromData(data);

        if (typeface is null)
        {
            data.Dispose();

            throw new ArgumentException($"{origin} holds no font that can be read.");
        }

        _faces.Add(new Face(typeface, data, HasCffOutlinesIn(content)));

        return this;
    }

    private List<Face> FamilyOf(Font font)
    {
        foreach (string name in font.Families)
        {
            List<Face> named = FacesNamed(name);

            if (named.Count > 0)
            {
                return named;
            }
        }

        return _faces.Count == 0 ? [] : FacesNamed(_faces[0].Typeface.FamilyName);
    }

    private List<Face> FacesNamed(string family) =>
        [.. _faces.Where(face => face.Typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))];

    private bool IsAvailable(string family) => GenericFamilies.Contains(family) || FacesNamed(family).Count > 0;

    internal readonly record struct Match(SKTypeface Typeface, bool IsSlanted);

    /// <summary>A face a document can carry inside itself: what it is, and the bytes it was loaded from.</summary>
    public readonly record struct EmbeddedFace(string Family, bool IsBold, bool IsItalic, byte[] Content);

    private sealed record Face(SKTypeface Typeface, SKData Data, bool HasCffOutlines) : IDisposable
    {
        public bool IsItalic => Typeface.FontSlant != SKFontStyleSlant.Upright;

        public void Dispose()
        {
            Typeface.Dispose();
            Data.Dispose();
        }
    }
}
