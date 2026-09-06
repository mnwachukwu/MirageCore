using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mirage.Shared;

namespace Mirage.Editor.Services;

public sealed class EditorBitmapCache
{
    // Tile sheets indexed by sheet number (gaps may be null), loaded from assets/graphics/tiles/.
    public IReadOnlyList<Bitmap?> Tilesets { get; private set; } = [];
    // Display names per sheet (the filename minus its numeric prefix and extension; "" for gaps).
    public IReadOnlyList<string> TilesetNames { get; private set; } = [];

    // Character sheets, indexed the same way and split by footprint size. One number is one character
    // at every size, so index 1 is 1_* in each of the three folders.
    public IReadOnlyList<Bitmap?> Sprites { get; private set; } = [];
    public IReadOnlyList<Bitmap?> Sprites64 { get; private set; } = [];
    public IReadOnlyList<Bitmap?> Sprites96 { get; private set; } = [];
    // Sprite sheet names come from the 32x32 folder, which is the one every size class has.
    public IReadOnlyList<string> SpriteNames { get; private set; } = [];

    public IReadOnlyList<Bitmap?> Items { get; private set; } = [];
    public IReadOnlyList<string> ItemNames { get; private set; } = [];

    public void Load(string assetsPath)
    {
        // The outgoing sheets are released before the incoming ones replace them. One press of a Reload
        // button would never have shown it; the asset manager reloads after every rename, import and
        // delete, and each of those would otherwise strand a whole set of surfaces.
        var retired = new List<Bitmap?>(Tilesets);
        retired.AddRange(Sprites);
        retired.AddRange(Sprites64);
        retired.AddRange(Sprites96);
        retired.AddRange(Items);

        (Tilesets, TilesetNames) = LoadSheetSet(Path.Combine(assetsPath, Constants.TilesAssetSubfolder));
        (Sprites, SpriteNames) = LoadSpriteSheets(assetsPath, Constants.PicX);
        (Sprites64, _) = LoadSpriteSheets(assetsPath, Constants.PicX * 2);
        (Sprites96, _) = LoadSpriteSheets(assetsPath, Constants.PicX * 3);
        (Items, ItemNames) = LoadSheetSet(Path.Combine(assetsPath, Constants.ItemsAssetSubfolder));

        foreach (var old in retired)
        {
            // A reload that produced the very same instance must not dispose it out from under the new one.
            if (old is null) continue;
            if (Tilesets.Contains(old) || Sprites.Contains(old) || Sprites64.Contains(old)
                || Sprites96.Contains(old) || Items.Contains(old)) continue;
            try { old.Dispose(); }
            catch { /* a sheet already released is not worth failing a reload over */ }
        }
    }

    /// <summary>Re-scans the asset folders at runtime (the editor's Reload Assets action).</summary>
    public void Reload(string assetsPath) => Load(assetsPath);

    // The sheets for one footprint size class, from sprites/<cell>x<cell>/.
    private static (Bitmap?[] sheets, string[] names) LoadSpriteSheets(string assetsPath, int cell) =>
        LoadSheetSet(Path.Combine(assetsPath, Constants.SpritesAssetSubfolder, $"{cell}x{cell}"));

    // Scans one asset folder for numbered sheets (0_*.bmp, 1_*.png, ...). The leading number in each
    // filename is the stable sheet index; gaps stay null so a missing file never shifts a later index.
    private static (Bitmap?[] sheets, string[] names) LoadSheetSet(string dir)
    {
        var byIndex = new Dictionary<int, string>();
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                if (!SheetFile.IsSupported(path)) continue;
                int idx = SheetFile.ParseIndex(Path.GetFileNameWithoutExtension(path));
                if (idx >= 0 && idx < Constants.MaxTilesets) byIndex[idx] = path;
            }
        }
        if (byIndex.Count == 0) return ([], []);
        int max = 0;
        foreach (int k in byIndex.Keys) if (k > max) max = k;
        var sheets = new Bitmap?[max + 1];
        var names = new string[max + 1];
        for (int i = 0; i < names.Length; i++) names[i] = "";
        foreach (var kv in byIndex)
        {
            sheets[kv.Key] = TryLoad(kv.Value);
            names[kv.Key] = SheetFile.DisplayName(Path.GetFileNameWithoutExtension(kv.Value));
        }
        return (sheets, names);
    }

    /// <summary>
    /// Loads a sheet under the format contract: a BMP is color-keyed, a PNG keeps its own alpha.
    ///
    /// <para>The extension decides. Every BMP is keyed whichever way it decoded — the fast reader handles
    /// the 24-bit uncompressed files the art is authored as, and a 32-bit or compressed one is decoded
    /// normally and keyed afterwards.</para>
    /// </summary>
    private static Bitmap? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return SheetFile.UsesColorKey(path)
                ? LoadWithColorKey(path) ?? DecodeAndColorKey(path)
                : new Bitmap(path);
        }
        catch { return null; }
    }

    // Decodes an image and makes every pixel matching its top-left one transparent. Channel order is
    // whatever the decoder produced: the key is compared against the same layout it was read from, so the
    // match holds without knowing which of BGRA or RGBA it is.
    private static Bitmap DecodeAndColorKey(string path)
    {
        using var fs = File.OpenRead(path);
        var wb = WriteableBitmap.Decode(fs);
        using var fb = wb.Lock();

        unsafe
        {
            byte* pixels = (byte*)fb.Address;
            byte k0 = pixels[0], k1 = pixels[1], k2 = pixels[2];
            for (int y = 0; y < fb.Size.Height; y++)
            {
                byte* row = pixels + y * fb.RowBytes;
                for (int x = 0; x < fb.Size.Width; x++)
                {
                    byte* p = row + x * 4;
                    if (p[0] == k0 && p[1] == k1 && p[2] == k2) p[0] = p[1] = p[2] = p[3] = 0;
                    else p[3] = 255;
                }
            }
        }
        return wb;
    }

    // Loads a BMP file and returns a WriteableBitmap with the top-left pixel made
    // transparent throughout (the standard color-key convention).
    // Returns null for non-24-bit or compressed BMPs; caller falls back to Bitmap(path).
    private static WriteableBitmap? LoadWithColorKey(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);

        // File header (14 bytes)
        if (reader.ReadUInt16() != 0x4D42) return null; // "BM"
        reader.ReadUInt32(); // file size
        reader.ReadUInt32(); // reserved
        uint dataOffset = reader.ReadUInt32();

        // DIB header — we only handle BITMAPINFOHEADER (40 bytes)
        reader.ReadUInt32();              // header size
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        reader.ReadUInt16();              // planes
        ushort bpp = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();

        if (bpp != 24 || compression != 0 || width <= 0) return null;

        bool topDown = height < 0;
        int rows = Math.Abs(height);
        int stride = (width * 3 + 3) & ~3; // rows are padded to 4-byte boundary

        fs.Seek(dataOffset, SeekOrigin.Begin);
        var raw = new byte[stride * rows];
        if (fs.Read(raw, 0, raw.Length) != raw.Length) return null;

        // Top-left pixel: BMP without top-down flag stores rows bottom-up,
        // so the first visual row is the LAST row in the file.
        int topRow = topDown ? 0 : rows - 1;
        byte kb = raw[topRow * stride + 0]; // BMP stores BGR
        byte kg = raw[topRow * stride + 1];
        byte kr = raw[topRow * stride + 2];

        var wb = new WriteableBitmap(
            new PixelSize(width, rows),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var fb = wb.Lock();
        unsafe
        {
            byte* dst = (byte*)fb.Address;
            int rowBytes = fb.RowBytes;

            for (int row = 0; row < rows; row++)
            {
                // Flip row order for bottom-up BMPs
                int srcRow = topDown ? row : (rows - 1 - row);

                for (int col = 0; col < width; col++)
                {
                    byte b = raw[srcRow * stride + col * 3 + 0];
                    byte g = raw[srcRow * stride + col * 3 + 1];
                    byte r = raw[srcRow * stride + col * 3 + 2];
                    byte* p = dst + row * rowBytes + col * 4;

                    if (b == kb && g == kg && r == kr)
                    {
                        p[0] = p[1] = p[2] = p[3] = 0; // fully transparent
                    }
                    else
                    {
                        p[0] = b;
                        p[1] = g;
                        p[2] = r;
                        p[3] = 255;
                    }
                }
            }
        }

        return wb;
    }
}
