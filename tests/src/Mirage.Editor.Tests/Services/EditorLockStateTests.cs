using Mirage.Editor.Services;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// What the record lists show, given the server's lock table.
///
/// <para>The indicator is "held by another SESSION", not "held". A lock this window took is its own unsaved
/// work and must never gray out the row it belongs to — and a lock another window took is a conflict even
/// when that window is signed in as the same person, which is the case a single-author project meets first.</para>
///
/// <para>The account name survives only to word the tooltip: telling the reader "matt is editing this" when
/// the reader IS matt explains nothing, so that one case is worded differently.</para>
/// </summary>
[TestFixture]
public class EditorLockStateTests
{
    private const string Section = "Maps";

    private static EditorLockState With(params EditorLocksPacket.Held[] held)
    {
        var state = new EditorLockState { MyLogin = "matt", MySession = "mine" };
        state.Apply(new EditorLocksPacket { Locks = held });
        return state;
    }

    [Test]
    public void ARecordThisSessionHolds_IsNotLocked()
    {
        var state = With(new EditorLocksPacket.Held(Section, 70, "matt", "mine"));

        Assert.That(state.IsHeldByOther(Section, 70), Is.False, "Your own unsaved work never locks you out.");
        Assert.That(state.IsHeldByMyAccountElsewhere(Section, 70), Is.False);
    }

    [Test]
    public void ARecordAnotherWindowOfYourAccountHolds_IsLockedAndSaysSo()
    {
        var state = With(new EditorLocksPacket.Held(Section, 70, "matt", "theirs"));

        Assert.That(state.IsHeldByOther(Section, 70), Is.True,
            "Same account, different window — two sets of changes, so the row greys out.");
        Assert.That(state.IsHeldByMyAccountElsewhere(Section, 70), Is.True,
            "And the tooltip has to say which window, because the account name is the reader's own.");
        Assert.That(state.HolderOf(Section, 70), Is.EqualTo("matt"));
    }

    [Test]
    public void ARecordSomebodyElseHolds_IsLockedAndNamesThem()
    {
        var state = With(new EditorLocksPacket.Held(Section, 70, "sera", "theirs"));

        Assert.That(state.IsHeldByOther(Section, 70), Is.True);
        Assert.That(state.IsHeldByMyAccountElsewhere(Section, 70), Is.False, "A name is explanation enough.");
        Assert.That(state.HolderOf(Section, 70), Is.EqualTo("sera"));
    }

    [Test]
    public void ARecordNobodyHolds_IsNotLocked()
    {
        var state = With(new EditorLocksPacket.Held(Section, 70, "sera", "theirs"));

        Assert.That(state.IsHeldByOther(Section, 71), Is.False);
        Assert.That(state.HolderOf(Section, 71), Is.Null);
    }

    [Test]
    public void SectionsDoNotCollide()
    {
        var state = With(new EditorLocksPacket.Held("Maps", 70, "sera", "theirs"));

        Assert.That(state.IsHeldByOther("Items", 70), Is.False);
    }

    [Test]
    public void ClearingDropsTheTableAndThisSessionsIdentity()
    {
        var state = With(new EditorLocksPacket.Held(Section, 70, "sera", "theirs"));
        state.Clear();

        Assert.That(state.IsHeldByOther(Section, 70), Is.False);
        Assert.That(state.MySession, Is.Empty,
            "A stale session id would make the NEXT connection's own locks read as somebody else's.");
        Assert.That(state.MyLogin, Is.Empty);
    }

}
