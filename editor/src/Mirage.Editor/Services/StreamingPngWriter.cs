using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Mirage.Editor.Services;

/// <summary>
/// Minimal streaming PNG encoder — 8-bit truecolor (RGB), filter type None. Writes the signature + IHDR
/// on construction, one scanline at a time via <see cref="WriteScanline"/>, and IEND on <see cref="Dispose"/>.
/// Image bytes are compressed incrementally through a <see cref="ZLibStream"/> whose output is chopped into
/// IDAT chunks, so neither the raw image nor the whole compressed stream is ever held in memory — only small
/// buffers. This lets the map editor export an arbitrarily large world map without allocating a
/// width*height bitmap; the caller renders and feeds one horizontal band of scanlines at a time.
/// </summary>
internal sealed class StreamingPngWriter : IDisposable
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly Stream _out;
    private readonly int _width;
    private readonly int _height;
    private readonly IdatChunkStream _idat;
    private readonly ZLibStream _deflate;
    private int _rowsWritten;
    private bool _disposed;

    public StreamingPngWriter(Stream output, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        _out = output;
        _width = width;
        _height = height;

        _out.Write(Signature, 0, Signature.Length);
        WriteIhdr();
        _idat = new IdatChunkStream(_out);
        _deflate = new ZLibStream(_idat, CompressionLevel.Optimal, leaveOpen: true);
    }

    /// <summary>Writes one scanline; <paramref name="rgb"/> must be width*3 bytes (R,G,B per pixel).</summary>
    public void WriteScanline(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != _width * 3)
            throw new ArgumentException($"Scanline must be {_width * 3} bytes (got {rgb.Length}).", nameof(rgb));
        if (_rowsWritten >= _height)
            throw new InvalidOperationException("All scanlines have already been written.");
        _deflate.WriteByte(0); // per-row filter type: None
        _deflate.Write(rgb);
        _rowsWritten++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _deflate.Dispose();       // flushes remaining deflate output into _idat
        _idat.Flush();            // emit any buffered compressed bytes as the final IDAT chunk
        WriteChunk("IEND"u8, default);
        _out.Flush();
    }

    private void WriteIhdr()
    {
        Span<byte> data = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data[0..4], (uint)_width);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..8], (uint)_height);
        data[8] = 8;  // bit depth
        data[9] = 2;  // color type: truecolor (RGB)
        data[10] = 0; // compression: deflate
        data[11] = 0; // filter method: adaptive (per-row filter bytes)
        data[12] = 0; // interlace: none
        WriteChunk("IHDR"u8, data);
    }

    private void WriteChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data) => WriteChunkTo(_out, type, data);

    // Writes one PNG chunk: length (u32 BE) + 4-byte type + data + CRC32 over (type || data).
    internal static void WriteChunkTo(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header[0..4], (uint)data.Length);
        type.CopyTo(header[4..]);
        output.Write(header);
        if (!data.IsEmpty) output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(type, data));
        output.Write(crc);
    }

    // A write-only stream that repackages the compressed bytes written to it into PNG IDAT chunks, emitting
    // a chunk whenever the buffer fills so the compressed output is also bounded (never fully accumulated).
    private sealed class IdatChunkStream(Stream output) : Stream
    {
        private const int ChunkSize = 32 * 1024;
        private readonly byte[] _buffer = new byte[ChunkSize];
        private int _count;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int n = Math.Min(buffer.Length, ChunkSize - _count);
                buffer[..n].CopyTo(_buffer.AsSpan(_count));
                _count += n;
                buffer = buffer[n..];
                if (_count == ChunkSize) Emit();
            }
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        // Emits any buffered bytes as a (final, smaller) IDAT chunk. Called after the ZLibStream is disposed.
        public override void Flush()
        {
            if (_count > 0) Emit();
        }

        private void Emit()
        {
            WriteChunkTo(output, "IDAT"u8, _buffer.AsSpan(0, _count));
            _count = 0;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // Standard PNG CRC-32 (ISO-HDLC, reflected polynomial 0xEDB88320), computed over (type || data).
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        public static uint Compute(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
