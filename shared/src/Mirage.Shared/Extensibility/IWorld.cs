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

    /// <summary>
    /// Whether this body is engaged right now.
    ///
    /// <para>🔴 <b>The five states above could be entered and not asked about.</b> A game that holds
    /// its regen through a fight, or refuses a verb to somebody still on cooldown, has to read the
    /// clock it set — and the alternative is keeping a second copy in an attribute, which is a second
    /// answer to a question the engine is already answering.</para>
    /// </summary>
    bool IsEngaged(EntityHandle who);

    /// <summary>Whether this body is out of action right now.</summary>
    bool IsDowned(EntityHandle who);

    /// <summary>Whether this body is marked right now.</summary>
    bool IsMarked(EntityHandle who);

    /// <summary>Whether this body is flagged as having started it.</summary>
    bool IsAggressor(EntityHandle who);

    /// <summary>Whether this body is still held off acting.</summary>
    bool IsWaiting(EntityHandle who);

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

    // ── The bag, slot by slot ─────────────────────────────────────────────────
    //
    // Carrying and Give and Take answer about an ITEM. A rule about what somebody is carrying —
    // what a death scatters, what a thief takes, what a customs post seizes — is about SLOTS: two
    // copies of one sword are two slots and two amounts of wear, and a stack of coins is one slot
    // holding a number.

    /// <summary>Which bag slots hold anything, in slot order. Empty for a body carrying nothing and
    /// for one that is not in the world.</summary>
    IReadOnlyList<int> BagOf(EntityHandle who);

    /// <summary>What is in that bag slot: the item, how many of it, and whether it is the copy being
    /// worn. Zeroes for a slot that is empty or is not there.
    ///
    /// <para><paramref name="quantity"/> is the stack size for something that stacks and 1
    /// otherwise, so a rule can count what a slot is worth without knowing which kind it is.</para></summary>
    (int ItemNum, int Quantity, bool Worn) InSlot(EntityHandle who, int slot);

    /// <summary>Puts what is in that bag slot on the ground where the body is standing.
    ///
    /// <para><paramref name="quantity"/> takes part of a stack; 0 takes the whole slot. What a game
    /// drops this way lands as a player drop and behaves like one: anyone may pick it up, and it is
    /// subject to the world's own limit on litter.</para>
    ///
    /// <para>False for a slot holding nothing, and for a body that is not in the world.</para></summary>
    bool DropFrom(EntityHandle who, int slot, int quantity = 0);

    /// <summary>
    /// Puts an item on the ground on a square, out of nowhere.
    ///
    /// <para>Not out of anybody's bag — a chest that opens, a reward left where a quest ended, a
    /// creature's hoard a game rolled for itself. <see cref="DropFrom"/> is the other one, and it moves
    /// something that already exists; this makes one.</para>
    ///
    /// <para><paramref name="claimedBy"/> and <paramref name="claimSeconds"/> hold it for one player for
    /// that long: nobody else may pick it up until the time runs out, and the client shows them whose it
    /// is. Left unsaid, the drop is free to whoever reaches it first.</para>
    ///
    /// <para>False for an item or a square that is not there.</para>
    /// </summary>
    bool DropAt(WorldPlace at, int itemNum, int quantity = 1,
                EntityHandle claimedBy = default, int claimSeconds = 0);

    /// <summary>How many of that item this body is carrying, worn ones and every stack counted
    /// together. 0 for a body carrying none.
    ///
    /// <para>The read that pairs with <see cref="Give"/> and <see cref="Take"/>. A rule that charges
    /// somebody in a currency of its own — a toll, a donation, an offering — asks this first, because
    /// taking more than they have and taking what they have look identical afterwards.</para></summary>
    long Carrying(EntityHandle who, int itemNum);

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

    /// <summary>Every record of one family, 1-based, blanks included. Empty for a family this world does
    /// not hold. For one of the engine's own families this is the game's fields on each record, never the
    /// engine's own properties.</summary>
    IReadOnlyList<AttributeBag> RecordsOf(string familyId);

    /// <summary>One record of one family, or null for a slot that is not there.</summary>
    AttributeBag? RecordAt(string familyId, int num);

    /// <summary>What a record is CALLED — an item's name, a creature's, a map's. Blank for a slot
    /// nobody authored and for a family whose records carry no name.
    ///
    /// <para>The one property every family has, and the only one of the engine's own that is readable
    /// this way. A rule that picks a record rather than being handed one — a bounty on a kind of
    /// creature, a shopping list, a map a season moves to — has to be able to say which.</para>
    ///
    /// <para>For a map this is the name a player is shown, resolved through its group the way the
    /// client resolves it. For a game's own family it is the record's <c>name</c> field.</para></summary>
    string RecordName(string familyId, int num);

    /// <summary>Where this map puts somebody who leaves it other than by walking, resolved through
    /// its <see cref="MapGroupOf">group</see>. <see cref="WorldPlace.Nowhere"/> for a map that names
    /// none, and for one that is not there.
    ///
    /// <para>The map author's say over where leaving this place lands you. A game deciding where a
    /// death or a spell sends somebody reads it and outranks it as it sees fit — the engine consults
    /// it for nothing on its own.</para></summary>
    WorldPlace ExitFrom(int mapNum);

    /// <summary>One of a game's own fields on a map, with the map's <see cref="MapGroupOf">group</see>
    /// behind it: the map's own value when it carries the key, else the group's, else null.
    ///
    /// <para>Every inheritable property the engine keeps on a map resolves this way, and a game's fields
    /// are no different — a dungeon can say once, on its group, that it is somewhere you can be
    /// attacked.</para></summary>
    AttributeValue? MapValue(int mapNum, string key);

    /// <summary>
    /// Write one field of one record, and get it onto disk.
    ///
    /// <para>A game's records are mostly authored and read, but some of what a game keeps belongs to no
    /// body and no guild: the last day it settled accounts, a season number, who holds a territory. Those
    /// have nowhere else to live.</para>
    ///
    /// <para>⚠ A game's own fields only. The engine's properties are written through their own paths,
    /// which normalize what they are given; a value written straight into one would skip all of that. A
    /// key in the extension bag has nothing to normalize, so it is reachable on any family.</para>
    /// </summary>
    bool SetRecordValue(string familyId, int num, string key, AttributeValue value);

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

    /// <summary>Which creature record this body is a copy of, or 0 for a player and for a handle naming
    /// nobody.
    ///
    /// <para>🔴 <b>The only way a game can tell one species from another.</b> <see cref="NameOf"/> answers
    /// with a display string, which is what a player reads rather than what a rule keys on: two records
    /// may share a name, a name may be translated, and an editor rename would silently rewrite the
    /// game's arithmetic. A game that pays a different bounty per creature, or drops a different kit,
    /// asks this.</para></summary>
    int KindOf(EntityHandle who);

    // ── Pointing a creature at somebody ─────────────────────────────────
    //
    // 🔴 What a body does ON ITS OWN is authored on its record — it holds a tile, ambles, closes on what
    // it notices, or opens the gap. That vocabulary is about LOCOMOTION and says nothing about why, which
    // is what keeps it genre-agnostic. Why is a game's, and a game needs two verbs to say it: send this
    // body after that one, and let it go.
    //
    // Between them they are how a game writes the reasons the engine deliberately does not hold — a
    // creature that fights back once hit, a guard that moves on whoever drew a weapon, a herd that
    // scatters from one that was startled.

    /// <summary>
    /// Send a creature after a body, whether or not it would ever have noticed that body itself.
    ///
    /// <para>⚠ <b>It overrides the record's own noticing, not its legs.</b> A body that holds its tile
    /// still holds it — being roused does not make a statue walk. Everything that moves closes on what it
    /// was pointed at, and a body authored to open the gap opens it instead, because retreating is what
    /// that body does about somebody.</para>
    ///
    /// <para>It also commits to the approach rather than walking it in, since a body that was SENT is not
    /// deciding whether to be interested.</para>
    ///
    /// <para>False when either side is not in the world, and for a creature pointed at itself.</para>
    /// </summary>
    bool Provoke(EntityHandle npc, EntityHandle quarry);

    /// <summary>Let go of whatever a creature was chasing, leaving it to its record's own behavior again.
    /// False for a handle naming nobody. Harmless on a body that was chasing nothing.</summary>
    bool Forget(EntityHandle npc);

    /// <summary>
    /// Every creature standing within so many tiles of a square, nearest first.
    ///
    /// <para>🔴 <b>A game is only ever HANDED one body at a time</b> — the one a verb was used on, the one
    /// that reached somebody. A rule about the bodies AROUND an event has nothing to start from without
    /// this: guards answering a call, a herd that scatters when one of them is startled, a spell that
    /// catches what is standing beside its target.</para>
    ///
    /// <para>⚠ On that map only, measured in tiles from the square. The world scrolls contiguously, so a
    /// body one tile over a border is close and is not in this answer — a game that wants the ones next
    /// door asks for each map. Visitors standing on the map are included; they are as much there as the
    /// natives.</para>
    /// </summary>
    IReadOnlyList<EntityHandle> NpcsNear(WorldPlace at, int tiles);

    /// <summary>
    /// Every player standing within so many tiles of a square, nearest first.
    ///
    /// <para>The mirror of <see cref="NpcsNear"/>, and needed for the same reason: a rule about the
    /// PEOPLE around an event — who shared a kill, who heard a shout, who was caught in a blast — has
    /// nothing to start from otherwise, because a game is only ever handed one body at a time.</para>
    ///
    /// <para>⚠ On that map only, measured in tiles from the square, exactly as above.</para>
    /// </summary>
    IReadOnlyList<EntityHandle> PlayersNear(WorldPlace at, int tiles);

    /// <summary>
    /// Every creature standing on a map, in no particular order.
    ///
    /// <para>The whole population rather than a neighborhood, which is a different question from
    /// <see cref="NpcsNear"/> and the one a SWEEP asks: every creature has to be told that night fell,
    /// counted for a census, checked for a boss that is still alive, cleared of something a spell left
    /// on it. A rule like that has no square to measure from.</para>
    ///
    /// <para>⚠ Visitors standing on the map are included and natives away chasing on another map are
    /// not, so a body appears exactly once across every map. Ask <see cref="RecordsOf"/> on the map family
    /// for how many maps there are to walk.</para>
    /// </summary>
    IReadOnlyList<EntityHandle> NpcsOn(int mapNum);

    // ── Asking about the ground ─────────────────────────────────────────
    //
    // 🔴 A game is given squares constantly — a verb was used on one, a body is standing on one — and
    // until now could learn nothing at all about what is there. Everything below is the engine
    // answering a question it already answers for itself a hundred times a tick.

    /// <summary>What kind of tile is at that square — <c>walkable</c>, <c>blocked</c>, <c>warp</c>,
    /// <c>item</c>, <c>npcavoid</c>, <c>door</c>, <c>plate</c>, or <c>ramp</c>. Blank for a square that
    /// is not on a real map.
    ///
    /// <para>⚠ On the ground layer. A bridge deck is a different answer at the same coordinates, and a
    /// game that cares about bridges is asking a question this does not answer.</para></summary>
    string TileAt(WorldPlace place);

    /// <summary>
    /// Whether a straight line from one square to another crosses nothing that stops sight.
    ///
    /// <para>🔴 <b>The engine's own answer, not an approximation of it.</b> This is the identical trace
    /// the client colors its target arrow with, so a game gating on it agrees with what the player was
    /// shown rather than nearly agreeing.</para>
    ///
    /// <para>A wall stops sight only if it was authored to: a railing or a window is blocked to walk
    /// through and clear to see through. A closed door always stops it. False when either square is not
    /// on a real map, or when the two are too far apart to be compared.</para>
    /// </summary>
    bool CanSee(WorldPlace from, WorldPlace to);

    /// <summary>
    /// How far apart two squares are, in tiles, counting across map borders.
    ///
    /// <para>🔴 <b>The world scrolls contiguously, so a body one tile over a border is one tile away</b>
    /// — and arithmetic on the coordinates alone says it is on another map and unreachable. Every range
    /// rule a game writes wants this rather than subtraction.</para>
    ///
    /// <para>-1 when either square is not on a real map, or when they are too far apart for the engine
    /// to place one relative to the other.</para>
    /// </summary>
    int Distance(WorldPlace from, WorldPlace to);

    /// <summary>What the weather is over that map — <c>clear</c>, <c>rain</c>, <c>snow</c>,
    /// <c>heatwave</c>, or <c>heavywind</c>. Blank for a map that is not there.</summary>
    string WeatherOn(int mapNum);

    /// <summary>What time of day it is across the world — <c>day</c>, <c>dusk</c>, <c>night</c>, or
    /// <c>dawn</c>.
    ///
    /// <para>The engine runs the cycle and the client paints it, and until a game can ask, the sky is
    /// scenery. It is the other half of the weather: a game with anything that is different after dark —
    /// creatures that hunt at night, a shop that shuts, a spell that only works under a moon — has
    /// nothing to read otherwise.</para>
    ///
    /// <para>⚠ One answer for the whole world, unlike the weather. Time of day is a cycle the server
    /// runs, not a property of a place.</para></summary>
    string TimeOfDay();

    /// <summary>Which map group that map belongs to, or 0 for a map in none.
    ///
    /// <para>A group is the engine's idea of a region: several maps sharing a name and some settings. A
    /// game that owns regions — a territory, a province — asks this to turn where somebody is standing
    /// into which region it is.</para></summary>
    int MapGroupOf(int mapNum);

    /// <summary>Put something on, out of the bag, and take off whatever was in its slot.
    ///
    /// <para>🔴 <b>A game that hands somebody a sword has no other way to put it in their hand.</b> The
    /// engine already knows how to wear things and which slot each item names; what it had no way to
    /// hear was a rule asking for it — a class's opening kit, a quest reward, a curse that arms
    /// somebody against their will.</para>
    ///
    /// <para>False for a body that is not carrying that item, and for a piece naming a slot this world
    /// does not declare. Wearing something already worn is not an error and changes nothing.</para></summary>
    bool Wear(EntityHandle who, int itemNum);

    /// <summary>Take something off. It stays in the bag.
    ///
    /// <para>The other half of <see cref="Wear(EntityHandle, int)"/>. A death that breaks a piece of
    /// armor calls this to take it off the body it broke on.</para>
    ///
    /// <para>False for a body that is not wearing that item.</para></summary>
    bool Remove(EntityHandle who, int itemNum);

    /// <summary>Whether they are running rather than walking right now. What running COSTS is a game's;
    /// the engine moves the body and this is how a rule hears about it.</summary>
    bool IsRunning(EntityHandle who);

    // ── Asking what a creature was authored as ──────────────────────────
    //
    // 🔴 A game reads its OWN attributes off a body freely and could learn nothing about what an author
    // actually wrote on the record. A rule that treats a chaser differently from an ambler, or that
    // keeps a pack together, had no way to tell them apart.

    /// <summary>How this body moves on its own — <c>stationary</c>, <c>wander</c>, <c>pursue</c>,
    /// <c>flee</c>, or <c>scavenge</c>. Blank for anything that is not a creature in the world.</summary>
    string BehaviorOf(EntityHandle npc);

    /// <summary>Which pack this body keeps to, or 0 for one in none. Two creatures sharing a non-zero
    /// group never notice each other, on top of never noticing their own kind.</summary>
    int GroupOf(EntityHandle npc);

    /// <summary>How far it notices anything, in tiles, as its record was authored. 0 for a body that
    /// notices nobody, and for anything that is not a creature in the world.</summary>
    int RangeOf(EntityHandle npc);

    /// <summary>Whether it is after somebody right now — one it noticed, or one a game sent it after.
    ///
    /// <para>The counterpart to <see cref="Provoke"/> and <see cref="Forget"/>, which write and never
    /// read: without this a rule cannot tell a creature already in a fight from one standing
    /// idle.</para></summary>
    bool IsChasing(EntityHandle npc);

    // ── Guilds, as something a game can act on ──────────────────────────
    //
    // 🔴 The engine already runs a guild: founding, membership, ranks, applications, a vault, and the
    // ledger of who paid into it. What it has no opinion about is what a guild DOES — a war, a rank
    // that means something, a level, a season. Those are a game's, and a guild carries an attribute
    // bag for exactly them.
    //
    // ⚠ A guild is named by its NUMBER rather than by a handle. It is a record, not a body: it has no
    // place, nothing walks it, and it outlives every member — so the thing that names one is the same
    // thing that names an item or a map.

    /// <summary>What this body's account may do to the world — <c>player</c>, <c>monitor</c>,
    /// <c>mapper</c>, <c>developer</c>, or <c>creator</c>. Blank for a body that is not there.
    ///
    /// <para>Whoever runs a world usually wants its staff outside its rules: no fighting, no loot, no
    /// place on a ladder. Which rules that means is a game's to decide, and this is the fact it
    /// decides on.</para></summary>
    string AccessOf(EntityHandle who);

    /// <summary>Which guild this body's account belongs to, or 0 for none.</summary>
    int GuildNumber(EntityHandle who);

    /// <summary>What that guild is called, or blank for a number naming none.</summary>
    string GuildName(int guild);

    /// <summary>The guild with that name, or 0. Case-insensitive, the way the engine's own founding
    /// check compares — a game declaring war on a name a player typed asks the same question.</summary>
    int GuildNamed(string name);

    /// <summary>What rank this body holds in their guild — <c>leader</c>, <c>officer</c>,
    /// <c>member</c>, or blank for somebody in no guild.
    ///
    /// <para>The engine keeps the rank and moves it; what a rank may DO is a game's, and this is what
    /// a rule gating on one reads.</para></summary>
    string GuildRankOf(EntityHandle who);

    /// <summary>
    /// Everything a game hangs on a guild that the engine has no name for — a war, a level, a season
    /// score. Null for a number naming none.
    /// </summary>
    AttributeBag? GuildValues(int guild);

    /// <summary>Write one of them, and get the guild onto disk. False for a number naming none.
    ///
    /// <para>⚠ Saved on every write, because a guild is not a body: nothing logs it out, so there is no
    /// later moment where its values would be written anyway.</para></summary>
    bool SetGuildValue(int guild, string key, AttributeValue value);

    /// <summary>Everybody IN THE WORLD who belongs to that guild. Empty for a guild with nobody
    /// online, which is not the same as a guild that is not there.</summary>
    IReadOnlyList<EntityHandle> MembersOf(int guild);

    /// <summary>Every guild there is, by number.
    ///
    /// <para>🔴 <b>What anything RANKED starts from.</b> Every other guild call here takes a number a game
    /// already had — off a body, off a name. A standing, a league table, a tax sweep, an award for the
    /// best of them: none of those has a number to start from, and a game cannot count upward and hope,
    /// because a disbanded guild leaves a hole in the numbering.</para></summary>
    IReadOnlyList<int> Guilds();

    /// <summary>
    /// The ACCOUNT behind a body — the name a game keeps when it needs to find this person again.
    ///
    /// <para>🔴 <b>The one identity here that outlives a session.</b> A handle names a body in the
    /// world and stops meaning anything the moment they log out; a character can be deleted; a name can
    /// be taken by somebody else. An account is what the engine files mail, guild membership and a
    /// market listing under, and it is what a game writes down when the thing it is promising will be
    /// settled later — a sale, a refund, a prize drawn next week.</para>
    ///
    /// <para>⚠ <b>Not a character name, and not for showing to players.</b> It is how somebody signs in.
    /// Print a character's name in anything a player reads; keep this for looking them up.</para>
    ///
    /// <para>Empty for a body that is not a player in the world.</para>
    /// </summary>
    string AccountOf(EntityHandle who);

    /// <summary>The body signed in to that account right now, or nobody.
    ///
    /// <para>The way back: a game that wrote an account down reaches the person again with this, and
    /// gets <see cref="EntityHandle.None"/> when they are not here — which is the answer that tells it
    /// to post rather than tell.</para></summary>
    EntityHandle WhoIs(string account);

    /// <summary>Every account in a guild, whether or not anybody is signed in to them.
    ///
    /// <para>🔴 <b>A guild's roster outlives its members' sessions, and until now a game could only see
    /// the part of it that happened to be online.</b> Anything about the guild rather than about the
    /// people standing in front of you — a dividend, a census, a rule about who has stopped turning up —
    /// starts here. <see cref="MembersOf"/> is the other one, and it answers with bodies.</para></summary>
    IReadOnlyList<string> AccountsIn(int guild);

    /// <summary>Whether that account is a live member of the guild rather than a name on its roster:
    /// signed in for long enough, recently enough, by the engine's own measure.
    ///
    /// <para>The same question <see cref="MailMembers"/> asks when it is narrowed, offered on its own so
    /// a game can apply it to something else — who votes, who counts toward a quorum, who is worth
    /// counting when a guild is sized up.</para></summary>
    bool IsActiveIn(int guild, string account);

    /// <summary>
    /// Sends an ACCOUNT a letter, with something attached or without, whether or not anybody is signed
    /// in to it.
    ///
    /// <para>What <see cref="Mail"/> cannot do: reach somebody who is not here. A game that wrote an
    /// account down when it had the person settles up with them afterwards, and they find it waiting
    /// the next time they play.</para>
    ///
    /// <para>False for an account this server has never heard of.</para>
    /// </summary>
    bool MailTo(string account, string subject, string body, int itemNum = 0, int quantity = 0);

    /// <summary>
    /// Sends one player a letter, with something attached or without.
    ///
    /// <para>🔴 <b>The one thing a game can say that OUTLIVES the moment.</b> Everything else here is
    /// spoken to somebody who is standing there: a line of chat is gone when they log out, and a thing
    /// handed over needs a bag with room in it. A letter waits — through a logout, a restart, and a
    /// server that was down for a week — and what is attached to it waits with it.</para>
    ///
    /// <para>So it is what a reward that was earned rather than picked up looks like: a refund, a prize,
    /// a delivery, the rest of a payout that would not fit. An item of nothing sends the letter
    /// alone.</para>
    ///
    /// <para>False for a body that is not a player in the world. A game can only name somebody it can
    /// see; <see cref="MailMembers"/> is the one way to reach people it cannot, because a guild is the
    /// only roster the engine keeps that outlives its members' sessions.</para>
    /// </summary>
    bool Mail(EntityHandle who, string subject, string body, int itemNum = 0, int quantity = 0);

    /// <summary>
    /// Sends every member of a guild an item, reaching the ones who are not here.
    ///
    /// <para>🔴 <b>The only way to pay somebody who is offline.</b> Everything else a game can do reaches
    /// a body in the world, and a reward earned by a GROUP is owed to its members whether or not they
    /// happened to be logged in when it was earned — a season payout, a war dividend, a founder's
    /// bonus. It arrives as mail, so it waits for them.</para>
    ///
    /// <para><paramref name="onlyActive"/> narrows it to members who have actually been playing: online
    /// long enough, recently enough, by the engine's own measure of a live roster. What that measure is
    /// belongs to the engine, because it is the same question the roster itself answers — and a payout
    /// split among a hundred names that have not logged in for a year is a payout nobody feels.</para>
    ///
    /// <para>Returns how many members it reached. Zero for a guild that is not there, an item that is
    /// not there, or an amount of nothing.</para>
    /// </summary>
    int MailMembers(int guild, int itemNum, int quantity, string subject, string body,
                    bool onlyActive = false);

    /// <summary>What is in that guild's vault. 0 for a number naming none.</summary>
    long GuildGold(int guild);

    /// <summary>Put gold into a guild's vault. False for a number naming none, or an amount of nothing.
    ///
    /// <para>The counterpart to <see cref="SpendGuildGold"/>. A game that holds gold aside — an escrow, a
    /// stake, a bond — has to be able to give it back, and a war that ends in a draw returns both sides'
    /// stakes rather than paying anybody.</para></summary>
    bool GiveGuildGold(int guild, long amount);

    // ── Worn gear, and what wears it out ────────────────────────────────
    //
    // Core spends durability on its own — a swing wears a weapon, a repair shop restores it — and a
    // game that puts a cost on dying has no way to reach any of that. What follows is the same four
    // questions the shop asks, answered for a rule instead.

    /// <summary>What this body is wearing, as item numbers, in slot order. Empty for a body wearing
    /// nothing and for one that is not in the world.</summary>
    IReadOnlyList<int> WornBy(EntityHandle who);

    /// <summary>What this body is wearing in ONE slot, by item number. 0 for an empty slot, and for a
    /// slot this world does not declare.
    ///
    /// <para>A rule about a particular place on the body asks this rather than walking the list:
    /// whether somebody is holding a shield, whether their hand is free, what is on their head. Which
    /// slots exist is the game's own declaration, so the key is one it chose.</para></summary>
    int WornIn(EntityHandle who, string slotKey);

    /// <summary>How much wear is left in the copy of that item they are wearing, and how much it holds
    /// when new. Both 0 when they are not wearing one, and when the item has no durability at all —
    /// which is an ordinary thing for an item to be.</summary>
    (int Left, int Full) DurabilityOf(EntityHandle who, int itemNum);

    /// <summary>Wear out that many points of the copy they are wearing, never past nothing. Returns how
    /// many points were actually taken, which is less than asked for when it was nearly worn out.
    ///
    /// <para>An item worn to nothing is not destroyed: it stays in the bag, unusable, until it is
    /// repaired. That is the engine's rule, not a game's.</para></summary>
    int Wear(EntityHandle who, int itemNum, int points);

    /// <summary>What repairing that many points of that item costs in gold, by the engine's own repair
    /// rate. 0 for an item that is not there.</summary>
    int RepairCost(int itemNum, int points);

    /// <summary>Gold a point of durability costs to repair on ON-TIER gear at that tier, priced against
    /// a reference piece rather than against anything anybody is holding.
    ///
    /// <para>Fractional, and deliberately so — at the bottom of the ladder a point is worth a fraction of
    /// a coin. A game charging a per-use upkeep in something other than durability asks this so its
    /// number tracks the engine's repair economy instead of being pinned beside it, where the two drift
    /// apart the first time repair is retuned and nothing reports it.</para></summary>
    double RepairRateAt(int tier);

    /// <summary>
    /// Up to so many spots spread across a region, every one reachable on foot from every other.
    ///
    /// <para>🔴 <b>Measured by WALKING</b>, across the region's map seams, not in a straight line and not
    /// by map number. A straight line is a lie wherever a wall, a cliff or water stands between two tiles
    /// that are near on paper and a long way apart on foot; map numbers run in authoring order, so
    /// spreading by them piles everything into whichever corner was drawn first.</para>
    ///
    /// <para>What the spots are for is a game's — capture points, chests, patrol posts, somewhere to hide
    /// a key. What the engine contributes is the part no script can do: a region is thousands of tiles,
    /// and the seam step, the walking distance and the connected stretch are all things it already knows
    /// because it moves bodies across them.</para>
    ///
    /// <para><paramref name="onlyWhere"/> names one of the game's own truth fields on Maps, and a spot
    /// goes only on a map carrying it; blank puts one anywhere in the region. The walk covers the whole
    /// region either way — a town in the middle of one is walked THROUGH, and leaving it out would cut
    /// the region in half at its towns.</para>
    ///
    /// <para>Fewer than asked for is an ordinary answer: the region's largest walkable stretch had
    /// nowhere else to put one.</para>
    /// </summary>
    IReadOnlyList<WorldPlace> SpreadOver(int region, int count, string onlyWhere = "");

    /// <summary>
    /// Takes every creature off a map and keeps it that way.
    ///
    /// <para>For a place that has to stop being ordinary ground for a while: a war fought over it, a
    /// ritual nobody should interrupt, an arena cleared for a duel. The bodies go now and nothing comes
    /// back until <see cref="Refill"/> — which is what separates this from clearing a map and watching it
    /// refill a minute later.</para>
    ///
    /// <para>False for a map that is not there, or one already emptied.</para>
    /// </summary>
    bool Empty(int mapNum);

    /// <summary>Lets a map hold creatures again, and puts its own back at once rather than leaving it
    /// bare until each slot's clock comes round. False for a map that was not emptied.</summary>
    bool Refill(int mapNum);

    /// <summary>Whether a map is being kept clear of creatures.</summary>
    bool IsEmptied(int mapNum);

    /// <summary>Puts a mark on the ground, or replaces the one already under that name.
    ///
    /// <para>The twin of an overhead bar, for a PLACE — a flag on a capture point, a ring around a
    /// blast, a name over a doorway, a meter on a ritual. See <see cref="WorldMarker"/> for what one
    /// draws.</para>
    ///
    /// <para>Replacing rather than stacking is what makes a mark that moves, or whose meter is counting,
    /// one call. False for a square that is not there.</para></summary>
    bool Mark(WorldMarker marker);

    /// <summary>Takes one away by name. False when nothing was under it, which is an ordinary answer for
    /// a game clearing up after something that ended on its own.</summary>
    bool Unmark(string id);

    /// <summary>Whether a square is inside a mark's ring.
    ///
    /// <para>🔴 <b>The ring drawn and the ring asked about are the same mark.</b> A game scoring the
    /// ground inside one asks this rather than doing the arithmetic itself: the two answers drifting
    /// apart is invisible, because the line a player can see would sit somewhere other than the line
    /// that counts.</para>
    ///
    /// <para>Measured center to center and inclusive, so what this answers yes for is exactly the
    /// staircase of tiles the ring outlines. False for a mark with no ring, and for another map.</para></summary>
    bool InsideMark(string id, WorldPlace place);

    /// <summary>The time now, in seconds since 1970, UTC.
    ///
    /// <para>A game with anything dated in it needs this: a cooldown that has to survive a restart, a
    /// window that opens for an hour, a daily reset. Counting ticks answers a different question, since
    /// ticks stop when the server does.</para></summary>
    long Now();

    /// <summary>How far the server's own civil day is from UTC right now, in seconds — east of it
    /// positive, west of it negative.
    ///
    /// <para><b>Anything that turns over at MIDNIGHT wants this.</b> A daily reset, a weekly tax, a
    /// season: those are meant to land on the operator's civil day rather than on a UTC boundary, and a
    /// game that divides <see cref="Now"/> by a day gets the wrong one everywhere but Greenwich. Add it
    /// before dividing.</para>
    ///
    /// <para>⚠ Read fresh each time, so a place that keeps summer time gives a different answer in July
    /// than in January. That is what makes a boundary land at midnight all year rather than drifting an
    /// hour twice.</para></summary>
    int LocalOffset();

    /// <summary>
    /// Everything a game keeps about the WORLD rather than about anybody in it — a season number,
    /// whether an event is running, how many times something has happened.
    ///
    /// <para>The one place for state that belongs to no body and no record. It is persisted beside the
    /// time of day and the weather, which are the engine's own answers to the same question, and it
    /// comes back as it was left when the server starts again.</para>
    /// </summary>
    AttributeBag WorldValues();

    /// <summary>Write one of them. Kept until the world is next written, which happens on the engine's
    /// own save cadence and at shutdown.</summary>
    void SetWorldValue(string key, AttributeValue value);

    /// <summary>Take gold out of a guild's vault, recording who spent it.
    ///
    /// <para>🔴 <b>Through the engine's own ledger, not by writing the number.</b> A vault that went
    /// down with nothing in the spending log is money a guild cannot account for, and accounting for it
    /// is most of what a vault is for.</para>
    ///
    /// <para>False when the vault does not hold that much, which is what makes this the check as well
    /// as the payment.</para></summary>
    bool SpendGuildGold(int guild, long amount, EntityHandle by);
}
