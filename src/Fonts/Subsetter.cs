using System.Buffers.Binary;
using System.Text;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Cuts a TrueType face down to the glyphs a document draws, every glyph keeping its number.</summary>
/// <remarks>
/// SkiaSharp writes a face into a PDF whole — it names the face a subset, but the subsetting library Skia would
/// use is not in its build — so two letters of PT Sans cost 443 KB. Cut here first, the face Skia embeds holds
/// only the outlines drawn. Glyph numbers stay as they were: the outlines of glyphs not drawn are emptied rather
/// than removed, so everything that names a glyph by number — the character map, the advances, the text Skia
/// writes — is still right without being rewritten. Only the tables a reader draws with and Skia sets with are
/// kept; the layout tables go, since nothing here shapes text. A face whose maker asked for it never to be
/// subset (bit 8 of <c>fsType</c>) is left whole, and so is anything that does not read as plain TrueType.
/// </remarks>
internal static class Subsetter
{
    private const uint TrueTypeVersion = 0x00010000;

    private const uint AppleTrueTypeVersion = 0x74727565;

    private const ushort NoSubsetting = 0x0100;

    private const uint ChecksumMagic = 0xB1B0AFBA;

    private const int HeaderSize = 12;

    private const int RecordSize = 16;

    private const ushort ArgumentsAreWords = 0x0001;

    private const ushort HasScale = 0x0008;

    private const ushort MoreComponents = 0x0020;

    private const ushort HasXAndYScale = 0x0040;

    private const ushort HasTwoByTwo = 0x0080;

    /// <summary>The tables a cut face keeps, in the order of their tags: what a PDF reader draws with and Skia sets with.</summary>
    private static readonly string[] Kept =
        ["OS/2", "cmap", "cvt ", "fpgm", "gasp", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post", "prep"];

    /// <summary>The face holding only the outlines of the given glyphs and of the glyphs they are built from.</summary>
    /// <returns>
    /// The bytes of the cut face, or <see langword="null"/> when it is to stay whole — among others when a table
    /// points past the end of the file, which is read as a face to be carried as it came rather than cut.
    /// </returns>
    public static byte[]? Cut(ReadOnlySpan<byte> font, IEnumerable<ushort> drawn)
    {
        try
        {
            return CutOrNull(font, drawn);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static byte[]? CutOrNull(ReadOnlySpan<byte> font, IEnumerable<ushort> drawn)
    {
        Dictionary<string, Range> tables = TablesOf(font);
        bool isTrueType = tables.ContainsKey("head") && tables.ContainsKey("maxp") && tables.ContainsKey("loca") && tables.ContainsKey("glyf");

        if (!isTrueType || IsSubsettingForbidden(font, tables))
        {
            return null;
        }

        ReadOnlySpan<byte> head = font[tables["head"]];
        ReadOnlySpan<byte> glyf = font[tables["glyf"]];
        int count = BinaryPrimitives.ReadUInt16BigEndian(font[tables["maxp"]][4..]);
        bool isLong = BinaryPrimitives.ReadInt16BigEndian(head[50..]) != 0;

        if (LocationsOf(font[tables["loca"]], count, isLong, glyf.Length) is not { } locations)
        {
            return null;
        }

        HashSet<ushort> kept = Closure(glyf, locations, drawn);
        (byte[] outlines, byte[] index) = Outlines(glyf, locations, kept);
        var written = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["glyf"] = outlines,
            ["loca"] = index,
            ["head"] = LongIndexed(head),
        };

        if (tables.TryGetValue("post", out Range post))
        {
            written["post"] = Nameless(font[post]);
        }

        return Assembled(font, tables, written);
    }

    /// <summary>The table directory: where each table lies in the file.</summary>
    private static Dictionary<string, Range> TablesOf(ReadOnlySpan<byte> font)
    {
        var tables = new Dictionary<string, Range>(StringComparer.Ordinal);
        uint version = BinaryPrimitives.ReadUInt32BigEndian(font);

        if (version is not (TrueTypeVersion or AppleTrueTypeVersion))
        {
            return tables;
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> record = font.Slice(HeaderSize + (index * RecordSize), RecordSize);
            string tag = Encoding.Latin1.GetString(record[..4]);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));

            _ = font.Slice(offset, length);
            tables[tag] = new Range(offset, offset + length);
        }

        return tables;
    }

    private static bool IsSubsettingForbidden(ReadOnlySpan<byte> font, Dictionary<string, Range> tables) =>
        tables.TryGetValue("OS/2", out Range os2)
        && (BinaryPrimitives.ReadUInt16BigEndian(font[os2][8..]) & NoSubsetting) != 0;

    /// <summary>Where each glyph's outline starts in <c>glyf</c>, one more than there are glyphs; nothing when they do not fit.</summary>
    private static int[]? LocationsOf(ReadOnlySpan<byte> loca, int count, bool isLong, int length)
    {
        int[] locations = new int[count + 1];

        for (int glyph = 0; glyph <= count; glyph++)
        {
            locations[glyph] = isLong
                ? checked((int)BinaryPrimitives.ReadUInt32BigEndian(loca[(glyph * 4)..]))
                : BinaryPrimitives.ReadUInt16BigEndian(loca[(glyph * 2)..]) * 2;

            bool isOrdered = glyph == 0 || locations[glyph] >= locations[glyph - 1];

            if (!isOrdered || locations[glyph] > length)
            {
                return null;
            }
        }

        return locations;
    }

    /// <summary>The glyphs drawn, the empty glyph every face opens with, and every glyph a composite is built from.</summary>
    private static HashSet<ushort> Closure(ReadOnlySpan<byte> glyf, int[] locations, IEnumerable<ushort> drawn)
    {
        int count = locations.Length - 1;
        var kept = new HashSet<ushort> { 0 };
        var waiting = new Stack<ushort>();

        waiting.Push(0);

        foreach (ushort glyph in drawn)
        {
            if (glyph < count && kept.Add(glyph))
            {
                waiting.Push(glyph);
            }
        }

        while (waiting.TryPop(out ushort glyph))
        {
            ReadOnlySpan<byte> outline = glyf[locations[glyph]..locations[glyph + 1]];

            foreach (ushort part in PartsOf(outline))
            {
                if (part < count && kept.Add(part))
                {
                    waiting.Push(part);
                }
            }
        }

        return kept;
    }

    /// <summary>The glyphs a composite outline places; none for a simple one.</summary>
    private static List<ushort> PartsOf(ReadOnlySpan<byte> outline)
    {
        var parts = new List<ushort>();

        if (outline.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(outline) >= 0)
        {
            return parts;
        }

        int at = 10;
        ushort flags;

        do
        {
            flags = BinaryPrimitives.ReadUInt16BigEndian(outline[at..]);
            parts.Add(BinaryPrimitives.ReadUInt16BigEndian(outline[(at + 2)..]));
            at += 4 + ((flags & ArgumentsAreWords) != 0 ? 4 : 2) + ScaleSize(flags);
        }
        while ((flags & MoreComponents) != 0);

        return parts;
    }

    private static int ScaleSize(ushort flags)
    {
        if ((flags & HasScale) != 0)
        {
            return 2;
        }

        if ((flags & HasXAndYScale) != 0)
        {
            return 4;
        }

        return (flags & HasTwoByTwo) != 0 ? 8 : 0;
    }

    /// <summary>The outlines of the kept glyphs at their numbers, every other glyph empty, with a long index to them.</summary>
    private static (byte[] Outlines, byte[] Index) Outlines(ReadOnlySpan<byte> glyf, int[] locations, HashSet<ushort> kept)
    {
        int count = locations.Length - 1;
        int size = 0;

        foreach (ushort glyph in kept)
        {
            size += Padded(locations[glyph + 1] - locations[glyph]);
        }

        byte[] outlines = new byte[size];
        byte[] index = new byte[(count + 1) * 4];
        int at = 0;

        for (int glyph = 0; glyph < count; glyph++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(glyph * 4), (uint)at);

            if (kept.Contains((ushort)glyph))
            {
                ReadOnlySpan<byte> outline = glyf[locations[glyph]..locations[glyph + 1]];

                outline.CopyTo(outlines.AsSpan(at));
                at += Padded(outline.Length);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(count * 4), (uint)at);

        return (outlines, index);
    }

    /// <summary>The font header saying the index is long, with its checksum adjustment cleared to be worked out again.</summary>
    private static byte[] LongIndexed(ReadOnlySpan<byte> head)
    {
        byte[] copy = head.ToArray();

        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(8), 0);
        BinaryPrimitives.WriteInt16BigEndian(copy.AsSpan(50), 1);

        return copy;
    }

    /// <summary>The PostScript table without its glyph names, which no reader of a PDF asks for; its rule metrics stay.</summary>
    private static byte[] Nameless(ReadOnlySpan<byte> post)
    {
        if (post.Length < 32)
        {
            return post.ToArray();
        }

        byte[] copy = post[..32].ToArray();

        BinaryPrimitives.WriteUInt32BigEndian(copy, 0x00030000);

        return copy;
    }

    /// <summary>The cut face as a file: the kept tables in tag order, aligned and summed as the format asks.</summary>
    private static byte[] Assembled(ReadOnlySpan<byte> font, Dictionary<string, Range> tables, Dictionary<string, byte[]> written)
    {
        List<string> tags = [.. Kept.Where(tables.ContainsKey)];
        int count = tags.Count;
        int size = HeaderSize + (count * RecordSize);

        foreach (string tag in tags)
        {
            size += Padded(written.TryGetValue(tag, out byte[]? table) ? table.Length : font[tables[tag]].Length);
        }

        byte[] file = new byte[size];
        int power = 1 << (int)Math.Log2(count);
        int at = HeaderSize + (count * RecordSize);
        int headAt = 0;

        BinaryPrimitives.WriteUInt32BigEndian(file, TrueTypeVersion);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(6), (ushort)(power * RecordSize));
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(8), (ushort)Math.Log2(power));
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(10), (ushort)((count - power) * RecordSize));

        for (int index = 0; index < count; index++)
        {
            string tag = tags[index];
            ReadOnlySpan<byte> table = written.TryGetValue(tag, out byte[]? replaced) ? replaced : font[tables[tag]];
            Span<byte> record = file.AsSpan(HeaderSize + (index * RecordSize), RecordSize);

            table.CopyTo(file.AsSpan(at));

            for (int letter = 0; letter < 4; letter++)
            {
                record[letter] = (byte)tag[letter];
            }

            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(file.AsSpan(at, Padded(table.Length))));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)at);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)table.Length);

            headAt = tag == "head" ? at : headAt;
            at += Padded(table.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headAt + 8), ChecksumMagic - Checksum(file));

        return file;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;

        for (int at = 0; at + 4 <= data.Length; at += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
        }

        return sum;
    }

    private static int Padded(int length) => (length + 3) & ~3;
}
