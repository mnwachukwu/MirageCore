namespace Mirage.Shared.Extensibility;

/// <summary>
/// What a game may ask the world to do.
///
/// <para><b>This is the other half of the module seam, and the half everything else has been waiting
/// for.</b> A module declares what it adds through <see cref="ICoreBuilder"/> and is told what happens
/// through <see cref="IWorldObserver"/> — and until it is handed one of these it can act on none of it.
/// Every mechanism the engine keeps for a game to drive is reachable from here and from nowhere
/// else.</para>
///
/// <para><b>A curated surface, not the engine's internals.</b> What is on it is what a game has a
/// legitimate reason to ask for, named in the engine's own terms. A game that needs something absent is
/// a reason to add a member and say what it means, not a reason to hand out the systems behind it —
/// those change shape, and a game written against them would break for reasons that are nobody's
/// fault.</para>
///
/// <para><b>Every method is safe to call with a body that is not there.</b> A handle outlives what it
/// names: a player logs out, an NPC despawns, a slot is reused. So nothing here throws for an absent
/// body — it answers false, null, or does nothing, and a game that cares asks
/// <see cref="IsInWorld"/>.</para>
///
/// <para><b>Called on the game thread.</b> An observer is raised on it, and
/// <see cref="ICoreModule.Start"/> runs before the loop is started, so both are safe. Work that arrives
/// on any other thread belongs on <see cref="ITickWork"/>, which the loop drives.</para>
/// </summary>
public interface IWorld
{
    // ── Who is here ───────────────────────────────────────────────────────────

    /// <summary>Whether this handle still names a body that is in the world.</summary>
    bool IsInWorld(EntityHandle who);

    /// <summary>Where that body is, or <see cref="WorldPlace.Nowhere"/> when it is not in the world.</summary>
    WorldPlace PlaceOf(EntityHandle who);

    // ── What a body carries ───────────────────────────────────────────────────

    /// <summary>The body's attribute bag, or null when it is not in the world.
    ///
    /// <para>Read freely. Writing through the bag directly changes the value and tells nobody — use
    /// <see cref="SetAttribute"/> for anything a client should see.</para></summary>
    AttributeBag? AttributesOf(EntityHandle who);

    /// <summary>Sets one attribute and ships it to everyone entitled to see it. False when the body is
    /// not there.</summary>
    bool SetAttribute(EntityHandle who, string key, AttributeValue value);

    /// <summary>Sets several at once and ships them in one packet, which is what a rule that moves three
    /// numbers together wants.</summary>
    bool SetAttributes(EntityHandle who, IReadOnlyCollection<KeyValuePair<string, AttributeValue>> values);

    /// <summary>Removes an attribute. A key a body does not have is not an error.</summary>
    bool RemoveAttribute(EntityHandle who, string key);

    // ── What a body is doing ──────────────────────────────────────────────────
    //
    // Five timed states the engine already acts on and had no way to enter. Each takes SECONDS FROM NOW
    // rather than a deadline, because that is the question every one of them answers and it needs no
    // clock agreed between a game and the engine. Zero or less clears the state.

    /// <summary>Marks this body as engaged for the next <paramref name="seconds"/>.
    ///
    /// <para>The engine already knows what an engaged body looks like: its overhead bars appear and take
    /// a border, the state travels to every observer, and the client floats a line as it is entered and
    /// left. What engagement MEANS is a game's — a fight, a negotiation, a race.</para></summary>
    void SetEngaged(EntityHandle who, int seconds);

    /// <summary>Puts this body out of action for the next <paramref name="seconds"/>: it cannot move,
    /// trade, bank, shop, drop or repair, it is walked over rather than around, NPCs stop noticing it,
    /// and its own client shows the wait.</summary>
    void SetDowned(EntityHandle who, int seconds);

    /// <summary>Marks this body for the next <paramref name="seconds"/> — a flag other players can see
    /// on its name, and that NPC target acquisition and the movement rules read.</summary>
    void SetMarked(EntityHandle who, int seconds);

    /// <summary>Flags this body as the one who started it, for the next <paramref name="seconds"/>. The
    /// client draws the flashing name.</summary>
    void SetAggressor(EntityHandle who, int seconds);

    /// <summary>Starts this body's action cooldown, which the overhead bars draw as a depleting row.
    /// <paramref name="seconds"/> is how long the bar takes to empty.</summary>
    void SetActionCooldown(EntityHandle who, int seconds);

    // ── What can be done to a body ────────────────────────────────────────────

    /// <summary>Ends this body's turn in the world and puts it back where the game's death policies say.
    /// Returns whether it happened; a policy may refuse.</summary>
    bool Kill(EntityHandle who, EntityHandle killer = default, string causeKey = "");

    /// <summary>Puts this body somewhere. False for a destination that is not a real tile.</summary>
    bool Warp(EntityHandle who, WorldPlace to);

    /// <summary>Puts items in this body's bag. <paramref name="quantity"/> is the amount for something
    /// that stacks and is ignored otherwise.</summary>
    void Give(EntityHandle who, int itemNum, int quantity = 1);

    /// <summary>Takes items out of this body's bag, worn ones included.</summary>
    void Take(EntityHandle who, int itemNum, int quantity = 1);

    /// <summary>Takes a body a <see cref="ILingerPolicy"/> kept out of the world now, rather than when
    /// its deadline passes.</summary>
    void ReleaseGhost(EntityHandle who);

    // ── The world itself ──────────────────────────────────────────────────────

    /// <summary>Puts a stain on the ground, which dries on its own and is drawn to everyone who can see
    /// the tile. What stains and why is a game's.</summary>
    void Stain(WorldPlace at, int size, WorldLayer layer, float amount);

    /// <summary>Every record of one of this game's own families, 1-based, blanks included. Empty for a
    /// family this world does not hold.</summary>
    IReadOnlyList<AttributeBag> RecordsOf(string familyId);

    /// <summary>One record of one of this game's own families, or null for a slot that is not there.</summary>
    AttributeBag? RecordAt(string familyId, int num);
}
