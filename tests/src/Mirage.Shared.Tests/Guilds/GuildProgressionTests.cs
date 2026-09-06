using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Guilds;

/// <summary>Guild progression (#10): the XP→level curve, its inverse, and the perk-active gate
/// (level threshold AND tax-paid PerksActive) that every perk site consults.</summary>
[TestFixture]
public class GuildProgressionTests
{
    [Test]
    public void LevelForExp_MapsThresholdsToLevels()
    {
        Assert.That(GuildLeveling.LevelForExp(0), Is.EqualTo(0));
        Assert.That(GuildLeveling.LevelForExp(Constants.GuildLevel1Exp - 1), Is.EqualTo(0));
        Assert.That(GuildLeveling.LevelForExp(Constants.GuildLevel1Exp), Is.EqualTo(1));
        Assert.That(GuildLeveling.LevelForExp(Constants.GuildLevel3Exp), Is.EqualTo(3));
        Assert.That(GuildLeveling.LevelForExp(Constants.GuildLevel5Exp), Is.EqualTo(5));
        Assert.That(GuildLeveling.LevelForExp(Constants.GuildLevel5Exp * 10), Is.EqualTo(5));  // capped at max
    }

    [Test]
    public void ExpForLevel_RoundTripsThresholds()
    {
        Assert.That(GuildLeveling.ExpForLevel(0), Is.EqualTo(0));
        Assert.That(GuildLeveling.ExpForLevel(1), Is.EqualTo(Constants.GuildLevel1Exp));
        Assert.That(GuildLeveling.ExpForLevel(5), Is.EqualTo(Constants.GuildLevel5Exp));
        Assert.That(GuildLeveling.ExpForLevel(99), Is.EqualTo(Constants.GuildLevel5Exp));       // clamp above max
    }

    [Test]
    public void Perks_InactiveForNullGuild()
    {
        Assert.That(GuildPerks.IsActive(null, Constants.GuildPerkLevelDropRate), Is.False);
    }

    [Test]
    public void Perks_RequireLevelAtOrAboveUnlock()
    {
        var g = new GuildRecord { Level = 2, PerksActive = true };
        Assert.That(GuildPerks.IsActive(g, Constants.GuildPerkLevelDropRate), Is.True);        // L1 perk, guild L2
        Assert.That(GuildPerks.IsActive(g, Constants.GuildPerkLevelPreventWear), Is.True);     // L2 perk, guild L2
        Assert.That(GuildPerks.IsActive(g, Constants.GuildPerkLevelBonusExp), Is.False);       // L3 perk, guild L2
    }

    [Test]
    public void Perks_SuppressedWhenTaxUnpaid()
    {
        var g = new GuildRecord { Level = 5, PerksActive = false };
        Assert.That(GuildPerks.IsActive(g, Constants.GuildPerkLevelDropRate), Is.False);
        Assert.That(GuildPerks.IsActive(g, Constants.GuildPerkLevelDoubleDrop), Is.False);
    }
}
