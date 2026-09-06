using Mirage.Server.Core.GameLogic;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>
/// The one place in the engine that divides currency.
///
/// <para>Splitting a gold drop across everyone who earned it replaced tagging the whole purse to a
/// single roll winner, and the arithmetic is the part with edges: gold is integral, so a party that
/// does not divide evenly has to either lose a coin or invent one, and a purse smaller than the
/// party has to not round everybody to nothing.</para>
///
/// <para>WHO gets a larger share is decided by a roll at the call site and is deliberately not this
/// function's business — which is exactly what leaves the arithmetic deterministic and testable.
/// These tests pin the shape of the split; they say nothing about the draw.</para>
/// </summary>
public class CurrencySplitTests
{
    [TestCase(100, 1)]
    [TestCase(100, 2)]
    [TestCase(100, 3)]
    [TestCase(10, 3)]
    [TestCase(10, 4)]
    [TestCase(1, 4)]
    [TestCase(7, 7)]
    [TestCase(1_000_000, 9)]
    public void ConservesTheTotalExactly(int total, int recipients)
    {
        Assert.That(CombatSystem.SplitCurrency(total, recipients).Sum(), Is.EqualTo(total));
    }

    [Test]
    public void SplitsEvenlyWhenItDivides()
    {
        Assert.That(CombatSystem.SplitCurrency(100, 4), Is.EqualTo(new[] { 25, 25, 25, 25 }));
    }

    [Test]
    public void PutsTheLargerShareFirst()
    {
        // The caller orders the recipients by ROLL, so "first" means "won the draw for the odd coin".
        // This only decides how many larger shares there are.
        Assert.Multiple(() =>
        {
            // 3 apiece leaves ONE coin over, so exactly one player gets the 4.
            Assert.That(CombatSystem.SplitCurrency(10, 3), Is.EqualTo(new[] { 4, 3, 3 }));
            // 3 apiece leaves TWO over, so TWO players get a 4 — the spare coins go to different
            // people rather than both to one winner.
            Assert.That(CombatSystem.SplitCurrency(11, 3), Is.EqualTo(new[] { 4, 4, 3 }));
        });
    }

    [Test]
    public void PaysSomebodyWhenThereIsLessGoldThanPlayers()
    {
        // The whole point of the remainder rule: 3 gold among 4 is not "nothing for anyone". Which
        // three of the four is a roll, not an ordering property of this function.
        Assert.That(CombatSystem.SplitCurrency(3, 4), Is.EqualTo(new[] { 1, 1, 1, 0 }));
    }

    [Test]
    public void OneRecipientTakesItAll()
    {
        Assert.That(CombatSystem.SplitCurrency(57, 1), Is.EqualTo(new[] { 57 }));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void NothingToSplitPaysNothing(int total)
    {
        // Not a reachable state — a currency line is floored at 1 before it gets here — so this is
        // defensive. It must still never hand out negative gold.
        Assert.That(CombatSystem.SplitCurrency(total, 3), Is.EqualTo(new[] { 0, 0, 0 }));
    }

    [Test]
    public void NoRecipientsIsEmptyRatherThanACrash()
    {
        Assert.That(CombatSystem.SplitCurrency(100, 0), Is.Empty);
    }

    [Test]
    public void SharesNeverDifferByMoreThanOne()
    {
        // The fairness property, checked across a spread rather than at one chosen point: no
        // contributor can be shorted by more than the single coin that would not divide.
        for (int total = 0; total < 200; total++)
            for (int recipients = 1; recipients <= 8; recipients++)
            {
                int[] shares = CombatSystem.SplitCurrency(total, recipients);
                Assert.That(shares.Max() - shares.Min(), Is.LessThanOrEqualTo(1),
                    $"{total} among {recipients} spread by more than one coin: [{string.Join(", ", shares)}]");
            }
    }
}
