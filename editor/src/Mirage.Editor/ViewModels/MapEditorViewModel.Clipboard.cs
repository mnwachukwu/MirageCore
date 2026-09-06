using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>Copy, cut and paste over a selected region — capturing tiles, attributes and lights,
/// trimming empty margins off the capture, and stamping each back down at an anchor.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    public void ClearClipboard()
    {
        ClipboardKind = ClipboardKind.None;
        ClipboardTiles = null;
        ClipboardAttrs = null;
        ClipboardLights = null;
    }

    public void CopySelection()
    {
        if (SelectedMap is null || SelectionRect is null) return;
        var sel = SelectionRect.Value;
        if (!CaptureRectToClipboard(sel.X1, sel.Y1, sel.X2, sel.Y2)) return;
        SelectionRect = null;
    }

    public void CutSelection()
    {
        // Ctrl+X is a window-level hotkey and reaches the map past every disabled control. Copy stays
        // available on a locked map: reading takes nothing.
        if (SelectedMap is null || SelectionRect is null || IsSelectedLocked) return;
        var sel = SelectionRect.Value;
        if (!CaptureRectToClipboard(sel.X1, sel.Y1, sel.X2, sel.Y2)) return;

        var map = SelectedMap.Record;
        BeginBatch();
        for (int y = sel.Y1; y <= sel.Y2; y++)
        {
            for (int x = sel.X1; x <= sel.X2; x++)
            {
                if (SelectedMode == EditorMode.Light)
                {
                    var lb = LightAt(map, x, y, SelectedAttributeLayer);
                    if (lb is null) continue;
                    SetLightSlot(map, x, y, SelectedAttributeLayer, null);
                    SelectedMap.UpdateRecord(map);
                    RepaintTile(x, y);
                    RecordLight(x, y, lb, null);
                    continue;
                }
                var t = map.Tile[x, y];
                var before = Snap(t);
                if (SelectedMode == EditorMode.Tile)
                {
                    int li = SelectedLayerArrayIndex;
                    if (LayerCell.IsEmpty(SelectedLayers(t)[li])) continue;
                    t = t.WithCell(SelectedLayerType, li, LayerCell.Empty);
                    map.Tile[x, y] = t;
                }
                else  // Attribute — clear the ACTIVE layer (Fringe drops its FringeAttr / ramp; Ground → Walkable).
                {
                    if (ActiveAttrType(t) == TileType.Walkable) continue;
                    t = WithActiveAttr(t, TileType.Walkable);
                    map.Tile[x, y] = t;
                }
                SelectedMap.UpdateRecord(map);
                RepaintTile(x, y);
                Record(x, y, before, Snap(t));
            }
        }

        CommitBatch();
        SelectionRect = null;
    }

    // Reads the rect into the clipboard, trimmed to the bounding box of non-empty
    // cells.  Returns false (leaving the clipboard untouched) if nothing in the
    // rect was non-empty for the current mode/layer.
    private bool CaptureRectToClipboard(int x1, int y1, int x2, int y2)
    {
        if (SelectedMap is null) return false;
        var map = SelectedMap.Record;
        int w = x2 - x1 + 1, h = y2 - y1 + 1;

        if (SelectedMode == EditorMode.Tile)
        {
            var raw = new int[w, h];
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    var t = map.Tile[x1 + dx, y1 + dy];
                    raw[dx, dy] = SelectedLayers(t)[SelectedLayerArrayIndex];
                }
            }

            if (!TryTrimTiles(raw, out var trimmed))
            {
                StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NothingToCopy);
                return false;
            }
            ClipboardTiles = trimmed;
            ClipboardAttrs = null;
            ClipboardKind = ClipboardKind.Tile;
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_CopiedTiles,
                ("Count", CountTiles(trimmed)));
            return true;
        }
        else if (SelectedMode == EditorMode.Attribute)
        {
            var raw = new TileAttr[w, h];
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                    // Read the ACTIVE logical layer (Ground inline vs FringeAttr — a ramp reads as LayerRamp on Fringe),
                    // so copying from the fringe plane captures fringe attributes + ramps, not the ground beneath.
                    raw[dx, dy] = ActiveAttrData(map.Tile[x1 + dx, y1 + dy]);
            }

            if (!TryTrimAttrs(raw, out var trimmed))
            {
                StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NothingToCopyAttr);
                return false;
            }
            ClipboardAttrs = trimmed;
            ClipboardTiles = null;
            ClipboardKind = ClipboardKind.Attribute;
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_CopiedAttributes,
                ("Count", CountAttrs(trimmed)));
            return true;
        }
        else // EditorMode.Light
        {
            var raw = new LightSpec?[w, h];
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                    raw[dx, dy] = LightAt(map, x1 + dx, y1 + dy, SelectedAttributeLayer)?.Light;
            }

            if (!TryTrimLights(raw, out var trimmed))
            {
                StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NothingToCopyLights);
                return false;
            }
            ClipboardLights = trimmed;
            ClipboardTiles = null;
            ClipboardAttrs = null;
            ClipboardKind = ClipboardKind.Light;
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_CopiedLights,
                ("Count", CountLights(trimmed)));
            return true;
        }
    }

    private static bool TryTrimTiles(int[,] src, out int[,] result)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (src[x, y] != 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0)
        {
            result = new int[0, 0];
            return false;
        }
        int tw = maxX - minX + 1, th = maxY - minY + 1;
        result = new int[tw, th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
                result[x, y] = src[minX + x, minY + y];
        }

        return true;
    }

    private static bool TryTrimAttrs(TileAttr[,] src, out TileAttr[,] result)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (src[x, y].Type != TileType.Walkable)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0)
        {
            result = new TileAttr[0, 0];
            return false;
        }
        int tw = maxX - minX + 1, th = maxY - minY + 1;
        result = new TileAttr[tw, th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
                result[x, y] = src[minX + x, minY + y];
        }

        return true;
    }

    private static int CountTiles(int[,] a)
    {
        int n = 0, w = a.GetLength(0), h = a.GetLength(1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                if (a[x, y] != 0) n++;
        }

        return n;
    }

    private static int CountAttrs(TileAttr[,] a)
    {
        int n = 0, w = a.GetLength(0), h = a.GetLength(1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                if (a[x, y].Type != TileType.Walkable) n++;
        }

        return n;
    }

    private void PasteTilesAt(int ax, int ay, bool retain)
    {
        if (SelectedMap is null || ClipboardTiles is null) return;
        var map = SelectedMap.Record;
        var clip = ClipboardTiles;
        int w = clip.GetLength(0), h = clip.GetLength(1);

        BeginBatch();
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                int idx = clip[dx, dy];
                if (idx == 0) continue;
                int tx = ax + dx, ty = ay + dy;
                if (!InMapBounds(tx, ty)) continue;

                var t = map.Tile[tx, ty];
                var layers = SelectedLayers(t);
                int li = SelectedLayerArrayIndex;
                if (!LayerCell.IsEmpty(SelectedLayers(t)[li])) continue;

                var before = Snap(t);
                // Clipboard cells store packed LayerCell values, so sheet + Anim flag are preserved on paste.
                t = t.WithCell(SelectedLayerType, li, idx);
                map.Tile[tx, ty] = t;
                SelectedMap.UpdateRecord(map);
                RepaintTile(tx, ty);
                Record(tx, ty, before, Snap(t));
            }
        }

        CommitBatch();
        if (!retain) ClearClipboard();
    }

    private void PasteLightsAt(int ax, int ay, bool retain)
    {
        if (SelectedMap is null || ClipboardLights is null) return;
        var map = SelectedMap.Record;
        var clip = ClipboardLights;
        int w = clip.GetLength(0), h = clip.GetLength(1);

        BeginBatch();
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                if (clip[dx, dy] is not { } spec) continue;
                int tx = ax + dx, ty = ay + dy;
                if (!InMapBounds(tx, ty)) continue;

                var before = LightAt(map, tx, ty, SelectedAttributeLayer);
                var pl = new PlacedLight(Guid.NewGuid(), tx, ty, spec, SelectedAttributeLayer);   // fresh identity per pasted light
                SetLightSlot(map, tx, ty, SelectedAttributeLayer, pl);
                SelectedMap.UpdateRecord(map);
                RepaintTile(tx, ty);
                RecordLight(tx, ty, before, pl);
            }
        }

        CommitBatch();
        if (!retain) ClearClipboard();
    }

    private static bool TryTrimLights(LightSpec?[,] src, out LightSpec?[,] result)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (src[x, y].HasValue)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0)
        {
            result = new LightSpec?[0, 0];
            return false;
        }
        int tw = maxX - minX + 1, th = maxY - minY + 1;
        result = new LightSpec?[tw, th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
                result[x, y] = src[minX + x, minY + y];
        }

        return true;
    }

    private static int CountLights(LightSpec?[,] a)
    {
        int n = 0, w = a.GetLength(0), h = a.GetLength(1);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                if (a[x, y].HasValue) n++;
        }

        return n;
    }

    private void PasteAttrsAt(int ax, int ay, bool retain)
    {
        if (SelectedMap is null || ClipboardAttrs is null) return;
        var map = SelectedMap.Record;
        var clip = ClipboardAttrs;
        int w = clip.GetLength(0), h = clip.GetLength(1);

        BeginBatch();
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                var src = clip[dx, dy];
                if (src.Type == TileType.Walkable) continue;
                int tx = ax + dx, ty = ay + dy;
                if (!InMapBounds(tx, ty)) continue;

                var t = map.Tile[tx, ty];
                // Skip cells whose ACTIVE layer already holds a DIFFERENT attribute — same legality check as the place
                // path. Writes to the active plane (Fringe → FringeAttr, incl. re-creating a ramp; Ground → inline).
                if (ActiveAttrType(t) != TileType.Walkable && ActiveAttrType(t) != src.Type) continue;

                var before = Snap(t);
                t = WithActiveAttr(t, src);
                map.Tile[tx, ty] = t;
                SelectedMap.UpdateRecord(map);
                RepaintTile(tx, ty);
                Record(tx, ty, before, Snap(t));
            }
        }

        CommitBatch();
        if (!retain) ClearClipboard();
    }
}
