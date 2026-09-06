using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// The other end of an item lock: the table the server broadcasts, arriving at a real item editor.
///
/// <para>Only the padlock is per-row. What deadens the form is <see cref="ItemEditorViewModel.IsSelectedLocked"/>,
/// which follows the SELECTED row — so both have to move, and they move on different signals: the table
/// arriving, and the selection changing while the table stands still.</para>
///
/// <para>The lock table names a SESSION. Two windows signed in as one account hold two sets of unsaved
/// changes and shut each other out, which is the case a single author meets.</para>
/// </summary>
[TestFixture]
public class ItemLockIndicatorTests
{
    private const string Section = "Items";
    private const int Held = 3, Free = 4;

    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    private static ItemEditorViewModel Build()
    {
        var data = new EditorDataService();
        Set(data, nameof(EditorDataService.OfflineItems), new[]
        {
            new ItemRecord(), new ItemRecord { Name = "Rusted Dagger" },
            new ItemRecord { Name = "Oak Shield" }, new ItemRecord { Name = "Ashwood Bow" },
            new ItemRecord { Name = "Bent Stick" },
        });
        Set(data, nameof(EditorDataService.OfflineClasses), new[] { new ClassRecord() });
        Set(data, nameof(EditorDataService.OfflineSpells), new[] { new SpellRecord() });

        var vm = new ItemEditorViewModel(data, new EditorConnection())
        {
            Locks = new EditorLockState { MyLogin = "alice", MySession = "session-alice" },
        };
        vm.LoadOffline();
        return vm;
    }

    /// <summary>The table exactly as <c>EditorLockRegistry.Snapshot</c> builds it.</summary>
    private static void TableSays(ItemEditorViewModel vm, string login, string session)
    {
        vm.Locks!.Apply(new EditorLocksPacket { Locks = [new EditorLocksPacket.Held(Section, Held, login, session)] });
        vm.RefreshLockState();
    }

    private static ItemRowViewModel Row(ItemEditorViewModel vm, int num) => vm.Items.First(i => i.Index == num);

    [Test]
    public void AnItemAnotherSessionHolds_ShowsThePadlockAndNamesTheHolder()
    {
        var vm = Build();

        TableSays(vm, "bob", "session-bob");

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, Held).LockedByOther, Is.True);
            Assert.That(Row(vm, Held).LockHolder, Is.EqualTo("bob"));
            Assert.That(Row(vm, Free).LockedByOther, Is.False, "one item is claimed, not the section");
        });
    }

    [Test]
    public void OpeningAHeldItem_DeadensTheForm()
    {
        var vm = Build();
        TableSays(vm, "bob", "session-bob");

        vm.SelectedItem = Row(vm, Held);

        Assert.That(vm.IsSelectedLocked, Is.True, "the form stays live on an item somebody else is editing");
    }

    /// <summary>The form follows the selection, not just the arriving table — moving to a free item has to
    /// bring it back.</summary>
    [Test]
    public void MovingToAFreeItem_BringsTheFormBack()
    {
        var vm = Build();
        TableSays(vm, "bob", "session-bob");
        vm.SelectedItem = Row(vm, Held);
        Assume.That(vm.IsSelectedLocked, Is.True);

        vm.SelectedItem = Row(vm, Free);

        Assert.That(vm.IsSelectedLocked, Is.False);
    }

    /// <summary>The table can arrive while the item is already open, which is what happens when somebody
    /// else starts typing into it.</summary>
    [Test]
    public void AClaimArrivingOnTheOpenItem_DeadensItWhereItStands()
    {
        var vm = Build();
        vm.SelectedItem = Row(vm, Held);
        Assume.That(vm.IsSelectedLocked, Is.False);

        TableSays(vm, "bob", "session-bob");

        Assert.That(vm.IsSelectedLocked, Is.True);
    }

    [Test]
    public void MyOwnClaim_LeavesTheFormAlone()
    {
        var vm = Build();
        vm.SelectedItem = Row(vm, Held);

        TableSays(vm, "alice", "session-alice");

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, Held).LockedByOther, Is.False, "your own unsaved work is not a conflict");
            Assert.That(vm.IsSelectedLocked, Is.False);
        });
    }

    /// <summary>Same account, another window. A conflict like any other, worded so the reader is not told
    /// that they themselves are editing it.</summary>
    [Test]
    public void MyOtherWindowsClaim_LocksMeOutAndSaysWhich()
    {
        var vm = Build();
        vm.SelectedItem = Row(vm, Held);

        TableSays(vm, "alice", "session-alice-2");

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsSelectedLocked, Is.True);
            Assert.That(Row(vm, Held).LockHolder, Is.Not.EqualTo("alice"),
                "naming the reader explains nothing — this case is worded differently");
            Assert.That(Row(vm, Held).LockHolder, Does.Contain("alice"));
        });
    }

    [Test]
    public void WhenTheyGiveItBack_TheFormReturns()
    {
        var vm = Build();
        vm.SelectedItem = Row(vm, Held);
        TableSays(vm, "bob", "session-bob");
        Assume.That(vm.IsSelectedLocked, Is.True);

        vm.Locks!.Apply(new EditorLocksPacket { Locks = [] });
        vm.RefreshLockState();

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, Held).LockedByOther, Is.False);
            Assert.That(Row(vm, Held).LockHolder, Is.Empty);
            Assert.That(vm.IsSelectedLocked, Is.False);
        });
    }
}
