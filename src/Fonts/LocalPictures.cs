using Snail.Toolkit.HtmlComposer.Engine;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Reads pictures from <c>data:</c> URIs and from files under one folder, and nothing from anywhere else.</summary>
/// <remarks>
/// Nothing is fetched over the network. A converter that follows <c>http</c> sources in user-supplied HTML can
/// be pointed at internal services, and one slow host stalls the whole conversion. Remote pictures are fetched
/// beforehand and inlined as <c>data:</c> URIs, or read by an <see cref="IImageSource"/> of the application's
/// own, handed to the renderer in its options with whatever timeouts and allow-lists are needed.
/// </remarks>
public sealed class LocalPictures(string? baseDirectory) : IImageSource
{
    /// <inheritdoc/>
    public Image? Load(string source)
    {
        byte[]? content = source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? FromDataUri(source) : FromFile(source);

        return content is null ? null : Decoding.Read(content);
    }

    private static byte[]? FromDataUri(string source)
    {
        int comma = source.IndexOf(',');

        if (comma < 0 || !source[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string payload = source[(comma + 1)..];
        byte[] buffer = new byte[(payload.Length * 3 / 4) + 3];

        return Convert.TryFromBase64String(payload, buffer, out int written) ? buffer[..written] : null;
    }

    private static string? RelativePathOf(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            return Uri.UnescapeDataString(source);
        }

        return uri.IsFile ? uri.LocalPath : null;
    }

    /// <summary>The bytes of a picture, or nothing at all when it cannot be read for any reason.</summary>
    /// <remarks>
    /// A picture that cannot be read is a note and the document goes on without it, which is this library's
    /// answer to everything it cannot set as written. That held only for a picture that was logically absent:
    /// reading asked <see cref="File.Exists(string)"/> and then read, so a file that existed and then could
    /// not be read — taken away between the two, permissions, a failing disk, a path the platform refuses —
    /// threw, and one unreadable picture took the whole document with it. Failing to read is now simply no
    /// picture.
    /// </remarks>
    private byte[]? FromFile(string source)
    {
        if (baseDirectory is null || RelativePathOf(source) is not { } relative)
        {
            return null;
        }

        try
        {
            return Fenced(baseDirectory, relative);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The bytes of a file that really lies under the base directory, or nothing.</summary>
    private static byte[]? Fenced(string baseDirectory, string relative)
    {
        string root = Real(Path.GetFullPath(baseDirectory));
        string fence = Path.EndsInDirectorySeparator(root) ? root : $"{root}{Path.DirectorySeparatorChar}";
        string path = Path.GetFullPath(Path.Combine(root, relative));

        if (!File.Exists(path))
        {
            return null;
        }

        return Real(path).StartsWith(fence, StringComparison.Ordinal) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Where a path really leads, with every link along it followed.</summary>
    /// <remarks>
    /// <see cref="Path.GetFullPath(string)"/> tidies a path but does not follow links, so the fence used to be
    /// satisfied by a name while the bytes came from wherever the link pointed: a link under the base
    /// directory read any file the process could. Every segment is resolved, not only the last, because a
    /// linked folder carries everything beneath it out of the fence just as surely as a linked file does.
    /// <para>
    /// What a link resolves to is resolved in turn: the target is recorded as it was written, and may lead
    /// through links of its own. A temporary folder on macOS is exactly that — <c>/var</c> is itself a link to
    /// <c>/private/var</c> — so without this a link pointing at its own neighbour came back in a form the
    /// fence could not recognise, and a picture that should have been read was refused.
    /// </para>
    /// </remarks>
    private static string Real(string path)
    {
        string real = Path.GetPathRoot(path) ?? string.Empty;

        foreach (string segment in Path.GetRelativePath(real, path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Combine(real, segment);
            string followed = Followed(next);

            real = string.Equals(followed, next, StringComparison.Ordinal) ? next : Real(followed);
        }

        return real;
    }

    private static string Followed(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }

        return File.Exists(path)
            ? File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path
            : path;
    }
}
