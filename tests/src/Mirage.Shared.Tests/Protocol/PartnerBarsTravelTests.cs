using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// A partner's bars are read on the SERVER and sent, because the client has no way to read them.
///
/// <para>🔴 The whole point of a party overlay is somebody you cannot see, and a client holds
/// attributes only for bodies in its own neighbourhood. So the rows cannot be derived at the far end:
/// if they do not travel, the overlay is blank for exactly the partner it exists to show.</para>
/// </summary>
[TestFixture]
public class PartnerBarsTravelTests
{
    private static OverheadBarSet TwoBars => new(
    [
        new OverheadBar { ValueKey = "stamina", MaxKey = "staminaMax", Rgb = 0x00FF00 },
        new OverheadBar { ValueKey = "focus", MaxKey = "focusMax", Rgb = 0x0000FF },
    ]);

    private static PlayerRecord Partner(params (string Key, long Value)[] values)
    {
        var p = new PlayerRecord { Name = "Quarry", Map = 3, X = 4, Y = 5 };
        foreach (var (key, value) in values) p.Attributes.Set(key, value);
        return p;
    }

    private static PartyPartnerPacket RoundTrip(PartyPartnerPacket sent)
        => (PartyPartnerPacket)PacketSerializer.TryDeserialize(PacketSerializer.Serialize(sent))!;

    [Test]
    public void OneFractionPerDeclaredBar_SurvivesTheWire()
    {
        var packet = PacketBuilder.PartyPartner(
            2, Partner(("stamina", 10), ("staminaMax", 40), ("focus", 3), ("focusMax", 4)),
            TwoBars, combatExpiresAt: 0, pkGraceUntilUtc: 0, nowMs: 0, combatDurationMs: 0);

        var back = RoundTrip(packet);

        Assert.Multiple(() =>
        {
            Assert.That(back.Bars, Has.Count.EqualTo(2), "one per declared bar, in declaration order");
            Assert.That(back.Bars[0], Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(back.Bars[1], Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(back.Name, Is.EqualTo("Quarry"));
        });
    }

    /// <summary>A bar this body says nothing about arrives negative, which is how the row says "do not
    /// draw me" everywhere else. Zero would be a partner at death's door rather than a partner the game
    /// has no opinion about.</summary>
    [Test]
    public void ABarTheBodyHasNothingToSayAbout_ArrivesAbsentRatherThanEmpty()
    {
        var packet = PacketBuilder.PartyPartner(
            2, Partner(("stamina", 10), ("staminaMax", 40)),
            TwoBars, combatExpiresAt: 0, pkGraceUntilUtc: 0, nowMs: 0, combatDurationMs: 0);

        var back = RoundTrip(packet);

        Assert.Multiple(() =>
        {
            Assert.That(back.Bars[0], Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(back.Bars[1], Is.LessThan(0f), "the second bar has no values behind it");
        });
    }

    /// <summary>A game that declares no bars sends none, and the overlay is a name and a way out.</summary>
    [Test]
    public void AGameWithNoBars_SendsNoRows()
    {
        var packet = PacketBuilder.PartyPartner(
            2, Partner(), OverheadBarSet.Empty,
            combatExpiresAt: 0, pkGraceUntilUtc: 0, nowMs: 0, combatDurationMs: 0);

        Assert.That(RoundTrip(packet).Bars, Is.Empty);
    }

    /// <summary>The teardown notify carries nothing, and must not read as a partner with empty bars.</summary>
    [Test]
    public void TheTeardownNotify_IsStillEmpty()
    {
        var back = RoundTrip(PacketBuilder.PartyCleared());

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.Empty, "an empty name is what tears the overlay down");
            Assert.That(back.Bars, Is.Empty);
        });
    }
}
