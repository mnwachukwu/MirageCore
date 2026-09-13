using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The packet that tells a client what a surface shows.
///
/// <para>The client is compiled against no fields at all, so this has to survive intact or a game's
/// heads-up display is blank. What travels is the DECLARATION, not the numbers: the values arrive as
/// ordinary attribute syncs, so a moving number costs no display traffic and a row cannot disagree with
/// the attribute behind it.</para>
/// </summary>
[TestFixture]
public class DisplayFieldWireTests
{
    private static T RoundTrip<T>(T sent) where T : class, IPacket
    {
        var back = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent));
        Assert.That(back, Is.TypeOf<T>(), "the packet did not survive the wire at all");
        return (T)back!;
    }

    [Test]
    public void TheFieldsSurviveTheTripInDrawOrder()
    {
        var set = new DisplayFieldSet(
        [
            new DisplayField { Surface = "sheet", ValueKey = "gold", LabelKey = "hud.gold", Rgb = 0xFFD700, Ordinal = 1 },
            new DisplayField
            {
                Surface = DisplaySurfaces.Hud, ValueKey = "fuel", MaxKey = "fuelMax",
                LabelKey = "hud.fuel", Rgb = 0x28C8FF, Style = DisplayStyle.Meter, Ordinal = 0,
            },
        ]);

        var back = RoundTrip(PacketBuilder.DisplayFields(set));

        Assert.Multiple(() =>
        {
            Assert.That(back.Fields.Select(f => f.ValueKey), Is.EqualTo(new[] { "fuel", "gold" }));
            Assert.That(back.Fields[0].Surface, Is.EqualTo(DisplaySurfaces.Hud));
            Assert.That(back.Fields[0].MaxKey, Is.EqualTo("fuelMax"));
            Assert.That(back.Fields[0].Style, Is.EqualTo(DisplayStyle.Meter));
            Assert.That(back.Fields[0].Rgb, Is.EqualTo(0x28C8FF));
            Assert.That(back.Fields[1].Surface, Is.EqualTo("sheet"), "a game may name a surface Core never draws");
        });
    }

    [Test]
    public void AGameThatShowsNothing_SendsAnEmptyList()
        => Assert.That(RoundTrip(PacketBuilder.DisplayFields(DisplayFieldSet.Empty)).Fields, Is.Empty);

    /// <summary>🔴 Packets serialize enums as NUMBERS, so a style is only proved to survive by sending a
    /// non-default one and reading it back.</summary>
    [Test]
    public void AStyleSurvivesAsItself()
    {
        var set = new DisplayFieldSet(
            [new DisplayField { ValueKey = "wanted", LabelKey = "hud.wanted", Style = DisplayStyle.Badge }]);

        Assert.That(RoundTrip(PacketBuilder.DisplayFields(set)).Fields[0].Style, Is.EqualTo(DisplayStyle.Badge));
    }

    /// <summary>A row with no caption is a real thing a game declares, and a missing JSON field reads as
    /// null too — so the two have to be told apart by round-tripping one.</summary>
    [Test]
    public void ARowWithNoCaptionStaysWithoutOne()
    {
        var set = new DisplayFieldSet([new DisplayField { ValueKey = "state", Style = DisplayStyle.Badge }]);

        Assert.That(RoundTrip(PacketBuilder.DisplayFields(set)).Fields[0].LabelKey, Is.Null);
    }
}
