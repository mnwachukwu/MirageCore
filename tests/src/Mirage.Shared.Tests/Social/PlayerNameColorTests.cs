using NUnit.Framework;

namespace Mirage.Shared.Tests.Social;

/// <summary>Locks the access-rank → name color mapping after the chat-overhaul recolor: each rank maps to
/// an appended color slot (Monitor orange, Mapper turquoise, Developer royal-blue, Creator amethyst, Player
/// tan), with PK keeping the QB bright red. A silent repaint here would shift every overhead and chat-name
/// color, so pin it.</summary>
[TestFixture]
public class PlayerNameColorTests
{
    [Test]
    public void PkFlag_WinsOverAccess()
        => Assert.That(PlayerNameColor.For(showAsPk: true, AdminLevel.Creator), Is.EqualTo(GameColor.BrightRed));

    [TestCase(AdminLevel.Player, GameColor.Tan)]
    [TestCase(AdminLevel.Monitor, GameColor.Orange)]
    [TestCase(AdminLevel.Mapper, GameColor.Turquoise)]
    [TestCase(AdminLevel.Developer, GameColor.RoyalBlue)]
    [TestCase(AdminLevel.Creator, GameColor.Amethyst)]
    public void RankColor(AdminLevel access, int expected)
        => Assert.That(PlayerNameColor.For(showAsPk: false, access), Is.EqualTo(expected));
}
