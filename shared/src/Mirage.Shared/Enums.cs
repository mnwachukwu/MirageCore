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
    Item = 1,
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

/// <summary>Fixed descriptive tags a leader applies to a guild (up to
/// <see cref="Constants.MaxGuildLabels"/>); surfaced in the guild info panel and the open-guild
/// browser. 0 = unset.</summary>
public enum GuildLabel : byte
{
    Pvp = 1,
    Pve = 2,
    Leveling = 3,
    CasualSocial = 4,
    Hardcore = 5,
    OrganizedWars = 6,
    ItemFarming = 7,
    NewbieFocused = 8,
    VeteranFocused = 9,
}

/// <summary>What action a shared-kernel <see cref="Records.Objective"/> tracks. Only <see cref="Kill"/>
/// is wired in v1 (the mob-kill hook); <see cref="Fetch"/>/<see cref="Gather"/>/<see cref="Explore"/>
/// are declared plumbing for later objective kinds. 0 = unset (an empty objective).</summary>
public enum ObjectiveKind : byte
{
    None = 0,
    Kill = 1,
    Fetch = 2,
    Gather = 3,
    Explore = 4,
}

/// <summary>Per-character lifetime state of a player quest, from first acquisition through infinite repeats.
/// NotStarted (0) is never stored — a never-touched quest simply has no entry in
/// <see cref="Records.PlayerQuest"/>. The <c>InProgress</c> vs <c>InProgressRepeat</c> distinction is what the
/// abandon + repeat-reward logic keys off, so no completion counter is needed.</summary>
public enum QuestStatus : byte
{
    NotStarted = 0,        // never accepted — no entry
    InProgress = 1,        // accepted, NEVER completed before → abandon DROPS it; turn-in pays the MAIN rewards
    InProgressRepeat = 2,  // re-accepted after a prior completion → abandon reverts to Done; turn-in pays REPEAT rewards
    Done = 3,              // completed at least once, not currently active
}

/// <summary>How often a Repeatable quest re-opens. Eligibility is a LAZY per-character period-key compare
/// (no scheduler): a Done repeatable quest re-lights when the current period's key differs from the key
/// stored at last completion. None = not repeatable (Done is permanent).</summary>
public enum QuestCadence : byte
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

/// <summary>What a dialogue choice does when picked (NPC conversations). None = pure text navigation (follow
/// the choice's NextNodeId; 0 = end). OpenShop / OpenQuests are terminal HAND-OFFS into the NPC's existing
/// roles — they close the conversation and re-issue an NpcInteract so the server opens the keeper shop/inn or
/// the quest menu (re-validating r=5). No economy mutation lives in a conversation.</summary>
public enum ConversationAction : byte
{
    None = 0,
    OpenShop = 1,
    OpenQuests = 2,
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

public enum MapMoral : byte
{
    None = 0,
    Safe = 1,
    // Consequence-free PvP: behaves like an open (None) map for every mechanic (collision, grace,
    // regen, PvP permitted), but player-vs-player kills carry no stakes — no EXP loss, no drops, no
    // PK/aggressor flag, no reward — whenever either party is on an Arena map. Arena↔Safe combat is
    // still blocked exactly like None↔Safe.
    Arena = 2,
}

public enum ItemSource : byte
{
    TileDefined = 0,
    PlayerDropped = 1,
    NpcDropped = 2,
    // Items shed by a dying player. Behaves like PlayerDropped for pickup/persistence but is
    // exempt from the guard janitor sweep so a corpse in a safe zone stays lootable.
    PlayerDeathDropped = 3,
}

public enum ShopType : byte
{
    Store = 0,
    Inn = 1,
}

/// <summary>How a client wants an NpcInteract resolved (conversations added later). Auto (the
/// melee-key default) lets the server pick the NPC's best role — TALK-FIRST: a conversation if the NPC has one,
/// else a quest menu if it has an actionable quest for this player, else its keeper shop/inn. Shop / Talk / Quest
/// each FORCE one role — the context-menu items, and the conversation's terminal hand-off choices — so a forced
/// open can't loop back into a different menu.</summary>
public enum NpcInteractChoice : byte
{
    Auto = 0,
    Shop = 1,
    Talk = 2,
    Quest = 3,
    /// <summary>The player is opening ONE quest's offer, picked by name from the NPC's menu. Claims the NPC
    /// so accept and turn-in can resolve it, and sends nothing back — a quest menu pushed in reply would land
    /// on top of the offer they already have open.</summary>
    QuestOffer = 4,
}
