using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>One destination map with its surroundings, and how many warp tiles lead to it.</summary>
public sealed partial class WarpTargetRowViewModel(
    int mapNum, string name, int entryCount, Bitmap? preview, Action<int> open) : ObservableObject
{
    public int MapNum { get; } = mapNum;
    public string Title { get; } = string.IsNullOrWhiteSpace(name) ? $"#{mapNum}" : $"#{mapNum}  {name}";
    public Bitmap? Preview { get; } = preview;

    public string EntryText { get; } = EditorStrings.Format(
        EditorStrings.WarpTargets_EntryCount, ("Count", entryCount));

    [RelayCommand]
    private void Open() => open(MapNum);
}

/// <summary>
/// Where one map's warps lead, as pictures.
///
/// <para>The World Preview draws the grid, which is the half of the world its links describe. Warps are the
/// other half and have no place on that grid: a doorway can send a player to a map ten screens away or to no
/// neighbor at all. This lists those destinations, one rendered canvas each, keyed by destination map rather
/// than by warp tile — three doors into the same cave are one place.</para>
/// </summary>
public sealed partial class WarpTargetsDialogViewModel : ObservableObject
{
    public WarpTargetsDialogViewModel(
        int sourceMap, string sourceName,
        IReadOnlyList<(int MapNum, string Name, int EntryCount, Bitmap? Preview)> targets,
        Action<int> open)
    {
        SourceMap = sourceMap;
        Intro = EditorStrings.Format(EditorStrings.WarpTargets_Intro,
            ("Map", sourceMap), ("Name", sourceName));
        Targets = [.. targets.Select(t => new WarpTargetRowViewModel(t.MapNum, t.Name, t.EntryCount, t.Preview,
            num => { open(num); Chosen?.Invoke(); }))];
    }

    public int SourceMap { get; }
    public string Intro { get; }
    public IReadOnlyList<WarpTargetRowViewModel> Targets { get; }
    public bool HasTargets => Targets.Count > 0;

    /// <summary>A destination was picked and opened.</summary>
    public event Action? Chosen;

    /// <summary>Builds the list for one map, rendering each destination inside its own 3x3.
    ///
    /// <para>Awaits each read rather than blocking on it: online a map may not have been fetched yet, and
    /// waiting on the wire from the UI thread is what freezes a window. A destination that cannot be read
    /// still gets a row, without a picture, because "there is a warp to map 40 and I cannot show it" is worth
    /// more than leaving map 40 out. The renders themselves stay on the UI thread, which is where a render
    /// target has to be made.</para></summary>
    public static async Task<WarpTargetsDialogViewModel> BuildAsync(
        int sourceMap, MapRecord source, Func<int, ValueTask<MapRecord?>> read,
        IReadOnlyList<Bitmap?> tilesets, Action<int> open)
    {
        var entryCounts = new Dictionary<int, int>();
        foreach (int dest in WarpLinks.Exits(source).Select(e => e.DestMap))
            entryCounts[dest] = entryCounts.GetValueOrDefault(dest) + 1;

        var rows = new List<(int, string, int, Bitmap?)>();
        foreach (int dest in WarpLinks.WarpOnlyDestinations(sourceMap, source))
        {
            var rec = await read(dest).ConfigureAwait(true);
            Bitmap? shot = null;
            if (rec is not null)
            {
                try
                {
                    shot = await RenderNeighborhoodAsync(rec, read, tilesets).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    EditorLog.Warn("Warp targets could not render map {Map}: {Error}", dest, ex.Message);
                }
            }
            rows.Add((dest, rec?.Name ?? "", entryCounts.GetValueOrDefault(dest), shot));
        }

        return new WarpTargetsDialogViewModel(sourceMap, source.Name, rows, open);
    }

    /// <summary>
    /// The destination in the middle of its own 3x3, the way the map editor frames a map.
    ///
    /// <para>A warp destination on its own says almost nothing — one screen of tiles could be anywhere. What
    /// tells you where you would land is what surrounds it, which is exactly the observable area the editor
    /// shows while authoring. The center is outlined so the card cannot be misread as nine maps that happen
    /// to sit together.</para>
    ///
    /// <para>Diagonals are not stored on a map: they are composed from two cardinal links, first non-zero
    /// wins, the same rule <c>WorldCoordHelper.BuildMapGrid</c> and the map editor's neighbor resolution
    /// both use.</para>
    ///
    /// <para>The result is cropped to the cells that actually hold a map, so a lone map is shown as a lone
    /// map and a 2x2 corner as a 2x2. Only gaps enclosed by the crop stay black, because those are real
    /// holes in the world and closing them would sit two maps together that are not neighbors.</para>
    /// </summary>
    private static async Task<Bitmap> RenderNeighborhoodAsync(
        MapRecord center, Func<int, ValueTask<MapRecord?>> read, IReadOnlyList<Bitmap?> tilesets)
    {
        var up = await read(center.Up).ConfigureAwait(true);
        var down = await read(center.Down).ConfigureAwait(true);
        var left = await read(center.Left).ConfigureAwait(true);
        var right = await read(center.Right).ConfigureAwait(true);

        async ValueTask<MapRecord?> Diagonal(MapRecord? viaA, Func<MapRecord, int> stepA,
                                             MapRecord? viaB, Func<MapRecord, int> stepB)
        {
            if (viaA is not null && stepA(viaA) > 0) return await read(stepA(viaA)).ConfigureAwait(true);
            if (viaB is not null && stepB(viaB) > 0) return await read(stepB(viaB)).ConfigureAwait(true);
            return null;
        }

        var upLeft = await Diagonal(up, m => m.Left, left, m => m.Up);
        var upRight = await Diagonal(up, m => m.Right, right, m => m.Up);
        var downLeft = await Diagonal(down, m => m.Left, left, m => m.Down);
        var downRight = await Diagonal(down, m => m.Right, right, m => m.Down);

        int mw = TileGridControl.MapPixelW(center), mh = TileGridControl.MapPixelH(center);
        (MapRecord? Map, int Col, int Row)[] cells =
        [
            (upLeft, 0, 0), (up, 1, 0), (upRight, 2, 0),
            (left, 0, 1), (center, 1, 1), (right, 2, 1),
            (downLeft, 0, 2), (down, 1, 2), (downRight, 2, 2),
        ];

        // Cropped to the cells that hold a map, so a destination with nothing around it is one map rather
        // than one map adrift in eight black rectangles. The center always exists, so the box always does.
        // A gap INSIDE the box still draws black: that hole is real, and closing it up would put two maps
        // side by side that are not.
        int minCol = 2, maxCol = 0, minRow = 2, maxRow = 0;
        foreach (var (map, col, row) in cells)
        {
            if (map is null) continue;
            minCol = Math.Min(minCol, col);
            maxCol = Math.Max(maxCol, col);
            minRow = Math.Min(minRow, row);
            maxRow = Math.Max(maxRow, row);
        }

        int cols = maxCol - minCol + 1, rows = maxRow - minRow + 1;
        int w = cols * mw, h = rows * mh;

        var placements = new List<(MapRecord Map, int PixelX, int PixelY)>(cells.Length);
        foreach (var (map, col, row) in cells)
            if (map is not null) placements.Add((map, (col - minCol) * mw, (row - minRow) * mh));

        // Art and marker share one drawing context: re-opening a context on a render target is not
        // guaranteed to preserve what is already on it, and a second pass could have wiped the maps.
        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));
            TileGridControl.DrawPlacementsBand(ctx, placements, tilesets, 0, h);
            // Only worth marking when there is something to tell it apart from.
            if (placements.Count > 1)
                ctx.DrawRectangle(null, CenterCellPen,
                    new Rect((1 - minCol) * mw, (1 - minRow) * mh, mw, mh));
        }
        return rtb;
    }

    // Marks which of the nine cells is the map being warped to. Thick because the card is shown small.
    private static readonly Pen CenterCellPen =
        new(new SolidColorBrush(Color.FromArgb(255, 0, 229, 255)), 10);
}
