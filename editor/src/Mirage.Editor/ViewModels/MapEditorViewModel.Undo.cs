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

/// <summary>The undo/redo stacks and the batching that groups a drag-paint into one operation,
/// plus the per-tile snapshot/restore those operate on and the Undo/Redo commands that replay them.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Undo / Redo ────────────────────────────────────────────────────────────

    // A tile is a value and its fringe plane is immutable, so a snapshot is the tile itself: nothing to
    // deep-copy, and a later edit to the map cannot reach back into an undo entry.
    private static TileRecord Snap(TileRecord t) => t;

    // An undo entry: a tile change (Tile/Attribute modes) OR a placed-light change (Light Sources mode,
    // at most one light per tile). Both carry the (x,y) they touch so undo can invalidate that cell.
    private abstract record UndoOp(int X, int Y);
    private sealed record TileOp(int X, int Y, TileRecord Before, TileRecord After) : UndoOp(X, Y);
    private sealed record LightOp(int X, int Y, PlacedLight? Before, PlacedLight? After) : UndoOp(X, Y);
    // A fixed NPC-spawn pin change (Attribute mode, NpcSpawn tool; at most one pin per tile). Before/After = the
    // index of the Npcs entry pinned at (x,y), or null for none. Symmetric with LightOp — a per-(x,y) op fully
    // captures the change; a row removal shifts every later index, so RemoveNpcRow fixes up these ops in place.
    private sealed record NpcSpawnOp(int X, int Y, WorldLayer Layer, int? Before, int? After) : UndoOp(X, Y);

    // Batch accumulates changes during a single pointer press/drag.
    private readonly List<UndoOp> _batch = [];
    private bool _batchOpen;

    public void BeginBatch()
    {
        _batch.Clear();
        _batchOpen = true;
        // A batch is one gesture, and the hidden-layer prompt is asked once per gesture: a drag crosses
        // many cells with the pointer still down, and a question per cell would be unusable.
        _hiddenLayerAsked = false;
    }

    public void CommitBatch()
    {
        _batchOpen = false;
        if (_batch.Count == 0) return;
        _undoStack.Push([.. _batch]);
        _redoStack.Clear();
        _batch.Clear();
        UpdateUndoRedo();
        NotifyMapContentChanged();
    }

    // Records a tile change.  If a batch is open it's accumulated; otherwise pushed immediately.
    private void Record(int x, int y, TileRecord before, TileRecord after)
    {
        if (before.Equals(after)) return;
        PushOp(new TileOp(x, y, before, after));
    }

    // Records a placed-light change (before/after = the light at (x,y); null = no light there).
    private void RecordLight(int x, int y, PlacedLight? before, PlacedLight? after)
    {
        if (before == after) return;
        PushOp(new LightOp(x, y, before, after));
    }

    // Records a fixed NPC-spawn pin change (before/after = the entry index pinned at (x,y); null = no pin there).
    private void RecordNpcSpawn(int x, int y, WorldLayer layer, int? before, int? after)
    {
        if (before == after) return;
        PushOp(new NpcSpawnOp(x, y, layer, before, after));
    }

    private void PushOp(UndoOp op)
    {
        if (_batchOpen)
        {
            _batch.Add(op);
        }
        else
        {
            _undoStack.Push([op]);
            _redoStack.Clear();
            UpdateUndoRedo();
        }
    }

    private readonly Stack<List<UndoOp>> _undoStack = new();
    private readonly Stack<List<UndoOp>> _redoStack = new();

    private void UpdateUndoRedo()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // Both reach the map through a window-level hotkey rather than a control, so the lock has to be part of
    // CanExecute — disabling the canvas and the panels does not stop Ctrl+Z.
    private bool CanUndo() => _undoStack.Count > 0 && SelectedMap is not null && !IsSelectedLocked;
    private bool CanRedo() => _redoStack.Count > 0 && SelectedMap is not null && !IsSelectedLocked;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        // Tested before the pop, so a refusal leaves the history where it was. A RelayCommand runs whether
        // or not CanExecute agrees, so the greyed button and the hotkey's own check are both affordances.
        if (IsSelectedLocked || !_undoStack.TryPop(out var batch) || SelectedMap is null) return;
        var map = SelectedMap.Record;
        for (int i = batch.Count - 1; i >= 0; i--)
            ApplyUndoOp(map, batch[i], undo: true);
        _redoStack.Push(batch);
        UpdateUndoRedo();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (IsSelectedLocked || !_redoStack.TryPop(out var batch) || SelectedMap is null) return;
        var map = SelectedMap.Record;
        foreach (var op in batch)
            ApplyUndoOp(map, op, undo: false);
        _undoStack.Push(batch);
        UpdateUndoRedo();
    }

    // Applies one undo entry to its "before" (undo) or "after" (redo) state.
    private void ApplyUndoOp(MapRecord map, UndoOp op, bool undo)
    {
        switch (op)
        {
            case TileOp t:
                map.Tile[t.X, t.Y] = undo ? t.Before : t.After;
                break;
            case LightOp l:
                SetLightSlot(map, l.X, l.Y, (l.Before ?? l.After)?.Layer ?? WorldLayer.Ground, undo ? l.Before : l.After);
                break;
            case NpcSpawnOp n:
                SetEntryPinAt(map, n.X, n.Y, n.Layer, undo ? n.Before : n.After);
                RefreshNpcRow(n.Before);
                RefreshNpcRow(n.After);
                break;
        }
        SelectedMap!.UpdateRecord(map);
        RepaintTile(op.X, op.Y);
    }

    // A row removal shifts entry indices, so fix up the entry-index keys in every queued NPC-spawn pin op (undo
    // AND redo) IN PLACE — preserving the whole undo history instead of clearing it. Before/After are the entry
    // index pinned at the op's tile: the removed index → null (that entry, and its pin, are gone, so the op
    // degrades to a harmless "clear this tile" no-op), an index past the removed one shifts down by one.
    private void AdjustPinOpsAfterRemoval(int removedIndex)
    {
        ShiftPinOps(_undoStack, removedIndex);
        ShiftPinOps(_redoStack, removedIndex);
    }

    private static void ShiftPinOps(Stack<List<UndoOp>> stack, int removedIndex)
    {
        // The stack's batches are mutable List references; editing an op in place persists without disturbing the
        // stack order, so history depth and CanUndo/CanRedo are untouched.
        foreach (var batch in stack)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                if (batch[i] is NpcSpawnOp op)
                {
                    batch[i] = op with
                    {
                        Before = ShiftPinIndex(op.Before, removedIndex),
                        After = ShiftPinIndex(op.After, removedIndex)
                    };
                }
            }
        }
    }

    private static int? ShiftPinIndex(int? entryIndex, int removedIndex)
    {
        if (entryIndex is not int i) return null;
        if (i == removedIndex) return null;      // the pinned entry was removed — no target remains
        return i > removedIndex ? i - 1 : i;     // entries after the removed one slid down a post
    }
}
