using System.Buffers.Binary;
using DocumentFormat.OpenXml.Packaging;

namespace Snail.Toolkit.HtmlComposer.Pptx;

/// <summary>The package around the slides, written so that the same document gives the same bytes.</summary>
/// <remarks>
/// Left to itself the package draws every relationship id at random and stamps every zip entry with the time it
/// was written, so two decks of one document differed in a few dozen bytes: a cache keyed on the content, a
/// test comparing output or a signature over it saw a new file each time. Ids are counted instead, and the
/// entries carry one fixed time — the first a zip can hold.
/// </remarks>
internal static partial class Deck
{
    private const string PresentationContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";

    private const string CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private const string DublinCore = "http://purl.org/dc/elements/1.1/";

    private const int EndOfDirectorySize = 22;

    private const uint EndOfDirectorySignature = 0x06054B50;

    private const uint DirectoryEntrySignature = 0x02014B50;

    private const uint LocalEntrySignature = 0x04034B50;

    /// <summary>1 January 1980 in the MS-DOS form a zip entry carries: day 1 of month 1 of year 0.</summary>
    private const ushort FirstDay = (1 << 5) | 1;

    /// <summary>The next free relationship id of a part, counted from its relationships rather than drawn at random.</summary>
    private static string NextId(OpenXmlPartContainer owner)
    {
        int next = owner.Parts.Count() + 1;
        string id = $"rId{next}";

        while (owner.TryGetPartById(id, out _))
        {
            next++;
            id = $"rId{next}";
        }

        return id;
    }

    /// <summary>Gives every entry of the zip just written, from <paramref name="start"/> on, the same fixed time.</summary>
    /// <remarks>
    /// The times stand at fixed places in each entry's header and again in the directory at the end, so they are
    /// written over where they lie, without copying the deck. A zip the package wrote in its 64-bit form, or one
    /// that does not end as expected, is left as it is.
    /// </remarks>
    private static void Settle(Stream output, long start)
    {
        long end = output.Length;

        if (end - start < EndOfDirectorySize)
        {
            return;
        }

        byte[] tail = new byte[EndOfDirectorySize];

        output.Position = end - EndOfDirectorySize;
        output.ReadExactly(tail);

        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(10));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(12));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(16));
        bool isPlain = BinaryPrimitives.ReadUInt32LittleEndian(tail) == EndOfDirectorySignature
            && count != ushort.MaxValue
            && size != uint.MaxValue
            && offset != uint.MaxValue;

        if (isPlain)
        {
            long directory = end - EndOfDirectorySize - size;

            Restamp(output, directory, directory - offset, count, size);
        }

        output.Position = end;
    }

    private static void Restamp(Stream output, long directory, long origin, int count, uint size)
    {
        byte[] entries = new byte[size];
        byte[] local = new byte[4];
        int at = 0;

        output.Position = directory;
        output.ReadExactly(entries);

        for (int index = 0; index < count && at + 46 <= entries.Length; index++)
        {
            Span<byte> entry = entries.AsSpan(at);

            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != DirectoryEntrySignature)
            {
                return;
            }

            long header = origin + BinaryPrimitives.ReadUInt32LittleEndian(entry[42..]);

            output.Position = header;
            output.ReadExactly(local);

            if (BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalEntrySignature)
            {
                return;
            }

            Stamp(entry[12..]);
            WriteStamp(output, header + 10);

            at += 46
                + BinaryPrimitives.ReadUInt16LittleEndian(entry[28..])
                + BinaryPrimitives.ReadUInt16LittleEndian(entry[30..])
                + BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]);
        }

        output.Position = directory;
        output.Write(entries);
    }

    /// <summary>Midnight of the first day, as the four bytes of time and date of an entry.</summary>
    private static void Stamp(Span<byte> time)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(time, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(time[2..], FirstDay);
    }

    private static void WriteStamp(Stream output, long position)
    {
        Span<byte> time = stackalloc byte[4];

        Stamp(time);
        output.Position = position;
        output.Write(time);
    }
}
