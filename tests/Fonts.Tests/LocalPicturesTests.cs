using System.Diagnostics;

namespace Snail.Toolkit.HtmlComposer.Fonts.Tests;

/// <summary>
/// The fence is the one piece of this library that stands between markup a user wrote and the files of the
/// machine setting it, so each way out of the folder is held to its own test.
/// </summary>
public sealed class LocalPicturesTests : IDisposable
{
    private readonly string _root = Folder();

    private readonly string _outside = Folder();

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    [Fact]
    public void Load_AFileUnderTheBaseDirectory_ReadsIt()
    {
        File.WriteAllBytes(Path.Combine(_root, "logo.png"), RedSquare());

        Assert.NotNull(new LocalPictures(_root).Load("logo.png"));
    }

    [Fact]
    public void Load_APathClimbingOutOfTheBaseDirectory_ReadsNothing()
    {
        File.WriteAllBytes(Path.Combine(_outside, "secret.png"), RedSquare());

        Assert.Null(new LocalPictures(_root).Load($"../{Path.GetFileName(_outside)}/secret.png"));
    }

    [Fact]
    public void Load_AnAbsolutePathOutsideTheBaseDirectory_ReadsNothing()
    {
        string secret = Path.Combine(_outside, "secret.png");
        File.WriteAllBytes(secret, RedSquare());

        Assert.Null(new LocalPictures(_root).Load(secret));
    }

    [Fact]
    public void Load_AFileUriLeadingOutsideTheBaseDirectory_ReadsNothing()
    {
        string secret = Path.Combine(_outside, "secret.png");
        File.WriteAllBytes(secret, RedSquare());

        Assert.Null(new LocalPictures(_root).Load(new Uri(secret).AbsoluteUri));
    }

    /// <summary>
    /// The fence used to be satisfied by the name alone. A link sitting under the base directory passed the
    /// prefix check, because the path was only tidied and never followed, and the bytes came from wherever it
    /// pointed.
    /// </summary>
    [Fact]
    public void Load_ALinkUnderTheBaseDirectoryLeadingOutside_ReadsNothing()
    {
        string secret = Path.Combine(_outside, "secret.png");
        File.WriteAllBytes(secret, RedSquare());
        File.CreateSymbolicLink(Path.Combine(_root, "logo.png"), secret);

        Assert.Null(new LocalPictures(_root).Load("logo.png"));
    }

    /// <summary>A linked folder carries everything beneath it out of the fence as surely as a linked file does.</summary>
    [Fact]
    public void Load_AFileInsideALinkedFolderLeadingOutside_ReadsNothing()
    {
        File.WriteAllBytes(Path.Combine(_outside, "secret.png"), RedSquare());
        Directory.CreateSymbolicLink(Path.Combine(_root, "assets"), _outside);

        Assert.Null(new LocalPictures(_root).Load("assets/secret.png"));
    }

    /// <summary>A link that leads back inside the fence is an ordinary file, and refusing it would be a bug of its own.</summary>
    [Fact]
    public void Load_ALinkThatStaysUnderTheBaseDirectory_ReadsIt()
    {
        string real = Path.Combine(_root, "real.png");
        File.WriteAllBytes(real, RedSquare());
        File.CreateSymbolicLink(Path.Combine(_root, "logo.png"), real);

        Assert.NotNull(new LocalPictures(_root).Load("logo.png"));
    }

    [Fact]
    public void Load_WithoutABaseDirectory_ReadsNoFileAtAll()
    {
        string picture = Path.Combine(_root, "logo.png");
        File.WriteAllBytes(picture, RedSquare());

        Assert.Null(new LocalPictures(null).Load(picture));
    }

    [Fact]
    public void Load_ADataUri_NeedsNoFolderAtAll() =>
        Assert.NotNull(new LocalPictures(null).Load($"data:image/png;base64,{Convert.ToBase64String(RedSquare())}"));

    /// <summary>Nothing is fetched over the network, whatever the markup asks for.</summary>
    [Fact]
    public void Load_AnHttpSource_ReadsNothing() =>
        Assert.Null(new LocalPictures(_root).Load("https://example.org/logo.png"));

    /// <summary>
    /// A picture that cannot be read is a note and the document goes on; it used to be an exception that took
    /// the whole document down, because reading asked whether the file existed and then read it as a separate
    /// act. Unreadable is not the same as absent, and only the second was ever handled.
    /// </summary>
    [Fact]
    public void Load_AFileThatExistsAndCannotBeRead_ReadsNothingRatherThanThrowing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string picture = Path.Combine(_root, "logo.png");
        File.WriteAllBytes(picture, RedSquare());
        File.SetUnixFileMode(picture, UnixFileMode.None);

        if (IsReadable(picture))
        {
            return;
        }

        Assert.Null(new LocalPictures(_root).Load("logo.png"));
    }

    /// <summary>A new folder named as Snail.Sdk.Mcp.Testing names a test's, so a cleaner can prove a crashed run's is a leftover.</summary>
    /// <remarks>
    /// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>, the start in Unix seconds as the system reports it: the tests of a
    /// library do not reference the package, so they name the folder themselves, as the standard asks of a project without
    /// it. <see cref="Dispose"/> deletes it when the test ends.
    /// </remarks>
    private static string Folder()
    {
        using var process = Process.GetCurrentProcess();
        long start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
        string folder = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{start}-{Path.GetRandomFileName()[..8]}");

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>Whether this user is actually held off the file, which root never is.</summary>
    private static bool IsReadable(string path)
    {
        try
        {
            using FileStream opened = File.OpenRead(path);

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] RedSquare() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09,
        0x29, 0x00, 0x00, 0x00, 0x18, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x9F, 0x81, 0xE1, 0x3F, 0x03, 0xC3, 0x7F, 0x06, 0x86, 0xFF, 0x0C, 0x00, 0x2A, 0xDB, 0x04, 0xFB,
        0x29, 0x8B, 0x3F, 0xBE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
