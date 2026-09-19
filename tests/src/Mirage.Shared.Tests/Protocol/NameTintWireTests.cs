using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The packet that tells a client what a creature's name means.
///
/// <para>The client is compiled against no rule at all, so this has to survive the trip intact or every
/// creature in every world comes out the same color — which reads as a finished world rather than as a
/// dropped packet. The keys travel as NAMES for the same reason a bar's do: the rule is asked once per
/// visible creature per frame against a bag keyed by name.</para>
/// </summary>
[TestFixture]
public class NameTintWireTests
{
    private static T RoundTrip<T>(T sent) where T : class, IPacket
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<T>(), "the packet did not survive the wire at all");
        return (T)back!;
    }

    [Test]
    public void TheTintsSurviveTheTripInTheOrderTheyAreAsked()
    {
        var tints = new NameTintSet(
        [
            new NameTint { Key = "hostile", Rgb = 0xFFFFFF, Ordinal = 1 },
            new NameTint { Key = "guard", Rgb = 0xFFFF00, Ordinal = 0 },
        ], otherwiseRgb: 0x00FF00);

        var back = RoundTrip(PacketBuilder.NameTints(tints));

        Assert.Multiple(() =>
        {
            Assert.That(back.Tints.Select(t => t.Key), Is.EqualTo(new[] { "guard", "hostile" }),
                "the first match wins, so the order is the rule and has to arrive intact");
            Assert.That(back.Tints[0].Rgb, Is.EqualTo(0xFFFF00));
            Assert.That(back.OtherwiseRgb, Is.EqualTo(0x00FF00));
        });
    }

    [Test]
    public void AGameThatColorsNothing_SendsAnEmptyListAndThePlainColor()
    {
        var back = RoundTrip(PacketBuilder.NameTints(NameTintSet.Plain));

        Assert.Multiple(() =>
        {
            Assert.That(back.Tints, Is.Empty);
            Assert.That(back.OtherwiseRgb, Is.EqualTo(NameTintSet.PlainRgb));
        });
    }

    /// <summary>🔴 Black is a color a game may legitimately pick, and it is also what a dropped field
    /// reads as. Round-tripping it is the only way to tell the two apart — and a name drawn in black on
    /// a dark map is invisible rather than merely wrong.</summary>
    [Test]
    public void ABlackTintStaysBlackRatherThanBecomingAMissingField()
    {
        var tints = new NameTintSet([new NameTint { Key = "k", Rgb = 0 }], otherwiseRgb: 0);

        var back = RoundTrip(PacketBuilder.NameTints(tints));

        Assert.Multiple(() =>
        {
            Assert.That(back.Tints, Has.Count.EqualTo(1));
            Assert.That(back.Tints[0].Rgb, Is.Zero);
            Assert.That(back.OtherwiseRgb, Is.Zero);
        });
    }
}
