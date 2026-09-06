using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Shared.Tests.Protocol;

/// <summary>
/// The marketplace listings a browsing client is sent.
///
/// <para>🔴 <see cref="MarketListing"/> is the wire DTO as well as the file on disk, and the two want
/// opposite things from <see cref="MarketListing.Id"/>: on disk the id is the FILENAME and a copy of it
/// inside the file would be a second, disagreeable source of truth, while on the wire the id is the only
/// handle a buyer has. The wire wins, and the loader defends the file by assigning the id from the
/// filename after deserializing.</para>
///
/// <para>The failure this guards is silent. A listing whose id does not survive the trip arrives as id
/// 0, every row in the browser still looks right, and buying one is answered with "that listing is no
/// longer available" — the server has no listing 0. Nothing logs and nothing throws; the market simply
/// does not work.</para>
///
/// <para>Server-side tests call <c>Buy</c> with an id straight out of the world table, so they pass
/// whatever the wire does. This is the gap between them.</para>
/// </summary>
[TestFixture]
public class MarketWireTests
{
    private static MarketListPacket.Entry Listed() => new()
    {
        Id = 7,
        Seller = "courtney",
        ItemNum = 42,
        Quantity = 3,
        Dur = 17,
        Price = 1250,
        ListedUtc = 1_700_000_000,
    };

    private static MarketListPacket RoundTrip(MarketListPacket packet) =>
        (MarketListPacket)PacketSerializer.TryDeserialize(PacketSerializer.Serialize(packet))!;

    /// <summary>The id a buyer names a listing by survives the trip to the client.</summary>
    [Test]
    public void AListingKeepsItsIdOnTheWire()
    {
        var sent = new MarketListPacket { Listings = [Listed()] };

        var received = RoundTrip(sent);

        Assert.That(received.Listings, Has.Count.EqualTo(1));
        Assert.That(received.Listings[0].Id, Is.EqualTo(7),
            "a listing that arrives as id 0 cannot be bought or canceled — the server has no listing 0");
    }

    /// <summary>Every other field too: a browser prices, names and ages a listing entirely from what it
    /// was sent, so a dropped field is a row that reads wrong rather than one that fails.</summary>
    [Test]
    public void AListingKeepsEveryFieldOnTheWire()
    {
        var sent = Listed();

        var received = RoundTrip(new MarketListPacket { Listings = [sent] }).Listings[0];

        var missing = typeof(MarketListPacket.Entry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Where(p => !Equals(p.GetValue(sent), p.GetValue(received)))
            .Select(p => p.Name)
            .ToArray();

        Assert.That(missing, Is.Empty, "these did not survive the trip to a browsing client");
    }

    /// <summary>The viewer's own login rides along, which is what lets a browser tell its own listings
    /// apart without a second feed — and what stops it offering a Buy button on one of them.</summary>
    [Test]
    public void TheViewerIsToldWhoTheyAre()
    {
        var sent = new MarketListPacket { Listings = [Listed()], MeLogin = "matt2", Open = true, NowUtc = 99 };

        var received = RoundTrip(sent);

        Assert.Multiple(() =>
        {
            Assert.That(received.MeLogin, Is.EqualTo("matt2"));
            Assert.That(received.Open, Is.True);
            Assert.That(received.NowUtc, Is.EqualTo(99));
        });
    }
}
