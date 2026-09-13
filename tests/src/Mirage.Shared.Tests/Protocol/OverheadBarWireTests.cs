using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The packet that tells a client what to draw over a head.
///
/// <para>The client is compiled against no bars at all, so this has to survive the trip intact or a game
/// with a health bar renders bare sprites. The keys travel as NAMES rather than schema ordinals: a bar
/// is read once per visible body per frame against a bag that is keyed by name, and an ordinal would
/// have to be turned back into a name on every one of those reads.</para>
/// </summary>
[TestFixture]
public class OverheadBarWireTests
{
    private static T RoundTrip<T>(T sent) where T : class, IPacket
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<T>(), "the packet did not survive the wire at all");
        return (T)back!;
    }

    [Test]
    public void TheBarsSurviveTheTripInDrawOrder()
    {
        var bars = new OverheadBarSet(
        [
            new OverheadBar { ValueKey = "fuel", MaxKey = "fuelMax", Rgb = 0x28C8FF, Ordinal = 1 },
            new OverheadBar { ValueKey = "hull", MaxKey = "hullMax", Rgb = 0xDC2828, Ordinal = 0 },
        ]);

        var back = RoundTrip(PacketBuilder.OverheadBars(bars));

        Assert.Multiple(() =>
        {
            Assert.That(back.Bars.Select(b => b.ValueKey), Is.EqualTo(new[] { "hull", "fuel" }));
            Assert.That(back.Bars[0].MaxKey, Is.EqualTo("hullMax"));
            Assert.That(back.Bars[0].Rgb, Is.EqualTo(0xDC2828), "the color the draw layer is handed");
        });
    }

    [Test]
    public void AGameThatDrawsNothing_SendsAnEmptyList()
        => Assert.That(RoundTrip(PacketBuilder.OverheadBars(OverheadBarSet.Empty)).Bars, Is.Empty);

    /// <summary>🔴 Black is a color a game may legitimately pick, and it is also what a dropped field
    /// reads as. Round-tripping it is the only way to tell the two apart.</summary>
    [Test]
    public void ABlackBarStaysBlackRatherThanBecomingAMissingField()
    {
        var bars = new OverheadBarSet([new OverheadBar { ValueKey = "v", MaxKey = "m", Rgb = 0 }]);

        var back = RoundTrip(PacketBuilder.OverheadBars(bars));

        Assert.Multiple(() =>
        {
            Assert.That(back.Bars, Has.Count.EqualTo(1));
            Assert.That(back.Bars[0].Rgb, Is.Zero);
        });
    }
}
