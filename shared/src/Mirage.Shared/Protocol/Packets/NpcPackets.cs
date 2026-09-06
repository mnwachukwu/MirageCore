using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

public sealed record SendNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendNpcs;
    [JsonPropertyName("npcs")] public NpcData[] Npcs { get; init; } = [];

    public sealed record NpcData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sprite")] int Sprite,
        // Footprint size class (1/2/3); the client renders the NPC at Size*32 px and treats it as an
        // SxS-tile body. Ships with the static template like Sprite/Spd.
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("behavior")] NpcBehavior Behavior,
        [property: JsonPropertyName("spawnSecs")] int SpawnSecs,
        // SPD is sent with the static template (not the per-tick snapshot) so the client can scale a running
        // NPC's move-slide to match the server's SPD-paced step cadence (MovementFormulas.NpcRunMsPerTile).
        [property: JsonPropertyName("spd")] int Spd,
        [property: JsonPropertyName("emitsLight")] bool EmitsLight,
        [property: JsonPropertyName("light")] LightSpec Light,
        // Keeper-shop KIND assigned to this NPC number (recomputed from ShopRecord.Keeper on shop edits):
        // 0 = none, 1 = store, 2 = inn. Drives the client's $ vendor glyph, the attack-key/right-click
        // interact routing, and the right-click menu label (Shop vs Inn). Static per template.
        [property: JsonPropertyName("keeperShop")] int KeeperShop = 0,
        // Which sprite sheet Sprite is a row of. Appended with a default rather than placed beside Sprite:
        // this is a positional record, so a field inserted mid-list silently re-reads every argument after
        // it at every construction site.
        [property: JsonPropertyName("spriteSheet")] int SpriteSheet = 0
    );
}

/// <summary>C→S: the player interacted with a map NPC — via the melee attack key aimed at it (primary), or a
/// right-click on it within r=5 (both range-enforced client-side). The server resolves what the NPC offers
/// (a keeper-assigned shop/inn) and opens it.</summary>
public sealed record NpcInteractPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcInteract;
    [JsonPropertyName("map")] public int MapNum { get; init; }
    [JsonPropertyName("slot")] public int NpcSlot { get; init; }
    // Auto (melee-key) = server resolves the best role (quest menu if actionable, else shop/inn); Shop = force
    // the keeper shop (the gossip-menu "Shop"/"Inn" item). Defaults to Auto.
    [JsonPropertyName("choice")] public NpcInteractChoice Choice { get; init; }
}

public sealed record NpcSpawnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcSpawn;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcSlot")] public int NpcSlot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("maxHp")] public int MaxHp { get; init; }
    [JsonPropertyName("maxMp")] public int MaxMp { get; init; }
    [JsonPropertyName("maxSp")] public int MaxSp { get; init; }
    // Two-layer world: the logical layer the NPC spawned on (Ground default; a bridge-top spawn pin or a guest
    // returning home onto a fringe tile can spawn on Fringe). Omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
}

public sealed record MapNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapNpcs;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("npcs")] public MapNpcData[] Npcs { get; init; } = [];

    public sealed record MapNpcData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("hp")] int Hp,
        [property: JsonPropertyName("maxHp")] int MaxHp,
        [property: JsonPropertyName("mp")] int Mp,
        [property: JsonPropertyName("maxMp")] int MaxMp,
        [property: JsonPropertyName("sp")] int Sp,
        [property: JsonPropertyName("maxSp")] int MaxSp,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("dir")] Direction Dir,
        // int.MaxValue = not in combat.  Otherwise ms elapsed since the NPC entered combat — see
        // SendHpPacket.MsSinceCombat for the rationale; lets re-syncing observers see the bar fade
        // at the true server time instead of restarting a 10s window on every region re-sync.
        // Default = int.MaxValue so older servers (no combatMs field) don't deserialize as "combat now".
        [property: JsonPropertyName("combatMs")] int MsSinceCombat = int.MaxValue,
        [property: JsonPropertyName("hasTarget")] bool HasTarget = false,
        // Two-layer world: the NPC's logical layer (ground vs bridge-top fringe); omitted on the wire when Ground.
        [property: JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground
    );
}

/// <summary>
/// S→C full state of a traversal NPC (one chasing across a seamless border).  Keyed by its
/// permanent (SpawnMapNum, SpawnSlot) identity, it places/updates the NPC on CurrentMapNum's
/// grid cell.  Sent on cross, move, attack, damage and target change — the client creates the
/// NPC by identity if new, moves it between cells when CurrentMapNum changes, else updates it.
/// </summary>
public sealed record TraversalNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TraversalNpc;
    [JsonPropertyName("spawnMap")] public int SpawnMapNum { get; init; }
    [JsonPropertyName("spawnSlot")] public int SpawnSlot { get; init; }
    [JsonPropertyName("curMap")] public int CurrentMapNum { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("dir")] public Direction Dir { get; init; }
    [JsonPropertyName("movement")] public MovementType Movement { get; init; }
    // True when CurrentMapNum changed via a contiguous one-tile BORDER step (not a warp/teleport or a
    // fresh appearance): the client slides the sprite across the seam in Dir instead of popping it.
    [JsonPropertyName("stepped")] public bool Stepped { get; init; }
    [JsonPropertyName("hp")] public int Hp { get; init; }
    [JsonPropertyName("maxHp")] public int MaxHp { get; init; }
    // int.MaxValue = not in combat.  Otherwise ms elapsed since the NPC entered combat — see
    // SendHpPacket.MsSinceCombat for the rationale.
    [JsonPropertyName("combatMs")] public int MsSinceCombat { get; init; } = int.MaxValue;
    [JsonPropertyName("hasTarget")] public bool HasTarget { get; init; }
    [JsonPropertyName("attacking")] public bool Attacking { get; init; }
    // Combat extras: when Damage != 0 the client floats a damage number at (X,Y) on CurrentMapNum;
    // Dead = true means the player landed the kill blow — the client removes the guest after the number.
    [JsonPropertyName("damage")] public int Damage { get; init; }
    [JsonPropertyName("isCrit")] public bool IsCrit { get; init; }
    [JsonPropertyName("dead")] public bool Dead { get; init; }
    // Two-layer world: the guest's logical layer, carried across the seam so an observer renders it on the
    // correct layer; omitted on the wire when Ground.
    [JsonPropertyName("layer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorldLayer Layer { get; init; }
}

/// <summary>S→C: a traversal NPC silently leaves the world (returned home) — no death, no loot.</summary>
public sealed record NpcDespawnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NpcDespawn;
    [JsonPropertyName("spawnMap")] public int SpawnMapNum { get; init; }
    [JsonPropertyName("spawnSlot")] public int SpawnSlot { get; init; }
}

public sealed record UpdateNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateNpc;
    [JsonPropertyName("npcNum")] public int NpcNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("attackSay")] public string AttackSay { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("spriteSheet")] public int SpriteSheet { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("behavior")] public NpcBehavior Behavior { get; init; }
    [JsonPropertyName("group")] public int Group { get; init; }
    [JsonPropertyName("spawnSecs")] public int SpawnSecs { get; init; }
    [JsonPropertyName("range")] public int Range { get; init; }
    [JsonPropertyName("drops")] public List<NpcDrop>? Drops { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("extraHp")] public int ExtraHp { get; init; }
    [JsonPropertyName("isBoss")] public bool IsBoss { get; init; }
    [JsonPropertyName("emitsLight")] public bool EmitsLight { get; init; }
    [JsonPropertyName("light")] public LightSpec Light { get; init; }
    // Keeper-shop KIND (0 none / 1 store / 2 inn) so a live shop/keeper edit refreshes the $ glyph +
    // interact routing + menu label without a reconnect. Mirrors SendNpcsPacket.NpcData.KeeperShop.
    [JsonPropertyName("keeperShop")] public int KeeperShop { get; init; }
}
