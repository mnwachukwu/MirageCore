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

/// <summary>The 3x3 neighbor grid and auto-linking: resolving each adjacent map, and keeping the
/// reciprocal links consistent when one edge is repointed.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    private readonly HashSet<int> _loadingNeighborIds = [];

    // ── Neighbor map 3×3 grid properties ────────────────────────────────────
    private MapRecord? Resolve(int id)
    {
        if (id <= 0) return null;
        if (!_data.IsOnline)
            return id < _data.OfflineMaps.Length ? _data.OfflineMaps[id] : null;
        return Maps.FirstOrDefault(m => m.Index == id && m.IsLoaded)?.Record;
    }

    private MapRecord? ResolveFrom(MapRecord? from, Func<MapRecord, int> getId) =>
        from is null ? null : Resolve(getId(from));

    private async Task EagerLoadNeighborsAsync()
    {
        if (!_data.IsOnline) return;
        var map = SelectedMap?.Record;
        if (map is null) return;
        await Task.WhenAll(
            LoadNeighborIfNeededAsync(map.Up),
            LoadNeighborIfNeededAsync(map.Down),
            LoadNeighborIfNeededAsync(map.Left),
            LoadNeighborIfNeededAsync(map.Right));
        map = SelectedMap?.Record;
        if (map is null) return;
        var upRec = Resolve(map.Up);
        var downRec = Resolve(map.Down);
        var leftRec = Resolve(map.Left);
        var rightRec = Resolve(map.Right);
        await Task.WhenAll(
            LoadNeighborIfNeededAsync(upRec?.Left ?? 0),
            LoadNeighborIfNeededAsync(upRec?.Right ?? 0),
            LoadNeighborIfNeededAsync(downRec?.Left ?? 0),
            LoadNeighborIfNeededAsync(downRec?.Right ?? 0),
            LoadNeighborIfNeededAsync(leftRec?.Up ?? 0),
            LoadNeighborIfNeededAsync(leftRec?.Down ?? 0),
            LoadNeighborIfNeededAsync(rightRec?.Up ?? 0),
            LoadNeighborIfNeededAsync(rightRec?.Down ?? 0));
    }

    private async Task LoadNeighborIfNeededAsync(int id)
    {
        if (id <= 0) return;
        var vm = Maps.FirstOrDefault(m => m.Index == id);
        if (vm is null || vm.IsLoaded || !_loadingNeighborIds.Add(id)) return;
        try
        {
            var pkt = await _conn.RequestMapAsync(id);
            if (pkt is not null)
                vm.LoadRecord(EditorDataService.MapRecordFromPacket(pkt));
        }
        catch { }
        finally
        {
            _loadingNeighborIds.Remove(id);
        }
        NotifyNeighborProperties();
    }

    private void NotifyNeighborProperties()
    {
        OnPropertyChanged(nameof(NeighborMapUp));
        OnPropertyChanged(nameof(NeighborMapDown));
        OnPropertyChanged(nameof(NeighborMapLeft));
        OnPropertyChanged(nameof(NeighborMapRight));
        OnPropertyChanged(nameof(NeighborMapUpLeft));
        OnPropertyChanged(nameof(NeighborMapUpRight));
        OnPropertyChanged(nameof(NeighborMapDownLeft));
        OnPropertyChanged(nameof(NeighborMapDownRight));
    }

    public MapRecord? NeighborMapUp => Resolve(MapUp);
    public MapRecord? NeighborMapDown => Resolve(MapDown);
    public MapRecord? NeighborMapLeft => Resolve(MapLeft);
    public MapRecord? NeighborMapRight => Resolve(MapRight);
    public MapRecord? NeighborMapUpLeft => ResolveFrom(Resolve(MapUp), m => m.Left) ?? ResolveFrom(Resolve(MapLeft), m => m.Up);
    public MapRecord? NeighborMapUpRight => ResolveFrom(Resolve(MapUp), m => m.Right) ?? ResolveFrom(Resolve(MapRight), m => m.Up);
    public MapRecord? NeighborMapDownLeft => ResolveFrom(Resolve(MapDown), m => m.Left) ?? ResolveFrom(Resolve(MapLeft), m => m.Down);
    public MapRecord? NeighborMapDownRight => ResolveFrom(Resolve(MapDown), m => m.Right) ?? ResolveFrom(Resolve(MapRight), m => m.Down);

    // ── Auto-linking ─────────────────────────────────────────────────────────
    // Topology-aware reciprocal linking. When the user changes a directional
    // link, the algorithm fills in (1) the direct opposite link on the target
    // and (2) any diagonal cell implied by an already-set perpendicular neighbor.
    // Recurses through the visited set so each (mapId, dir) edge is touched once.

    private enum MapDirection { Up, Down, Left, Right }

    private static MapDirection Opposite(MapDirection d) => d switch
    {
        MapDirection.Up => MapDirection.Down,
        MapDirection.Down => MapDirection.Up,
        MapDirection.Left => MapDirection.Right,
        MapDirection.Right => MapDirection.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private static (MapDirection A, MapDirection B) Perpendiculars(MapDirection d) => d switch
    {
        MapDirection.Up or MapDirection.Down => (MapDirection.Left, MapDirection.Right),
        MapDirection.Left or MapDirection.Right => (MapDirection.Up, MapDirection.Down),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private static int GetLink(MapRecord m, MapDirection d) => d switch
    {
        MapDirection.Up => m.Up,
        MapDirection.Down => m.Down,
        MapDirection.Left => m.Left,
        MapDirection.Right => m.Right,
        _ => 0,
    };

    private static void SetLink(MapRecord m, MapDirection d, int id)
    {
        switch (d)
        {
            case MapDirection.Up:
                m.Up = id;
                break;
            case MapDirection.Down:
                m.Down = id;
                break;
            case MapDirection.Left:
                m.Left = id;
                break;
            case MapDirection.Right:
                m.Right = id;
                break;
        }
    }

// Two maps joined by an edge are one continuous surface, so both sides must measure the same: world
    // coordinates run straight across a seam, and a mismatch puts a step across one somewhere other than
    // where it looks. Same rule the resize dialog enforces from its own side, so neither can make a mismatch.
    private async Task<bool> RevertMismatchedSizeAsync(MapDirection dir, int newId, int oldId)
    {
        if (SelectedMap is null || newId <= 0 || newId == SelectedMap.Index) return false;

        var target = RowFor(newId);
        // Online, a neighbor that never loaded has a placeholder record whose size means nothing. The
        // eager load above is what usually settles this; anything still unloaded is left alone.
        if (target is null || (_data.IsOnline && !target.IsLoaded)) return false;

        var mine = new MapSize(SelectedMap.Record.Width, SelectedMap.Record.Height);
        var theirs = new MapSize(target.Record.Width, target.Record.Height);
        if (mine == theirs) return false;

        SetLink(SelectedMap.Record, dir, oldId);
        NotifyNeighborProperties();
        NotifyLinkPicker(dir);

        if (ShowAlertAsync is not null)
        {
            await ShowAlertAsync(EditorStrings.Format(EditorStrings.MapEditor_LinkSizeMismatch,
                ("Target", newId), ("TargetSize", theirs), ("Size", mine)));
        }

        return true;
    }

    private void NotifyLinkPicker(MapDirection dir)
    {
        OnPropertyChanged(dir switch
        {
            MapDirection.Up => nameof(SelectedMapUp),
            MapDirection.Down => nameof(SelectedMapDown),
            MapDirection.Left => nameof(SelectedMapLeft),
            _ => nameof(SelectedMapRight),
        });
    }

    private readonly record struct LinkConflict(int MapId, MapDirection Dir, int CurrentTarget, int WantedTarget);

    private MapRowViewModel? RowFor(int id) =>
        id <= 0 ? null : Maps.FirstOrDefault(m => m.Index == id);

    private async Task HandleDirectionChangeAsync(MapDirection dir, int newId, int oldId)
    {
        if (SelectedMap is null || newId == oldId) return;
        if (_data.IsOnline) await EagerLoadNeighborsAsync();

        if (await RevertMismatchedSizeAsync(dir, newId, oldId)) return;

        var visited = new HashSet<(int, MapDirection)>();
        var conflicts = new List<LinkConflict>();
        var changed = new HashSet<int>();

        int sourceId = SelectedMap.Index;
        if (newId != 0)
            ApplyLink(sourceId, dir, newId, visited, conflicts, changed);
        else if (oldId != 0)
            ApplyUnlink(sourceId, dir, oldId, visited, changed);

        if (changed.Count > 0)
        {
            NotifyNeighborProperties();
            // A link edit moves maps around the grid rather than repainting one, so every map whose links
            // were rewritten is reported — the reciprocal side included.
            foreach (int id in changed) RaiseMapContentChanged(id);
            if (newId != 0)
            {
                StatusMessage = conflicts.Count == 0
                    ? EditorStrings.Format(EditorStrings.MapEditorStatus_AutoLinked,
                        ("Count", changed.Count))
                    : EditorStrings.Format(EditorStrings.MapEditorStatus_AutoLinkedConflict,
                        ("Count", changed.Count), ("Conflicts", conflicts.Count));
            }
            else
            {
                StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_AutoUnlinked,
                    ("Count", changed.Count));
            }
        }

        if (conflicts.Count > 0 && ShowAlertAsync is not null)
            await ShowAlertAsync(FormatConflictMessage(conflicts));
    }

    private void ApplyLink(int sourceId, MapDirection dir, int targetId,
        HashSet<(int, MapDirection)> visited, List<LinkConflict> conflicts, HashSet<int> changed)
    {
        if (sourceId <= 0 || targetId <= 0 || sourceId == targetId) return;
        TryApplyLink(targetId, Opposite(dir), sourceId, visited, conflicts, changed);
        var (yA, yB) = Perpendiculars(dir);
        TryDiagonalLink(sourceId, dir, targetId, yA, visited, conflicts, changed);
        TryDiagonalLink(sourceId, dir, targetId, yB, visited, conflicts, changed);
    }

    private void TryDiagonalLink(int sourceId, MapDirection dir, int targetId, MapDirection y,
        HashSet<(int, MapDirection)> visited, List<LinkConflict> conflicts, HashSet<int> changed)
    {
        var sourceRec = Resolve(sourceId);
        if (sourceRec is null) return;
        int mYId = GetLink(sourceRec, y);
        var mYRec = Resolve(mYId);
        if (mYRec is null) return;
        int diagId = GetLink(mYRec, dir);
        if (diagId <= 0) return;
        TryApplyLink(diagId, Opposite(y), targetId, visited, conflicts, changed);
    }

    private void TryApplyLink(int mapId, MapDirection dir, int wantedTarget,
        HashSet<(int, MapDirection)> visited, List<LinkConflict> conflicts, HashSet<int> changed)
    {
        if (!visited.Add((mapId, dir))) return;
        var row = RowFor(mapId);
        if (row is null) return;
        if (_data.IsOnline && !row.IsLoaded) return;
        int current = GetLink(row.Record, dir);
        if (current == wantedTarget) return;
        if (current != 0)
        {
            conflicts.Add(new LinkConflict(mapId, dir, current, wantedTarget));
            return;
        }
        SetLink(row.Record, dir, wantedTarget);
        row.MarkDirty();
        changed.Add(mapId);
        ApplyLink(mapId, dir, wantedTarget, visited, conflicts, changed);
    }

    /// <summary>Clearing a link removes the ONE edge the user cleared: the target's back-link home.
    ///
    /// <para>It is deliberately not the mirror of <see cref="ApplyLink"/>. Linking asserts facts that
    /// are still true afterwards, so re-asserting one is free; unlinking with the same shape DELETES
    /// facts the removed edge never implied. With A.Right=B, A.Up=C and C.Right=D, clearing A.Right
    /// walked B.Up → D, D.Left → C, and wiped C.Down — the vertical adjacency between C and A, which
    /// the horizontal A-B edge had nothing to do with — then recursed from there. Removing one link
    /// could empty a whole neighborhood.</para></summary>
    private void ApplyUnlink(int sourceId, MapDirection dir, int oldTargetId,
        HashSet<(int, MapDirection)> visited, HashSet<int> changed)
    {
        if (sourceId <= 0 || oldTargetId <= 0 || sourceId == oldTargetId) return;
        TryApplyUnlink(oldTargetId, Opposite(dir), sourceId, visited, changed);
    }

    private void TryApplyUnlink(int mapId, MapDirection dir, int matchTarget,
        HashSet<(int, MapDirection)> visited, HashSet<int> changed)
    {
        if (!visited.Add((mapId, dir))) return;
        var row = RowFor(mapId);
        if (row is null) return;
        if (_data.IsOnline && !row.IsLoaded) return;
        if (GetLink(row.Record, dir) != matchTarget) return;
        SetLink(row.Record, dir, 0);
        row.MarkDirty();
        changed.Add(mapId);
    }

    private string FormatConflictMessage(List<LinkConflict> conflicts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(EditorStrings.Get(EditorStrings.MapEditor_ConflictHeader));
        foreach (var c in conflicts)
        {
            sb.AppendLine(EditorStrings.Format(EditorStrings.MapEditor_ConflictRow,
                ("Map", MapFullLabel(c.MapId)),
                ("Dir", c.Dir.ToString()),
                ("Current", MapFullLabel(c.CurrentTarget)),
                ("Wanted", MapFullLabel(c.WantedTarget))));
        }
        return sb.ToString();
    }

    private string MapFullLabel(int id)
    {
        if (id <= 0) return EditorStrings.Get(EditorStrings.MapEditor_MapNone);
        var name = id < _data.LiveMapEntries.Length ? _data.LiveMapEntries[id].Name : null;
        return string.IsNullOrEmpty(name)
            ? EditorStrings.Format(EditorStrings.MapEditor_MapWithId, ("Id", id))
            : EditorStrings.Format(EditorStrings.MapEditor_MapWithName, ("Id", id), ("Name", name));
    }
}
