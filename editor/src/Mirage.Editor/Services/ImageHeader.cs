using System.Buffers.Binary;

namespace Mirage.Editor.Services;

/// <summary>
/// An image's dimensions, read from its header rather than by decoding it.
///
/// <para>The asset manager lists every sheet in a folder with its size. Decoding each one to learn how big
/// it is would mean holding a bitmap per row for a number the first few dozen bytes already carry, and it
/// would tie a question about files to a graphics toolkit.</para>
///
/// <para>Both formats the loaders accept, and nothing else: an unreadable or unknown file reports (0, 0),
/// which the caller shows as "unknown" rather than as zero pixels.</para>
/// </summary>
internal static class ImageHeader
{
    /// <summary>Pixel width and height, or (0, 0) when the file cannot be read as a supported image.</summary>
    public static (int Width, int Height) TryReadSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[26];
            if (fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) return (0, 0);

            if (head[0] == 0x42 && head[1] == 0x4D) return BmpSize(head);
            if (IsPng(head)) return PngSize(head);
            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Whether a PNG carries any transparency at all. False for a file that is not a readable PNG.
    /// </summary>
    /// <remarks>
    /// Two ways a PNG can be transparent, and both count. Color types 4 and 6 carry a real alpha channel;
    /// types 0, 2 and 3 can still name a transparent color in a <c>tRNS</c> chunk, which palette art
    /// routinely does. Reading only the color type would report perfectly good keyed-palette art as having
    /// no transparency, and a warning that fires on correct files is worse than no warning.
    /// </remarks>
    public static bool PngHasTransparency(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[26];
            if (fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) return false;
            if (!IsPng(head)) return false;

            byte colorType = head[25];
            if (colorType is 4 or 6) return true;

            // Walk the chunk list for tRNS, from the first chunk rather than from where the header read
            // stopped — that landed inside IHDR's payload, and a walk started there reads noise as lengths.
            // IDAT is where the image data begins, and every ancillary chunk that matters precedes it.
            fs.Seek(PngSignature.Length, SeekOrigin.Begin);
            Span<byte> chunk = stackalloc byte[8];
            while (fs.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false) == chunk.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(chunk[..4]);
                if (length < 0) return false;
                var type = chunk[4..8];
                if (type.SequenceEqual("tRNS"u8)) return true;
                if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;
                fs.Seek(length + 4, SeekOrigin.Current);   // payload plus its CRC
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // BITMAPINFOHEADER: width and height are signed 32-bit little-endian at 18 and 22. A negative height
    // means the rows are stored top-down, which changes nothing about how tall the image is.
    private static (int, int) BmpSize(ReadOnlySpan<byte> head)
    {
        int w = BinaryPrimitives.ReadInt32LittleEndian(head[18..22]);
        int h = BinaryPrimitives.ReadInt32LittleEndian(head[22..26]);
        return w > 0 ? (w, Math.Abs(h)) : (0, 0);
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool IsPng(ReadOnlySpan<byte> head) => head[..8].SequenceEqual(PngSignature);

    // IHDR is required to be the first chunk, so width and height sit at a fixed offset: 8 signature
    // bytes, 4 length, 4 type, then two big-endian 32-bit values.
    private static (int, int) PngSize(ReadOnlySpan<byte> head)
    {
        if (!head[12..16].SequenceEqual("IHDR"u8)) return (0, 0);
        int w = BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        int h = BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
        return w > 0 && h > 0 ? (w, h) : (0, 0);
    }
}
