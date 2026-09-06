using Mirage.Server.Core.Net;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// The rule that a record lock belongs to a SESSION and not to an account.
///
/// <para>Two editors signed in as the same person are two sets of unsaved changes, and the way work is
/// silently lost is that both read the same version, both write, and the second wins without either being
/// told. Keying the table by login instead of by connection makes exactly that case invisible — and it is
/// the case a single-author project hits FIRST, because the one person editing has no colleague to conflict
/// with and every window is signed in as them.</para>
///
/// <para>So every assertion below is the same-login pair. A test that used two different accounts would
/// pass against a login-keyed table too, and prove nothing.</para>
/// </summary>
[TestFixture]
public class EditorLockTests
{
    private const string Section = "Maps";
    private const string Login = "matt";

    private static (int Index, string Session) Sess(int index) => (index, $"session-{index}");

    [Test]
    public void ASecondSessionOfTheSameAccount_CannotTakeAHeldRecord()
    {
        var locks = new EditorLockRegistry();
        var a = Sess(1);
        var b = Sess(2);

        Assert.That(locks.TryAcquire(Section, 70, a.Index, Login, a.Session), Is.True,
            "The first session to dirty a record holds it.");
        Assert.That(locks.TryAcquire(Section, 70, b.Index, Login, b.Session), Is.False,
            "A second window signed in as the same person is a second holder, not the same one.");
    }

    [Test]
    public void TheHoldingSession_CanReclaimItsOwnRecord()
    {
        var locks = new EditorLockRegistry();
        var a = Sess(1);

        Assert.That(locks.TryAcquire(Section, 70, a.Index, Login, a.Session), Is.True);
        Assert.That(locks.TryAcquire(Section, 70, a.Index, Login, a.Session), Is.True,
            "Dirtying a record twice is not a conflict with yourself.");
    }

    [Test]
    public void ASecondSessionOfTheSameAccount_CannotReleaseWhatItDoesNotHold()
    {
        var locks = new EditorLockRegistry();
        var a = Sess(1);
        var b = Sess(2);
        locks.TryAcquire(Section, 70, a.Index, Login, a.Session);

        Assert.That(locks.Release(Section, 70, b.Index), Is.False,
            "Otherwise the second window frees the record out from under the one still editing it.");
        Assert.That(locks.HolderOf(Section, 70)?.EditorIndex, Is.EqualTo(a.Index));
    }

    [Test]
    public void DroppingASession_FreesOnlyItsOwnRecords()
    {
        var locks = new EditorLockRegistry();
        var a = Sess(1);
        var b = Sess(2);
        locks.TryAcquire(Section, 70, a.Index, Login, a.Session);
        locks.TryAcquire(Section, 71, b.Index, Login, b.Session);

        Assert.That(locks.ReleaseAll(a.Index), Is.True);
        Assert.That(locks.HolderOf(Section, 70), Is.Null, "The dropped session's record is free.");
        Assert.That(locks.HolderOf(Section, 71)?.EditorIndex, Is.EqualTo(b.Index),
            "The surviving window keeps what it was holding.");
        Assert.That(locks.ReleaseAll(a.Index), Is.False, "Nothing left to free, so nothing to broadcast.");
    }

    [Test]
    public void TheBroadcastTable_CarriesTheSessionAndNotJustTheLogin()
    {
        var locks = new EditorLockRegistry();
        var a = Sess(1);
        var b = Sess(2);
        locks.TryAcquire(Section, 70, a.Index, Login, a.Session);
        locks.TryAcquire(Section, 71, b.Index, Login, b.Session);

        var held = locks.Snapshot().Locks;
        Assert.That(held.Select(h => h.Login).Distinct().ToArray(), Is.EqualTo(new[] { Login }),
            "Both rows are the same account, which is what makes the login useless as an identity.");
        Assert.That(held.Single(h => h.Num == 70).Session, Is.EqualTo(a.Session));
        Assert.That(held.Single(h => h.Num == 71).Session, Is.EqualTo(b.Session));
    }
}
