using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// What one viewer is told about a body's attributes.
///
/// <para>🔴 The filter runs once, on the sending side. There is no second chance: a value written to a
/// socket has been disclosed, whatever the client does with it afterwards, so every case where the
/// projection lets something through is a leak rather than a display bug. These pin the four answers —
/// hidden, owner-only, public, and undeclared — from the outside, because the failure is silent in
/// every direction: an over-permissive filter looks exactly like a correct one to the player it
/// favours.</para>
/// </summary>
[TestFixture]
public class AttributeSyncProjectionTests
{
    private static readonly EntityHandle Who = EntityHandle.ForPlayer(3);

    private static AttributeSchema Schema() => new AttributeSchema.Builder()
        .Declare("seed", AttributeVisibility.None)
        .Declare("gold", AttributeVisibility.Owner)
        .Declare("hp", AttributeVisibility.Viewport)
        .Build();

    private static AttributeBag Bag() => new AttributeBag()
        .Set("seed", 99)
        .Set("gold", 250)
        .Set("hp", 40)
        .Set("undeclared", true);

    private static string[] KeysIn(AttributeSchema schema, AttributeVisibility viewer)
    {
        var packet = PacketBuilder.AttributeSync(Who, Bag(), schema, viewer);
        if (packet is null) return [];
        return [.. packet.Set.Select(e => schema.TryGet(e.Ordinal, out var d) ? d.Key : "?")];
    }

    [Test]
    public void AnOnlooker_SeesOnlyThePublicKeys()
    {
        Assert.That(KeysIn(Schema(), AttributeVisibility.Viewport), Is.EqualTo(new[] { "hp" }));
    }

    [Test]
    public void TheOwner_SeesTheirOwnKeysAndThePublicOnes()
    {
        Assert.That(KeysIn(Schema(), AttributeVisibility.Owner), Is.EquivalentTo(new[] { "gold", "hp" }));
    }

    /// <summary>The two that must never appear anywhere: a key declared hidden, and a key never
    /// declared at all. Both are server-side state, and the second is the fail-closed default — a game
    /// that forgets to think about a key leaks nothing.</summary>
    [Test]
    public void AHiddenKeyAndAnUndeclaredOne_ReachNobody()
    {
        Assert.Multiple(() =>
        {
            foreach (var viewer in new[] { AttributeVisibility.Owner, AttributeVisibility.Viewport })
            {
                Assert.That(KeysIn(Schema(), viewer), Does.Not.Contain("seed"), $"{viewer} saw a hidden key");
                Assert.That(KeysIn(Schema(), viewer), Does.Not.Contain("undeclared"), $"{viewer} saw an undeclared key");
            }
        });
    }

    [Test]
    public void AWorldWhoseGameDeclaredNothing_SendsNothingAtAll()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PacketBuilder.AttributeSync(Who, Bag(), AttributeSchema.Empty, AttributeVisibility.Owner),
                Is.Null, "Core alone must put no attribute traffic on the wire");
            Assert.That(PacketBuilder.AttributeSync(Who, new AttributeBag(), Schema(), AttributeVisibility.Owner),
                Is.Null, "and a body with no values is nothing to say either");
        });
    }

    /// <summary>A changed key the viewer may not see is dropped here, so a caller syncing "what just
    /// changed" never has to ask who is allowed to know.</summary>
    [Test]
    public void SyncingAChangedSet_StillFiltersIt()
    {
        var schema = Schema();
        var packet = PacketBuilder.AttributeSync(Who, Bag(), schema, AttributeVisibility.Viewport,
                                                 keys: ["gold", "seed", "hp"]);

        Assert.That(packet!.Set.Select(e => e.Ordinal), Is.EqualTo(new[] { 2 }), "only hp is public");
    }

    [Test]
    public void SyncingAChangedSetTheViewerCannotSee_SendsNothing()
    {
        Assert.That(PacketBuilder.AttributeSync(Who, Bag(), Schema(), AttributeVisibility.Viewport, keys: ["gold"]),
                    Is.Null);
    }

    /// <summary>Entries ride in ordinal order, so the same change is the same bytes however the keys
    /// happened to be set — the property the bag's own writer keeps for a world file, kept here for a
    /// captured line.</summary>
    [Test]
    public void EntriesAreInOrdinalOrder_WhateverOrderTheKeysCameIn()
    {
        var schema = Schema();
        var packet = PacketBuilder.AttributeSync(Who, Bag(), schema, AttributeVisibility.Owner,
                                                 keys: ["hp", "gold"]);

        Assert.That(packet!.Set.Select(e => e.Ordinal), Is.Ordered);
    }

    [Test]
    public void NobodyInParticular_IsToldNothing()
    {
        Assert.That(PacketBuilder.AttributeSync(EntityHandle.None, Bag(), Schema(), AttributeVisibility.Owner),
                    Is.Null);
    }
}
