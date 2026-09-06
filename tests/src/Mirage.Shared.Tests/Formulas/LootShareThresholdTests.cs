using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>
/// Who shared a kill: everyone within a quarter of the top damage dealer.
///
/// <para>The same set decides the per-item tag rolls AND the currency split, so this one figure is the
/// whole definition of "earned a cut". Party membership is deliberately not part of it — two strangers who
/// both fought the mob both earned one, and a party member who stood at the back did not.</para>
///
/// <para>These pin the SHAPE rather than the number: that the bar is a fraction of the top dealer, that it
/// admits a real second contributor, and that it still excludes someone who only chipped. Retuning the
/// figure is expected; a bar so tight only the leader clears it, or so loose a bystander does, is not.</para>
/// </summary>
[TestFixture]
public class LootShareThresholdTests
{
    /// <summary>The server's own arithmetic: the bar, clamped so a one-damage kill cannot admit everybody.</summary>
    private static int Bar(int topDamage) =>
        Math.Max(1, (int)(topDamage * Constants.LootDamageContributionThreshold));

    private static bool Shares(int myDamage, int topDamage) => myDamage >= Bar(topDamage);

    [Test]
    public void TheTopDealer_AlwaysShares()
    {
        Assert.That(Shares(myDamage: 1_000, topDamage: 1_000), Is.True);
    }

    /// <summary>A partner who pulled real weight shares the kill. This is the case the bar exists to admit,
    /// and the one a very tight bar quietly refuses.</summary>
    [Test]
    public void AGenuinePartner_Shares()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Shares(myDamage: 800, topDamage: 1_000), Is.True, "80% of the leader is not a bystander");
            Assert.That(Shares(myDamage: 900, topDamage: 1_000), Is.True);
        });
    }

    /// <summary>Somebody who threw one hit at a mob another player killed does not.</summary>
    [Test]
    public void AChipper_DoesNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Shares(myDamage: 50, topDamage: 1_000), Is.False);
            Assert.That(Shares(myDamage: 400, topDamage: 1_000), Is.False, "under half the leader is not a share");
        });
    }

    /// <summary>Zero damage never qualifies, whatever the fraction rounds to. Against a one-damage kill the
    /// bar truncates to 0, and a bar of 0 would let every player on the map roll.</summary>
    [Test]
    public void ZeroDamage_NeverShares()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Shares(myDamage: 0, topDamage: 1), Is.False, "a one-damage kill let the whole map roll");
            Assert.That(Shares(myDamage: 0, topDamage: 1_000), Is.False);
            Assert.That(Bar(1), Is.GreaterThan(0), "the clamp is what stops a zero bar");
        });
    }

    /// <summary>The bar is a fraction of the leader, not a flat amount — it has to scale with the fight.</summary>
    [Test]
    public void TheBarScalesWithTheKill()
    {
        Assert.That(Bar(10_000), Is.EqualTo(Bar(1_000) * 10).Within(10));
    }

    /// <summary>A bar at the very top admits only the leader, which makes every group kill a solo one; a bar
    /// near the bottom pays anyone who landed a hit. Both are failures of the idea, not tunings of it.</summary>
    [Test]
    public void TheThresholdLeavesRoomForASecondContributor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Constants.LootDamageContributionThreshold, Is.LessThan(0.9),
                "so tight that only the top dealer clears it — a partner doing most of the work gets nothing");
            Assert.That(Constants.LootDamageContributionThreshold, Is.GreaterThan(0.25),
                "so loose that chipping a mob somebody else killed earns a share");
        });
    }
}
