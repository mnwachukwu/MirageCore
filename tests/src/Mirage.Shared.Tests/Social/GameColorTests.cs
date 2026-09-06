using NUnit.Framework;

namespace Mirage.Shared.Tests.Social;

/// <summary><see cref="GameColor"/> is the single source of truth for the game's 16-color palette and the
/// packed-RGB (0xRRGGBB) representation shared across client and server. These cover the pack/unpack
/// helpers the color picker and guild-color policy both rely on.</summary>
[TestFixture]
public class GameColorTests
{
    [Test]
    public void PackThenUnpack_RoundTrips()
    {
        int rgb = GameColor.Pack(150, 110, 40);
        Assert.That(GameColor.RedOf(rgb), Is.EqualTo(150));
        Assert.That(GameColor.GreenOf(rgb), Is.EqualTo(110));
        Assert.That(GameColor.BlueOf(rgb), Is.EqualTo(40));
    }

    [Test]
    public void Pack_MasksChannelsToByte()
        => Assert.That(GameColor.Pack(0x1FF, 0x2AB, 0x3CD), Is.EqualTo(GameColor.Pack(0xFF, 0xAB, 0xCD)));

    [Test]
    public void Pack_ProducesExpectedLayout()
        => Assert.That(GameColor.Pack(0x12, 0x34, 0x56), Is.EqualTo(0x123456));
}
