using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Social;

/// <summary>Locks the access-rank → name color mapping: Monitor orange, Mapper turquoise, Developer
/// royal-blue, Creator amethyst, Player tan. A silent repaint here would shift every overhead and chat
/// name at once, so pin it.
///
/// <para>🔴 <b>Rank and rank only.</b> An operator's color is a permission Core owns, and it has to read
/// the same in every world — a game that could repaint it could disguise one. What a game decides about
/// a name arrives declared instead, which is what the marked color below is.</para></summary>
[TestFixture]
public class PlayerNameColorTests
{
    [TestCase(AdminLevel.Player, GameColor.Tan)]
    [TestCase(AdminLevel.Monitor, GameColor.Orange)]
    [TestCase(AdminLevel.Mapper, GameColor.Turquoise)]
    [TestCase(AdminLevel.Developer, GameColor.RoyalBlue)]
    [TestCase(AdminLevel.Creator, GameColor.Amethyst)]
    public void RankColor(AdminLevel access, int expected)
        => Assert.That(PlayerNameColor.For(access), Is.EqualTo(expected));

    /// <summary>A world that says nothing about marking still draws one, because Core tracks the state
    /// whether or not a game colors it.</summary>
    [Test]
    public void AWorldThatDeclaresNothing_StillHasAMarkedColor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NameTintSet.Plain.MarkedRgb, Is.EqualTo(NameTintSet.MarkedDefaultRgb));
            Assert.That(NameTintSet.Plain.AggressorRgb, Is.EqualTo(NameTintSet.AggressorDefaultRgb));
        });
    }

    /// <summary>⚠ The pulse alternates against the MARKED color rather than a second arbitrary one, so an
    /// aggressor reads as the mark they are about to earn. A game setting one and not the other still
    /// gets a legible pair.</summary>
    [Test]
    public void AGameSetsBothHalvesOfThePulse()
    {
        var tints = new NameTintSet([], NameTintSet.PlainRgb, markedRgb: 0x102030, aggressorRgb: 0x405060);

        Assert.Multiple(() =>
        {
            Assert.That(tints.MarkedRgb, Is.EqualTo(0x102030));
            Assert.That(tints.AggressorRgb, Is.EqualTo(0x405060));
        });
    }
}
