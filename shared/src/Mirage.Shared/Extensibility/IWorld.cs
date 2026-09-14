using Mirage.Shared.Protocol;

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

    // ── What a game says ──────────────────────────────────────────────────────

    /// <summary>
    /// Says something to one player, in the game's own words.
    ///
    /// <para><b>Literal text, and the only path here that is.</b> Everything else the server says is a
    /// KEY looked up per recipient, so the engine's own lines arrive in each player's language. A game's
    /// words are not in that table and cannot be added to it — the table is the engine's vocabulary, and
    /// a module that could write into it would be able to change what the engine says. So what a game
    /// says travels as it was written, and a game wanting several languages keeps its own table and picks
    /// the line before calling this.</para>
    ///
    /// <para>Does nothing for a body that is not a player in the world, like everything else here.</para>
    /// </summary>
    /// <param name="who">The player to say it to.</param>
    /// <param name="text">What to say, already in the words the player will read.</param>
    /// <param name="channel">Which of the client's chat filters it belongs under. The default is the one
    /// for feedback about your own character, which is what a rule reacting to what you just did is.</param>
    /// <param name="color">What color to draw it, from <see cref="GameColor"/>.</param>
    void Tell(EntityHandle who, string text,
              ChatChannel channel = ChatChannel.System, int color = GameColor.White);

    /// <summary>
    /// Says something to everybody in the world.
    ///
    /// <para>For the handful of things that are genuinely everyone's business — a season turning, a
    /// server notice, somebody finishing the thing only one person can finish. A game that announces
    /// ordinary events this way is a game whose chat log is unreadable.</para>
    /// </summary>
    void TellEveryone(string text, ChatChannel channel = ChatChannel.System,
                      int color = GameColor.White);

    /// <summary>
    /// Says something to everybody who can SEE <paramref name="mapNum"/> — the room, as near as a
    /// seamless world has one.
    ///
    /// <para>🔴 <b>Not "everybody standing on it".</b> The world scrolls contiguously, so somebody on
    /// the next map along is looking at this one and would watch an event happen in silence. The
    /// audience for an event is who can see it, which is what the observer set is.</para>
    /// </summary>
    void TellEveryoneOn(int mapNum, string text, ChatChannel channel = ChatChannel.System,
                        int color = GameColor.White);

    /// <summary>
    /// Says something to everybody within earshot of a square — the tighter audience, the one that
    /// hears speech rather than the one that can see the region.
    /// </summary>
    void TellEveryoneNear(WorldPlace at, string text, ChatChannel channel = ChatChannel.System,
                          int color = GameColor.White);

    /// <summary>
    /// Says something to a set of bodies, wherever they are.
    /// </summary>
    ///
    /// <para>🔴 <b>The audience nothing else here can express.</b> The other three are all about
    /// PLACE — one body, a region, an earshot — and a guild is not a place. Anything a game gathers
    /// for its own reasons, a raid, a party, everyone carrying a key, is this.</para>
    ///
    /// <para>Bodies that are not players in the world are skipped rather than refused: a set collected
    /// a moment ago is a set somebody may have logged out of.</para>
    /// </summary>
    void TellThese(IReadOnlyCollection<EntityHandle> them, string text,
                   ChatChannel channel = ChatChannel.System, int color = GameColor.White);

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

    /// <summary>
    /// Floats a line of text up off a body, to everybody within sight of it.
    /// </summary>
    ///
    /// <para>🔴 <b>The one place a game asks the client to DRAW.</b> Everything else here sets state and
    /// lets the client decide what that looks like — but a damage number is not state, it is an event
    /// that happened once, and there is nothing for a client to derive it from.</para>
    ///
    /// <para>Addressed to a BODY, not a tile, because that is what makes it follow: the client centers
    /// it on an oversize footprint, keeps it anchored across a seam crossing, and holds it until an
    /// in-flight projectile lands so the text and the impact read as one event.</para>
    ///
    /// <param name="who">Whose head it floats over.</param>
    /// <param name="text">What it says, already in the words a player will read.</param>
    /// <param name="rgb">Packed 0xRRGGBB, from <see cref="GameColor"/> or a game's own.</param>
    /// <param name="splatter">How much of a burst comes with it, 0 for none. Drawn in the world's own
    /// decal color, so what a splatter looks like is decided once rather than per hit.</param>
    void Float(EntityHandle who, string text, uint rgb, float splatter = 0f);

    /// <summary>
    /// Sweeps a crescent over a body, oriented by the way it is facing.
    /// </summary>
    /// <param name="who">Who is swinging.</param>
    /// <param name="connected">True flings sparks with it, which is what makes a sweep read as having
    /// hit something rather than passing through air.</param>
    void Sweep(EntityHandle who, bool connected = true);

    /// <summary>
    /// Throws something from one body to another, and holds any number owed to the target until it
    /// lands.
    /// </summary>
    ///
    /// <para>🔴 <b>The timing is the part worth having.</b> A damage number that appears before its
    /// bolt arrives reads as two unrelated events, so a hit on that target is registered as pending and
    /// released when the thing would land — several throws at one target stagger across their own
    /// arrivals. A target that resolves to nowhere gets the effect in place rather than no effect.</para>
    ///
    /// <param name="from">Who is throwing.</param>
    /// <param name="to">What it is aimed at.</param>
    /// <param name="style">What it looks like on its way.</param>
    /// <param name="rgb">Packed 0xRRGGBB.</param>
    void Throw(EntityHandle from, EntityHandle to, ProjectileStyle style, uint rgb);

    /// <summary>
    /// Bursts a spray of droplets from a body, arcing down under gravity.
    /// </summary>
    ///
    /// <para>Deliberately color-blind: blood, sparks off a struck anvil, water from a splash and dust
    /// off a rockfall are one burst with a different <paramref name="rgb"/>.</para>
    ///
    /// <param name="intensity">How big, 0 to 1. A trickle and a spray out of one call.</param>
    void Burst(EntityHandle who, uint rgb, float intensity = 0.5f);

    /// <summary>
    /// The body standing on that square, or <see cref="EntityHandle.None"/> for an empty one.
    ///
    /// <para>🔴 <b>The reverse of <see cref="PlaceOf"/>, and a game cannot do without it.</b> Everything
    /// else here starts from a handle the engine already gave out. A verb used on a square gives a game
    /// coordinates, so without this it can say what happened and cannot say who it happened to.</para>
    ///
    /// <para>A player is answered before an NPC when both somehow occupy one tile, because a rule aimed
    /// at a square is aimed at whoever is standing there and a player is the one who will notice.</para>
    /// </summary>
    EntityHandle At(WorldPlace place);

    /// <summary>Every record of one of this game's own families, 1-based, blanks included. Empty for a
    /// family this world does not hold.</summary>
    IReadOnlyList<AttributeBag> RecordsOf(string familyId);

    /// <summary>One record of one of this game's own families, or null for a slot that is not there.</summary>
    AttributeBag? RecordAt(string familyId, int num);

    // ── Who somebody is with ───────────────────────────────────────────
    //
    // Two standing groups the engine already keeps, and had no way to answer about. Both answer with
    // who is IN THE WORLD rather than with a roster on disk, because the only thing a game does with
    // the answer is act on them — and a body that logged out an hour ago cannot be acted on.

    /// <summary>The name of the guild this body's account belongs to, or blank for none.</summary>
    string GuildOf(EntityHandle who);

    /// <summary>
    /// Everybody in the world who shares this body's guild, including it.
    ///
    /// <para>Empty for a body in no guild, which is not the same as a guild with nobody online — and
    /// a game that needs to tell them apart asks <see cref="GuildOf"/>.</para>
    /// </summary>
    IReadOnlyList<EntityHandle> GuildmatesOf(EntityHandle who);

    /// <summary>Everybody in this body's party, including it. Empty for somebody in no party.</summary>
    IReadOnlyList<EntityHandle> PartyOf(EntityHandle who);

    /// <summary>What to call this body — a player's character name, or an NPC's record name. Blank for a
    /// handle naming nobody, and for one whose body has left the world.
    ///
    /// <para>A game is handed handles and has no other way to turn one into something a player can read.
    /// Trimmed, because a record name is stored fixed-width.</para></summary>
    string NameOf(EntityHandle who);
}
