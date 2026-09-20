namespace Mirage.Shared;

public enum Direction : byte
{
    Up = 0,
    Down = 1,
    Left = 2,
    Right = 3,
}

public enum MovementType : byte
{
    None = 0,
    Walking = 1,
    Running = 2,
}

/// <summary>Result code carried on an AlertMsgPacket so the client's auth-flow logic branches on a
/// stable value instead of the (localizable) server prose. None = an ordinary alert with no
/// flow meaning.</summary>
public enum AlertCode : byte
{
    None = 0,
    AccountCreated = 1,
    AccountDeleted = 2,
    PasswordChanged = 3,
    IncorrectPassword = 4,
    AccountNotFound = 5,
}

public enum TileType : byte
{
    Walkable = 0,
    Blocked = 1,
    Warp = 2,
    Item = 3,
    NpcAvoid = 4,
    // A door stays blocked until somebody holding the item named in Data1 opens it; a plate opens the
    // door whose coordinates it carries, with nothing held at all.
    Door = 5,
    Plate = 6,
    // A bridge ramp connects the ground layer to the fringe layer (the walkable top of a bridge).
    // Carried in FringeAttr.Type; Data1 = the ground-side Direction you mount from. See LayerLogic.
    LayerRamp = 7,
}

/// <summary>The two gameplay layers an entity can occupy at one (x,y): the ground, or the walkable
/// "fringe" surface on top of a bridge. A first-class coordinate dimension consulted by movement,
/// collision, rendering, lighting, combat, and the territory contest. Distinct from the visual
/// tile-art stacks (Ground[]/Fringe[]/Canopy[] on TileRecord), which are z-order paint only.</summary>
public enum WorldLayer : byte
{
    Ground = 0,
    Fringe = 1,
}

/// <summary>What an action-bar slot points at. <see cref="None"/> is an empty slot — the slot always
/// exists, it just has nothing bound to it, so there is no null case to carry through the UI.</summary>
public enum HotkeyKind : byte
{
    None = 0,

    /// <summary>One authored record, named by its family and its number: an item, a spell, a recipe,
    /// whatever that game keeps. Firing it invokes the verb its family named.</summary>
    Record = 1,

    /// <summary>One declared verb, named by its action id, optionally carrying a subject number that
    /// reaches the handler as the picked id.</summary>
    Verb = 2,
}

public enum ItemType : byte
{
    /// <summary>What TREASURE is typed as: an item whose only purpose is its worth, which is authored
    /// rather than derived.</summary>
    None = 0,

    /// <summary>Something worn, in the slot named by <see cref="Records.ItemRecord.EquipSlot"/>. WHICH
    /// slots exist is a game's decision; the engine only knows one thing goes in each.</summary>
    Equipment = 1,

    /// <summary>Something used up. Core paces it on its own clock and knows nothing else about it — what
    /// using one DOES is a game's rule, supplied by the module that declares the effect.</summary>
    Consumable = 2,

    /// <summary>Opens a Door tile that names this item.</summary>
    Key = 3,

    /// <summary>Counted rather than carried one per slot; gold is the stock example.</summary>
    Currency = 4,
}

/// <summary>What an NPC does with its time. Each member drives one distinct part of the AI, and none
/// of them names a genre: a <see cref="Pursue"/> body closes on what it notices and holds contact, and
/// what contact MEANS is the game layer's to decide.</summary>
public enum NpcBehavior : byte
{
    /// <summary>Holds its tile. Never moves, never notices anybody.</summary>
    Stationary = 0,

    /// <summary>Ambles in committed strides, never noticing anybody.</summary>
    Wander = 1,

    /// <summary>Notices a player or a non-kin NPC within <see cref="Records.NpcRecord.Range"/>, closes
    /// on it across map seams and through warps, and holds contact. Wanders while it has nobody.</summary>
    Pursue = 2,

    /// <summary>Notices on the same terms as <see cref="Pursue"/> and retreats instead, opening the gap
    /// until whoever it noticed is out of range. Wanders while it has nobody.</summary>
    Flee = 3,

    /// <summary>Walks to player-dropped litter on its map and clears it. Wanders while the map is
    /// clean.</summary>
    Scavenge = 4,

    /// <summary>Notices on the same terms as <see cref="Pursue"/> and then KEEPS ITS DISTANCE: closes
    /// until it is <see cref="Records.NpcRecord.Standoff"/> tiles away, holds there, and gives ground when
    /// something walks into it. Wanders while it has nobody.
    ///
    /// <para>The third answer to "something is over there", and the one neither of the other two gives: a
    /// pursuer walks into arm's reach and a fleeing body runs until it has forgotten you. A body that
    /// wants a gap and MEANS TO KEEP IT — an archer, a caster, a heckler, a bodyguard holding a
    /// perimeter, an animal that will not be approached — has neither.</para>
    ///
    /// <para>⚠ Reaching the distance it wanted raises contact for one of these, so a game hears
    /// about it in the same place and on the same terms as a body that closed all the way in.</para></summary>
    Shadow = 5,
}


public enum AdminLevel : byte
{
    Player = 0,
    Monitor = 1,
    Mapper = 2,
    Developer = 3,
    Creator = 4,
}

/// <summary>A member account's rank within its guild. 0 = not in a guild. Higher = more authority,
/// so the same relational-comparison idiom used for <see cref="AdminLevel"/> applies (e.g.
/// <c>rank >= GuildRank.Officer</c>).</summary>
public enum GuildRank : byte
{
    None = 0,
    Member = 1,
    Officer = 2,
    Leader = 3,
}

/// <summary>What a pending guild offer is, so the recipient's prompt shows the right message: an
/// invite to join, a request to approve, or a leadership transfer to accept.</summary>
public enum GuildOfferKind : byte
{
    Invite = 0,
    Request = 1,
    Transfer = 2,
}

/// <summary>What a dialogue choice does when picked (NPC conversations). None = pure text navigation (follow
/// the choice's NextNodeId; 0 = end). OpenShop is a terminal HAND-OFF into the NPC's other role — it closes
/// the conversation and re-issues an NpcInteract so the server opens the keeper shop (re-validating r=5).
/// No economy mutation lives in a conversation.
///
/// <para><b>The hand-off list is Core's, and it is short on purpose.</b> A game that adds a role an NPC can
/// have wants a choice that opens it, and that needs the client to know what opening it MEANS — which is
/// code rather than data, so it waits on the client module seam. Until then a game's own role is reached by
/// ending the conversation and acting on what the player did.</para></summary>
public enum ConversationAction : byte
{
    None = 0,
    OpenShop = 1,
}

public enum WeatherType : byte
{
    Clear = 0,
    Rain = 1,
    Snow = 2,
    HeatWave = 3,
    HeavyWind = 4,
}

public enum TimePhase : byte
{
    Day = 0,
    Dusk = 1,
    Night = 2,
    Dawn = 3,
}

/// <summary>How a light source's core animates: <see cref="None"/> = steady, <see cref="Flame"/> = irregular
/// torch flicker, <see cref="Pulse"/> = smooth magical breathing.</summary>
public enum FlickerStyle : byte
{
    None = 0,
    Flame = 1,
    Pulse = 2,
}

public enum ItemSource : byte
{
    TileDefined = 0,
    PlayerDropped = 1,
    NpcDropped = 2,
    // Items shed by a dying player. Behaves like PlayerDropped for pickup/persistence but is
    // exempt from the guard janitor sweep, so a corpse stays lootable for its full window.
    PlayerDeathDropped = 3,
}

public enum ShopType : byte
{
    Store = 0,
    Inn = 1,
}

/// <summary>How a client wants an NpcInteract resolved. Auto (what the interact key sends) lets the server pick
/// the NPC's best role — TALK-FIRST: a conversation if the NPC has one, else its keeper shop. Shop and Talk
/// each FORCE one role — the context-menu items, and the conversation's terminal hand-off choice — so a
/// forced open can't loop back into a different menu.
///
/// <para>The numbering has gaps where a role left Core. They are not reused: a client and a server that
/// disagree about what 3 means open the wrong menu, and nothing about that reads as a version mismatch.</para></summary>
public enum NpcInteractChoice : byte
{
    Auto = 0,
    Shop = 1,
    Talk = 2,
}
