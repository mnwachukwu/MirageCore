using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Undo, Redo and Cut are refused on a map another session holds.
///
/// <para>Every other control that writes to a map is deadened by <c>IsSelectedLocked</c> in the markup, but
/// these three arrive through a window-level tunnel handler that fires whatever has focus and whatever is
/// disabled. Nothing in the markup can stop them, so the refusal has to live on the view-model.</para>
///
/// <para>The situation is ordinary rather than exotic: you edit a map, save it — which gives the lock back —
/// somebody else claims it, and your undo history is still sitting there. Ctrl+Z then reaches a map
/// somebody else holds.</para>
///
/// <para>Copy is deliberately still allowed. Reading a record takes nothing and locks nothing, which is the
/// whole reason a lock is claimed on dirty rather than on open.</para>
/// </summary>
[TestFixture]
public class LockedMapRefusesHotkeysTests
{
    private const string Section = "Maps";
    private const int MapNum = 1;

    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    /// <summary>A map open in a session signed in as "matt", with one painted tile behind it so there is
    /// history to undo.</summary>
    private static (MapEditorViewModel vm, MapRecord map) Build()
    {
        var data = new EditorDataService();
        var map = new MapRecord { Name = "Yard" };
        Set(data, nameof(EditorDataService.OfflineMaps), new[] { new MapRecord { Name = "(none)" }, map });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });

        var vm = new MapEditorViewModel(data, new EditorConnection())
        {
            Locks = new EditorLockState { MyLogin = "matt", MySession = "mine" },
        };
        vm.LoadOffline();
        vm.SelectedMap = vm.Maps.First(m => m.Index == MapNum);
        vm.SelectedMode = EditorMode.Attribute;
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.FillRun = false;
        vm.TileClicked(new TileClick(3, 2, false, false));
        vm.BlockedBlocksLight = false;
        vm.ConfirmBlockedCommand.Execute(null);
        return (vm, map);
    }

    /// <summary>Hands the map to another session and tells the view-model, as an arriving table would.</summary>
    private static void HeldByAnother(MapEditorViewModel vm)
    {
        vm.Locks!.Apply(new EditorLocksPacket
        {
            Locks = [new EditorLocksPacket.Held(Section, MapNum, "someone-else", "theirs")],
        });
        vm.RefreshLockState();
    }

    [Test]
    public void WhileItIsMine_TheHotkeysWork()
    {
        var (vm, _) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelectedLocked, Is.False);
            Assert.That(vm.UndoCommand.CanExecute(null), Is.True, "the paint above should be undoable");
        });
    }

    [Test]
    public void OnceAnotherSessionHoldsIt_UndoAndRedoAreRefused()
    {
        var (vm, _) = Build();
        Assume.That(vm.UndoCommand.CanExecute(null), Is.True);

        HeldByAnother(vm);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelectedLocked, Is.True);
            Assert.That(vm.UndoCommand.CanExecute(null), Is.False, "Ctrl+Z would edit somebody else's map");
            Assert.That(vm.RedoCommand.CanExecute(null), Is.False, "Ctrl+Y would edit somebody else's map");
        });
    }

    [Test]
    public void OnceAnotherSessionHoldsIt_UndoChangesNothingEvenIfCalled()
    {
        var (vm, map) = Build();
        HeldByAnother(vm);
        var before = map.Tile[3, 2].Type;

        vm.UndoCommand.Execute(null);

        Assert.That(map.Tile[3, 2].Type, Is.EqualTo(before), "the tile came back despite the lock");
    }

    [Test]
    public void OnceAnotherSessionHoldsIt_CutIsRefused()
    {
        var (vm, map) = Build();
        HeldByAnother(vm);
        vm.SelectedAction = EditorAction.Select;
        vm.SelectionRect = new SelectionBox(2, 1, 4, 3);

        vm.CutSelection();

        Assert.That(map.Tile[3, 2].Type, Is.EqualTo(TileType.Blocked),
            "Ctrl+X erased tiles on a map held by another session");
    }

    /// <summary>Copy is not a write. Gating it would take away the one thing a lock is meant to leave
    /// alone, and it sits one line away from Cut.</summary>
    [Test]
    public void OnceAnotherSessionHoldsIt_CopyStillWorks()
    {
        var (vm, map) = Build();
        HeldByAnother(vm);
        vm.SelectedAction = EditorAction.Select;
        vm.SelectionRect = new SelectionBox(2, 1, 4, 3);

        vm.CopySelection();

        Assert.Multiple(() =>
        {
            Assert.That(vm.ClipboardKind, Is.EqualTo(ClipboardKind.Attribute), "reading a locked map is allowed");
            Assert.That(map.Tile[3, 2].Type, Is.EqualTo(TileType.Blocked), "copy changed the map");
        });
    }

    /// <summary>Handing the map back restores the hotkeys — the refusal follows the table rather than
    /// latching.</summary>
    [Test]
    public void WhenTheyGiveItBack_TheHotkeysReturn()
    {
        var (vm, _) = Build();
        HeldByAnother(vm);
        Assume.That(vm.UndoCommand.CanExecute(null), Is.False);

        vm.Locks!.Apply(new EditorLocksPacket { Locks = [] });
        vm.RefreshLockState();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelectedLocked, Is.False);
            Assert.That(vm.UndoCommand.CanExecute(null), Is.True);
        });
    }

    /// <summary>A lock this session took is its own unsaved work and must never lock it out of it.</summary>
    [Test]
    public void MyOwnLock_DoesNotRefuseAnything()
    {
        var (vm, _) = Build();

        vm.Locks!.Apply(new EditorLocksPacket
        {
            Locks = [new EditorLocksPacket.Held(Section, MapNum, "matt", "mine")],
        });
        vm.RefreshLockState();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelectedLocked, Is.False);
            Assert.That(vm.UndoCommand.CanExecute(null), Is.True);
        });
    }
}
