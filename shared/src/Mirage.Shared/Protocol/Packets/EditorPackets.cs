using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S: editor session ──────────────────────────────────────────────────────

public sealed record EditorLoginPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLogin;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

public sealed record EditorRequestItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
}

public sealed record EditorRequestNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestNpc;
    [JsonPropertyName("npcNum")] public int NpcNum { get; init; }
}

public sealed record EditorRequestShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
}

public sealed record EditorRequestSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
}

public sealed record EditorRequestMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

public sealed record EditorRequestClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
}

public sealed record EditorRequestMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
}

// ── Record locks ─────────────────────────────────────────────────────────────
// Two editors saving the same record is the one way work is silently lost: both read the same version, both
// write, and the second wins without either being told.
//
// A lock is taken the moment a record is DIRTIED, not when it is opened. Reading costs nothing and locks
// nothing, so browsing never shuts anybody out and the table only ever names people who actually have
// changes in hand. It is given up when those changes are saved or discarded, and everything a session holds
// falls away when it disconnects — a crashed editor cannot wedge a record shut.
//
// A lock belongs to a SESSION, not to an account. Two editors signed in as the same person are two sets of
// unsaved changes and block each other exactly like two people would; the account name is carried alongside
// only so a reader can be told who to go and ask.

/// <summary>Claims a record. Refused only when another session already holds it; re-claiming one you hold
/// is not an error.</summary>
public sealed record EditorLockPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLock;
    /// <summary>The section id the editor uses — "Maps", "Items", "NPCs" and so on.</summary>
    [JsonPropertyName("section")] public string Section { get; init; } = "";
    [JsonPropertyName("num")] public int Num { get; init; }
}

/// <summary>Gives a record back. Ignored unless the asking session is the holder.</summary>
public sealed record EditorUnlockPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorUnlock;
    [JsonPropertyName("section")] public string Section { get; init; } = "";
    [JsonPropertyName("num")] public int Num { get; init; }
}

/// <summary>The whole lock table, sent to every editor whenever it changes. A table rather than a delta so a
/// session that connects mid-flight, or misses a message, still ends up agreeing with the server.</summary>
public sealed record EditorLocksPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLocks;
    /// <summary><paramref name="Session"/> identifies the connection holding it and is what decides whether
    /// a lock is somebody else's. <paramref name="Login"/> is the account behind that connection, carried
    /// for display only — two sessions can share it.</summary>
    public sealed record Held(
        [property: JsonPropertyName("section")] string Section,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("session")] string Session);

    [JsonPropertyName("locks")] public Held[] Locks { get; init; } = [];
}

public sealed record EditorRequestAllItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllItems;
}

public sealed record EditorRequestAllNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllNpcs;
}

public sealed record EditorRequestAllShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllShops;
}

public sealed record EditorRequestAllSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllSpells;
}

public sealed record EditorRequestAllClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllClasses;
}

public sealed record EditorRequestAllMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllMapGroups;
}

public sealed record EditorSaveClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>The short pitch shown on the character-create screen. See <see cref="ClassRecord.Description"/>.</summary>
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    /// <summary>One sprite per sex; the character-create screen previews whichever the player picked.</summary>
    [JsonPropertyName("spriteMale")] public int SpriteMale { get; init; }
    [JsonPropertyName("spriteFemale")] public int SpriteFemale { get; init; }
    [JsonPropertyName("spriteSheetMale")] public int SpriteSheetMale { get; init; }
    [JsonPropertyName("spriteSheetFemale")] public int SpriteSheetFemale { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    /// <summary>The class's starting loadout. Carries the record types directly, as the NPC drop table
    /// does — every field on a starting line is authored, so a parallel DTO would only be a second shape
    /// to keep in step.</summary>
    [JsonPropertyName("startingItems")] public List<ClassStartingItem>? StartingItems { get; init; }
    [JsonPropertyName("startingSpells")] public List<int>? StartingSpells { get; init; }
}

public sealed record EditorSaveItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("pic")] public short Pic { get; init; }
    [JsonPropertyName("itemSheet")] public short ItemSheet { get; init; }
    [JsonPropertyName("type")] public ItemType Type { get; init; }
    // Type-specific fields; see ItemRecord for which apply to which ItemType.
    [JsonPropertyName("durability")] public short Durability { get; init; }
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("spellNum")] public short SpellNum { get; init; }
    [JsonPropertyName("power")] public short Power { get; init; }
    [JsonPropertyName("levelReq")] public short LevelReq { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    // Item restriction flags. See ItemRecord for behavior.
    [JsonPropertyName("nonTradeable")] public bool NonTradeable { get; init; }
    [JsonPropertyName("nonListable")] public bool NonListable { get; init; }
    [JsonPropertyName("nonMailable")] public bool NonMailable { get; init; }
    [JsonPropertyName("destroyOnDrop")] public bool DestroyOnDrop { get; init; }
    [JsonPropertyName("nonJunkable")] public bool NonJunkable { get; init; }
    [JsonPropertyName("price")] public int Price { get; init; }
}

public sealed record EditorSaveNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveNpc;
    [JsonPropertyName("npcNum")] public int NpcNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("attackSay")] public string AttackSay { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("spriteSheet")] public int SpriteSheet { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("spawnSecs")] public int SpawnSecs { get; init; }
    [JsonPropertyName("behavior")] public NpcBehavior Behavior { get; init; }
    [JsonPropertyName("group")] public int Group { get; init; }
    [JsonPropertyName("range")] public int Range { get; init; }
    /// <summary>The NPC's drop table. Null = drops nothing. Carries the record type directly, as
    /// <c>light</c> does with <c>LightSpec</c> — a parallel DTO would be a second shape to keep in step
    /// for no gain, since every field on a drop line is authored.</summary>
    [JsonPropertyName("drops")] public List<NpcDrop>? Drops { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("extraHp")] public int ExtraHp { get; init; }
    [JsonPropertyName("isBoss")] public bool IsBoss { get; init; }
    [JsonPropertyName("emitsLight")] public bool EmitsLight { get; init; }
    [JsonPropertyName("light")] public LightSpec Light { get; init; }
}

public sealed record EditorSaveShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("fixes")] public bool FixesItems { get; init; }
    [JsonPropertyName("shopType")] public ShopType ShopType { get; init; }
    [JsonPropertyName("allowBanking")] public bool AllowBanking { get; init; }
    // NPC template number this shop/inn is assigned to (0 = none) — the editor's Keeper picker. Drives the
    // client $ glyph + attack-key/right-click interact.
    [JsonPropertyName("keeper")] public int Keeper { get; init; }
    [JsonPropertyName("barters")] public BarterEntry[] Barters { get; init; } = [];
    /// <summary>Item numbers this shop sells for gold, priced from the item. Plain numbers, not rows —
    /// see <see cref="Records.ShopRecord.SalesItem"/>.</summary>
    [JsonPropertyName("sales")] public int[] Sales { get; init; } = [];

    public sealed record BarterEntry(
        [property: JsonPropertyName("giveItem")] int GiveItem,
        [property: JsonPropertyName("giveQuantity")] int GiveQuantity,
        [property: JsonPropertyName("getItem")] int GetItem,
        [property: JsonPropertyName("getQuantity")] int GetQuantity
    );
}

public sealed record EditorSaveSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("type")] public SpellType Type { get; init; }
    // Type-specific fields; see SpellRecord for which apply to which SpellType.
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("itemNum")] public short ItemNum { get; init; }
    [JsonPropertyName("itemQuantity")] public short ItemQuantity { get; init; }
    [JsonPropertyName("intReq")] public short IntReq { get; init; }
    [JsonPropertyName("levelReq")] public short LevelReq { get; init; }
}

public sealed record EditorSaveMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("map")] public SendMapPacket Map { get; init; } = null!;
}

// Editor-authored MapGroup fields. Moral + the environment bools are NULLABLE so a group can
// decline to provide one (null = inherit downstream). ControllingGuild is runtime state, NOT authored here —
// the server preserves it across a save.
public sealed record EditorSaveMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysLit")] public bool? AlwaysLit { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    // Map-enter/leave greeting fallback: a map inherits any blank greeting field from its group.
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    [JsonPropertyName("territory")] public bool Territory { get; init; }
}

// ── S→C: editor session ──────────────────────────────────────────────────────

public sealed record EditorLoginResponsePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLoginResponse;
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel AccessLevel { get; init; }
    /// <summary>Identifies this connection for the lifetime of the connection, so a session can tell its own
    /// locks from those of another session signed in as the same account. Fresh on every login, so a reused
    /// slot never inherits the last occupant's identity. Blank on a refused login.</summary>
    [JsonPropertyName("session")] public string SessionId { get; init; } = "";
}

public sealed record EditorDataPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorData;
    [JsonPropertyName("items")] public NameEntry[] Items { get; init; } = [];
    [JsonPropertyName("npcs")] public NameEntry[] Npcs { get; init; } = [];
    [JsonPropertyName("shops")] public NameEntry[] Shops { get; init; } = [];
    [JsonPropertyName("spells")] public NameEntry[] Spells { get; init; } = [];
    [JsonPropertyName("maps")] public NameEntry[] Maps { get; init; } = [];
    [JsonPropertyName("classes")] public NameEntry[] Classes { get; init; } = [];
    [JsonPropertyName("mapGroups")] public NameEntry[] MapGroups { get; init; } = [];
    [JsonPropertyName("quests")] public NameEntry[] Quests { get; init; } = [];
    [JsonPropertyName("conversations")] public NameEntry[] Conversations { get; init; } = [];
    /// <summary>Indices of the currency-type items, so the editor can validate drop quantities
    /// (currency needs a quantity; other item types ignore it) without fetching every full record.</summary>
    [JsonPropertyName("currencyItems")] public int[] CurrencyItems { get; init; } = [];
    /// <summary>Just enough of every item and spell to answer "could this class start with it?" — the
    /// class editor's starting-loadout tables have to evaluate the same equip and learn gates character
    /// creation will, and those need Power / VitalAmount, LevelReq and the class list.
    ///
    /// <para>Sent from the LIVE world rather than read from the editor's offline folder, which may be a
    /// different world entirely. Same reasoning as <see cref="CurrencyItems"/> above: a narrow projection
    /// of the facts the editor needs, not every full record.</para></summary>
    [JsonPropertyName("itemGates")] public ItemGate[] ItemGates { get; init; } = [];
    [JsonPropertyName("spellGates")] public SpellGate[] SpellGates { get; init; } = [];

    /// <summary>What the server calls the world an editor is now editing — its `world.json` name, blank
    /// when it has none.
    ///
    /// <para>It rides HERE and not on <see cref="ServerHelloPacket"/> because the hello goes to game
    /// clients too, and a world's name is for whoever is holding the records. A player never sees it; a
    /// mapper needs it, to know which of two servers they are connected to.</para></summary>
    [JsonPropertyName("worldName")] public string WorldName { get; init; } = "";

    // Price rides along with the gate facts rather than getting a packet of its own: the shop editor's sales
    // table shows what each listed item will actually cost, and that number lives on the item record. Both
    // consumers want "tell me about item N from the LIVE world", so one lookup serves them.
    public sealed record ItemGate(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("type")] ItemType Type,
        [property: JsonPropertyName("power")] int Power,
        [property: JsonPropertyName("levelReq")] short LevelReq,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses,
        [property: JsonPropertyName("price")] int Price = 0);

    public sealed record SpellGate(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("type")] SpellType Type,
        [property: JsonPropertyName("vitalAmount")] short VitalAmount,
        [property: JsonPropertyName("levelReq")] short LevelReq,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses);
    /// <summary>NPC footprint sizes (EffectiveSize, 1-based; index 0 unused) so the map editor renders +
    /// validates multi-tile spawn footprints without fetching every full NPC record.</summary>
    [JsonPropertyName("npcSizes")] public int[] NpcSizes { get; init; } = [];

    public sealed record NameEntry(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name
    );
}

public sealed record UpdateSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("type")] public SpellType Type { get; init; }
    // Type-specific fields; see SpellRecord for which apply to which SpellType.
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("itemNum")] public short ItemNum { get; init; }
    [JsonPropertyName("itemQuantity")] public short ItemQuantity { get; init; }
    [JsonPropertyName("intReq")] public short IntReq { get; init; }
    [JsonPropertyName("levelReq")] public short LevelReq { get; init; }
}

public sealed record UpdateShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("fixes")] public bool FixesItems { get; init; }
    [JsonPropertyName("shopType")] public ShopType ShopType { get; init; }
    [JsonPropertyName("allowBanking")] public bool AllowBanking { get; init; }
    [JsonPropertyName("keeper")] public int Keeper { get; init; }
    [JsonPropertyName("barters")] public EditorSaveShopPacket.BarterEntry[] Barters { get; init; } = [];
    [JsonPropertyName("sales")] public int[] Sales { get; init; } = [];
}

public sealed record UpdateClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    [JsonPropertyName("spriteMale")] public int SpriteMale { get; init; }
    [JsonPropertyName("spriteFemale")] public int SpriteFemale { get; init; }
    [JsonPropertyName("spriteSheetMale")] public int SpriteSheetMale { get; init; }
    [JsonPropertyName("spriteSheetFemale")] public int SpriteSheetFemale { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("startingItems")] public List<ClassStartingItem>? StartingItems { get; init; }
    [JsonPropertyName("startingSpells")] public List<int>? StartingSpells { get; init; }
}

public sealed record EditorAllItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllItems;
    [JsonPropertyName("items")] public UpdateItemPacket[] Items { get; init; } = [];
}

public sealed record EditorAllNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllNpcs;
    [JsonPropertyName("npcs")] public UpdateNpcPacket[] Npcs { get; init; } = [];
}

public sealed record EditorAllShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllShops;
    [JsonPropertyName("shops")] public UpdateShopPacket[] Shops { get; init; } = [];
}

public sealed record EditorAllSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllSpells;
    [JsonPropertyName("spells")] public UpdateSpellPacket[] Spells { get; init; } = [];
}

public sealed record EditorAllClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllClasses;
    [JsonPropertyName("classes")] public UpdateClassPacket[] Classes { get; init; } = [];
}

// S→C: one group's full state (RequestMapGroup response). Mirrors the authored fields; ControllingGuild is
// included read-only so the editor can surface who currently holds a territory.
public sealed record UpdateMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysLit")] public bool? AlwaysLit { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    [JsonPropertyName("territory")] public bool Territory { get; init; }
}

public sealed record EditorAllMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllMapGroups;
    [JsonPropertyName("mapGroups")] public UpdateMapGroupPacket[] MapGroups { get; init; } = [];
}

// ── Quest editor ─────────────────────────────────────────────────────────────

public sealed record EditorRequestQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
}

public sealed record EditorRequestAllQuestsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllQuests;
}

/// <summary>C→S: save an authored quest. Objectives/rewards ride the shared <see cref="Objective"/> /
/// <see cref="QuestReward"/> records (only non-empty entries are sent). Mirrors EditorSaveShopPacket.</summary>
public sealed record EditorSaveQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    [JsonPropertyName("obj")] public List<Objective> Objectives { get; init; } = new();
    [JsonPropertyName("reqLvl")] public int ReqLevel { get; init; }
    [JsonPropertyName("reqStr")] public int ReqStr { get; init; }
    [JsonPropertyName("reqDef")] public int ReqDef { get; init; }
    [JsonPropertyName("reqSpd")] public int ReqSpd { get; init; }
    [JsonPropertyName("reqInt")] public int ReqInt { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("prereq")] public int PrereqQuest { get; init; }
    [JsonPropertyName("rewExp")] public long RewardExp { get; init; }
    [JsonPropertyName("rewItems")] public List<QuestReward> RewardItems { get; init; } = new();
    [JsonPropertyName("repExp")] public long RepeatRewardExp { get; init; }
    [JsonPropertyName("repItems")] public List<QuestReward> RepeatRewardItems { get; init; } = new();
    [JsonPropertyName("giver")] public int GiverNpc { get; init; }
    [JsonPropertyName("turnIn")] public int TurnInNpc { get; init; }
    [JsonPropertyName("repeat")] public bool Repeatable { get; init; }
    [JsonPropertyName("cadence")] public QuestCadence Cadence { get; init; }
}

/// <summary>S→C: one quest's full definition — the RequestQuest response, an EditorAllQuests element, AND the
/// live broadcast to game clients on an editor save (so quest defs refresh without a reconnect, mirroring
/// the shop-keeper live-refresh). Identical field set to EditorSaveQuestPacket.</summary>
public sealed record UpdateQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    [JsonPropertyName("obj")] public List<Objective> Objectives { get; init; } = new();
    [JsonPropertyName("reqLvl")] public int ReqLevel { get; init; }
    [JsonPropertyName("reqStr")] public int ReqStr { get; init; }
    [JsonPropertyName("reqDef")] public int ReqDef { get; init; }
    [JsonPropertyName("reqSpd")] public int ReqSpd { get; init; }
    [JsonPropertyName("reqInt")] public int ReqInt { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("prereq")] public int PrereqQuest { get; init; }
    [JsonPropertyName("rewExp")] public long RewardExp { get; init; }
    [JsonPropertyName("rewItems")] public List<QuestReward> RewardItems { get; init; } = new();
    [JsonPropertyName("repExp")] public long RepeatRewardExp { get; init; }
    [JsonPropertyName("repItems")] public List<QuestReward> RepeatRewardItems { get; init; } = new();
    [JsonPropertyName("giver")] public int GiverNpc { get; init; }
    [JsonPropertyName("turnIn")] public int TurnInNpc { get; init; }
    [JsonPropertyName("repeat")] public bool Repeatable { get; init; }
    [JsonPropertyName("cadence")] public QuestCadence Cadence { get; init; }
}

public sealed record EditorAllQuestsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllQuests;
    [JsonPropertyName("quests")] public UpdateQuestPacket[] Quests { get; init; } = [];
}

// ── Conversation editor (NPC conversations) ──────────────────────────────────

public sealed record EditorRequestConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
}

public sealed record EditorRequestAllConversationsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllConversations;
}

/// <summary>C→S: save an authored conversation. The node tree (each node's choices) rides the shared
/// <see cref="ConversationNode"/> records (only non-empty entries are sent). Mirrors EditorSaveQuestPacket.</summary>
public sealed record EditorSaveConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("speaker")] public int SpeakerNpc { get; init; }
    [JsonPropertyName("root")] public int RootNodeId { get; init; }
    [JsonPropertyName("nodes")] public List<ConversationNode> Nodes { get; init; } = new();
}

/// <summary>S→C: one conversation's full definition — the RequestConversation response, an EditorAllConversations
/// element, AND the live broadcast to game clients on an editor save (so conversation defs + the "..." glyphs
/// refresh without a reconnect, mirroring the quest live-refresh). Identical field set to EditorSaveConversation.</summary>
public sealed record UpdateConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("speaker")] public int SpeakerNpc { get; init; }
    [JsonPropertyName("root")] public int RootNodeId { get; init; }
    [JsonPropertyName("nodes")] public List<ConversationNode> Nodes { get; init; } = new();
}

public sealed record EditorAllConversationsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllConversations;
    [JsonPropertyName("conversations")] public UpdateConversationPacket[] Conversations { get; init; } = [];
}

// -- Whole-world map fetch ---------------------------------------------------
// Every other record type answers a "give me all of them" in one packet, which maps cannot: a thousand of
// them at a couple of kilobytes each is a frame nothing should be asked to hold. So maps are asked for a
// slice at a time, which also gives the caller something honest to show a progress bar.

/// <summary>Asks for maps <c>Start</c> through <c>Start + Count - 1</c>. The server clamps
/// <see cref="Count"/> to its own chunk ceiling, so a caller that asks for everything at once gets a
/// smaller answer rather than an error.</summary>
public sealed record EditorRequestAllMapsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllMaps;
    [JsonPropertyName("start")] public int Start { get; init; } = 1;
    [JsonPropertyName("count")] public int Count { get; init; }
}

/// <summary>One slice of the world's maps. <see cref="Total"/> is the server's map ceiling, which is how a
/// caller learns when to stop asking.</summary>
public sealed record EditorAllMapsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllMaps;
    [JsonPropertyName("start")] public int Start { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Each carries its own MapNum, so the slice stays readable out of order.</summary>
    [JsonPropertyName("maps")] public SendMapPacket[] Maps { get; init; } = [];
}
