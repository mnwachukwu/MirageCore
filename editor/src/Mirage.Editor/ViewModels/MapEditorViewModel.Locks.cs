using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol.Packets;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The map editor's half of record locking and live sync.
///
/// <para>It is written separately from <see cref="EditorViewModelBase{TRow}"/>, which the other eight
/// editors share, because a map row is not one of those: it holds a whole <see cref="Mirage.Shared.Records.MapRecord"/>
/// rather than mirroring fields, and announces its own changes. The rules are the same ones — claim on dirty,
/// give back on clean, take whatever the server pushes — only the plumbing differs.</para>
/// </summary>
public sealed partial class MapEditorViewModel
{
    private const string LockSection = "Maps";

    /// <summary>The shared table, assigned by the shell. Null offline.</summary>
    public EditorLockState? Locks { get; set; }

    /// <summary>Claims a map the moment it goes dirty and gives it back when it comes clean, which is what
    /// makes the table name only people with changes in hand.
    ///
    /// <para>Driven from the one row subscription in <c>HookMaps</c>, which every loader rebuilds along with
    /// the rows themselves — a claim keyed on anything a reload does not rebuild goes quiet after the first
    /// load and the map is edited with no lock behind it.</para>
    ///
    /// <para>Offline there is nobody to tell.</para></summary>
    private void ClaimOrReleaseLock(object? sender)
    {
        if (sender is not MapRowViewModel row || Locks is null || !_conn.IsConnected) return;
        _ = row.IsDirty
            ? _conn.SendLockAsync(LockSection, row.Index)
            : _conn.SendUnlockAsync(LockSection, row.Index);
    }

    /// <summary>Re-reads every row's indicator from the table.</summary>
    public void RefreshLockState()
    {
        if (Locks is null) return;
        foreach (var row in Maps)
        {
            string? holder = Locks.HolderOf(LockSection, row.Index);
            row.LockHolder = holder is null ? ""
                : Locks.IsHeldByMyAccountElsewhere(LockSection, row.Index)
                    ? EditorStrings.Format(EditorStrings.Common_LockHeldByYourOtherSession, ("Holder", holder))
                    : holder;
            row.LockedByOther = Locks.IsHeldByOther(LockSection, row.Index);
        }
        OnPropertyChanged(nameof(IsSelectedLocked));
        // Undo and Redo reach the map by hotkey, past every disabled control.
        UpdateUndoRedo();
    }

    /// <summary>Whether the open map is held by somebody else — the canvas and both side panels go dead,
    /// and the edit hotkeys stop reaching it.</summary>
    public bool IsSelectedLocked => SelectedMap?.LockedByOther == true;

    /// <summary>Takes a map the server pushed after somebody else saved it. A row this session has dirtied is
    /// one it holds the lock on, so a push and local edits cannot both exist — but the check stays, because
    /// silently discarding somebody's work on a wrong assumption is the one outcome worth ruling out.</summary>
    public void OnMapLivePushed(SendMapPacket p)
    {
        var row = Maps.FirstOrDefault(m => m.Index == p.MapNum);
        if (row is null || row.IsDirty) return;
        // LoadRecord announces Record, and the selected row is already subscribed to that — so the open
        // map redraws from this without a second call.
        row.LoadRecord(EditorDataService.MapRecordFromPacket(p));
        RaiseMapContentChanged(p.MapNum);
    }
}
