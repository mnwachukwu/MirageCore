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
    Key = 5,
    KeyOpen = 6,
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
    Spell = 2,
}

public enum ItemType : byte
{
    None = 0,
    Weapon = 1,
    Armor = 2,
    Helmet = 3,
    Shield = 4,
    PotionAddHp = 5,
    PotionAddMp = 6,
    PotionAddSp = 7,
    PotionSubHp = 8,
    PotionSubMp = 9,
    PotionSubSp = 10,
    Key = 11,
    Currency = 12,
    Spell = 13,
}

public enum NpcBehavior : byte
{
    AttackOnSight = 0,
    AttackWhenAttacked = 1,
    Friendly = 2,
    Stationary = 3,
    Guard = 4,
}

public enum SpellType : byte
{
    AddHp = 0,
    AddMp = 1,
    AddSp = 2,
    SubHp = 3,
    SubMp = 4,
    SubSp = 5,
    GiveItem = 6,
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

/// <summary>The derived status of a <see cref="Records.GuildWar"/> entry from one guild's perspective —
/// computed (not stored) via <see cref="GuildWarFormulas.Status"/> for display + the combat ruleset.
/// A one-sided grievance is <see cref="Warmup"/> until it goes live, then splits into aggressor/defender;
/// once both guilds have declared it is <see cref="Mutual"/>.</summary>
public enum GuildWarStatus : byte
{
    /// <summary>A one-sided declaration still inside its warmup grace — not yet live (either side's view).</summary>
    Warmup = 0,
    /// <summary>Live one-sided war, this guild is the aggressor: it bears the war-death cost and the daily
    /// maintenance tax.</summary>
    OneSidedAggressor = 1,
    /// <summary>Live one-sided war, this guild is the defender: it loses nothing and pays nothing (it can
    /// return the declaration to go mutual, or simply wait the aggressor out).</summary>
    OneSidedDefender = 2,
    /// <summary>Live mutual war (both guilds declared) — the full war of attrition, with zero ongoing tax
    /// for either side.</summary>
    Mutual = 3,
}

/// <summary>The kind of war action an Officer has queued for Leader approval: a declaration
/// (which the Leader's acceptance resolves as a fresh grudge or a reciprocation, whichever applies), a
/// retraction of a still one-sided declaration, or suing for peace (conceding a mutual war). See
/// <see cref="Records.GuildWarRequest"/>.</summary>
public enum GuildWarRequestKind : byte
{
    Declare = 0,
    Retract = 1,
    Peace = 2,
    // Territory challenge: the same Officer-requests / Leader-approves queue, but TargetIndex is a
    // MapGroup (territory) index rather than a guild index.
    TerritoryChallenge = 3,
    TerritoryWithdraw = 4,
}

/// <summary>Cadence of a creator <c>/guildreset</c>: run the day's routines, or additionally the
/// weekly ones (financial reset + hold-score accrual + income roll), or additionally the season end. Ordered
/// so a larger scope includes the smaller (Day < Week < Season).</summary>
public enum SettlementScope : byte
{
    Day = 0,
    Week = 1,
    Season = 2,
}

/// <summary>A creator territory-war debug action: start a war night off-schedule (full ramp-up),
/// advance the live contest one phase, or bring it straight to cooldown.</summary>
public enum TerritoryWarDebugAction : byte
{
    Start = 0,
    Advance = 1,
    End = 2,
}

/// <summary>A peace action on a mutual war: the OFFERER sues for peace (a concession) or
/// withdraws a pending offer; the OPPONENT accepts (they win, the war ends) or rejects it.</summary>
public enum GuildWarPeaceAction : byte
{
    Offer = 0,
    Withdraw = 1,
    Accept = 2,
    Reject = 3,
}

/// <summary>A wager action on a mutual war: the PROPOSER offers a matched ante (or withdraws a
/// pending proposal); the OPPONENT accepts (both escrow it) or rejects it. Leader-only either side.</summary>
public enum GuildWarWagerAction : byte
{
    Propose = 0,
    Withdraw = 1,
    Accept = 2,
    Reject = 3,
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
    Seasonally = 4,
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

public enum Sex : byte
{
    Male = 0,
    Female = 1,
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
