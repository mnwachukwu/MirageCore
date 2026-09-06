using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// A stack that grows past what an int holds stops at the ceiling instead of wrapping.
///
/// <para>🔴 Gold is a plain <c>int</c> with no cap of its own, so a large pile plus a large gift can exceed
/// one — and an overflow turns a fortune into a debt, silently and with no error anywhere. It is reachable
/// from the account editor, which can hand out any amount a stack can hold, and from any ordinary pickup on
/// top of a pile already near the ceiling.</para>
/// </summary>
[TestFixture]
public class StacksDoNotWrapTests
{
    [Test]
    public void AnOrdinaryAdd_JustAdds()
    {
        var slot = new PlayerInvSlot { Num = 1, Quantity = 250 };

        slot.AddQuantity(100_000_000);

        Assert.That(slot.Quantity, Is.EqualTo(100_000_250));
    }

    [Test]
    public void AnAddPastTheCeiling_StopsAtIt()
    {
        var slot = new PlayerInvSlot { Num = 1, Quantity = 2_000_000_000 };

        slot.AddQuantity(2_000_000_000);

        Assert.That(slot.Quantity, Is.EqualTo(int.MaxValue), "the pile wrapped into a debt");
    }

    [Test]
    public void TheCeilingItself_Holds()
    {
        var slot = new PlayerInvSlot { Num = 1, Quantity = int.MaxValue };

        slot.AddQuantity(1);

        Assert.That(slot.Quantity, Is.EqualTo(int.MaxValue));
    }

    /// <summary>The editor's own give is the biggest single amount anything can hand over, so it has to be
    /// addable to a full pile without wrapping.</summary>
    [Test]
    public void TheLargestGift_OntoTheLargestPile_Saturates()
    {
        var slot = new PlayerInvSlot { Num = 1, Quantity = int.MaxValue };

        slot.AddQuantity(int.MaxValue);

        Assert.That(slot.Quantity, Is.EqualTo(int.MaxValue));
    }

    /// <summary>Taking is a subtraction elsewhere, but a negative amount reaching here must not drive a
    /// stack below empty.</summary>
    [Test]
    public void ANegativeAmount_CannotDriveTheStackBelowEmpty()
    {
        var slot = new PlayerInvSlot { Num = 1, Quantity = 10 };

        slot.AddQuantity(-50);

        Assert.That(slot.Quantity, Is.Zero);
    }
}
