using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mirage.Editor.Controls;
using Mirage.Shared.Records;
using System.IO;
using System.Runtime.InteropServices;

namespace Mirage.Editor.Services;

/// <summary>
/// Renders placed maps to PNG for the editor's export commands. Small exports (one map, or the 3×3
/// observable area) build a whole bitmap and save it; the world export <see cref="ExportWorldPng"/> streams
/// to disk rendering ONE map at a time into a fixed 512×384 surface, so no graphics surface ever scales with
/// the world size — an arbitrarily large world exports without a "wild" full-width render target that could
/// exceed Skia's max surface dimension. All methods must run on the UI thread —
/// <see cref="RenderTargetBitmap"/> is UI-thread-affine.
/// </summary>
internal static class MapImageExport
{
    // Renders the placements to one bitmap and saves it as PNG (Avalonia's built-in Skia encoder).
    public static void SaveBitmap(
        IReadOnlyList<(MapRecord Map, int PixelX, int PixelY)> placements,
        IReadOnlyList<Bitmap?> tilesets, int widthPx, int heightPx, string path)
    {
        using var bmp = TileGridControl.RenderMapsToBitmap(placements, tilesets, widthPx, heightPx);
        bmp.Save(path);
    }

    // Streams the placements to a PNG at `path`. Renders ONE map at a time into a fixed 512×384 render
    // target (the only graphics surface — always small enough to allocate, whatever the world size), then
    // assembles full-width output in a plain byte band (one map-row tall), which has no surface-dimension
    // limit. `progress` (if set) is invoked with (rowsWritten, worldH) after each grid-row band.
    public static void ExportWorldPng(
        IReadOnlyList<(MapRecord Map, int PixelX, int PixelY)> placements,
        IReadOnlyList<Bitmap?> tilesets, int worldW, int worldH, string path, Action<int, int>? progress = null)
    {
        int mapW = TileGridControl.MapPixelW(placements.Count > 0 ? placements[0].Map : null);
        int mapH = TileGridControl.MapPixelH(placements.Count > 0 ? placements[0].Map : null);

        // Group placements into grid-row bands keyed by their top pixel-Y (each a multiple of mapH).
        var mapsByTop = new Dictionary<int, List<(MapRecord Map, int PixelX)>>();
        foreach (var (map, px, py) in placements)
        {
            if (!mapsByTop.TryGetValue(py, out var list)) mapsByTop[py] = list = [];
            list.Add((map, px));
        }

        using var file = File.Create(path);
        using var png = new StreamingPngWriter(file, worldW, worldH);
        using var oneMap = new RenderTargetBitmap(new PixelSize(mapW, mapH), new Vector(96, 96));

        // Skia render targets are BGRA by default; only skip the R/B swap if the surface is actually RGBA.
        bool swapRB = oneMap.Format != PixelFormats.Rgba8888;
        var mapBgra = new byte[mapW * mapH * 4]; // one-map readback
        var bandRgb = new byte[worldW * mapH * 3]; // one grid-row of RGB output (plain memory, black = gaps)

        var pin = GCHandle.Alloc(mapBgra, GCHandleType.Pinned);
        try
        {
            nint ptr = pin.AddrOfPinnedObject();
            int written = 0;
            for (int top = 0; top < worldH; top += mapH)
            {
                Array.Clear(bandRgb); // black backdrop for gaps and empty grid rows
                if (mapsByTop.TryGetValue(top, out var maps))
                {
                    foreach (var (map, px) in maps)
                    {
                        using (var ctx = oneMap.CreateDrawingContext())
                        {
                            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, mapW, mapH));
                            TileGridControl.DrawPlacementsBand(ctx, [(map, 0, 0)], tilesets, 0, mapH);
                        }
                        oneMap.CopyPixels(new PixelRect(0, 0, mapW, mapH), ptr, mapBgra.Length, mapW * 4);
                        BlitMapIntoBand(mapBgra, bandRgb, px, worldW, mapW, mapH, swapRB);
                    }
                }
                for (int y = 0; y < mapH; y++)
                    png.WriteScanline(bandRgb.AsSpan(y * worldW * 3, worldW * 3));
                written += mapH;
                progress?.Invoke(written, worldH);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    // Copies one 512×384 map's BGRA readback into the full-width RGB band at horizontal pixel offset `dstX`.
    private static void BlitMapIntoBand(
        byte[] mapBgra, byte[] bandRgb, int dstX, int worldW, int mapW, int mapH, bool swapRB)
    {
        for (int y = 0; y < mapH; y++)
        {
            int src = y * mapW * 4;
            int dst = y * worldW * 3 + dstX * 3;
            for (int x = 0; x < mapW; x++, src += 4, dst += 3)
            {
                if (swapRB)
                {
                    bandRgb[dst] = mapBgra[src + 2]; // R
                    bandRgb[dst + 1] = mapBgra[src + 1]; // G
                    bandRgb[dst + 2] = mapBgra[src];     // B
                }
                else
                {
                    bandRgb[dst] = mapBgra[src];
                    bandRgb[dst + 1] = mapBgra[src + 1];
                    bandRgb[dst + 2] = mapBgra[src + 2];
                }
            }
        }
    }
}
