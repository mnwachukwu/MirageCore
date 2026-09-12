using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Shared.Protocol;

/// <summary>
/// Factory methods that construct typed packet POCOs from game state values.
/// Pass the returned packet to PacketSerializer.Serialize() for sending.
/// </summary>
public static partial class PacketBuilder
{
    // ── Account ──────────────────────────────────────────────────────────────

    public static AlertMsgPacket Alert(string message) =>
        new() { Message = message };

    public static AlertMsgPacket Alert(string message, AlertCode code) =>
        new() { Message = message, Code = code };

    public static SendCharsPacket SendChars(IEnumerable<PlayerRecord?> chars) =>
        new()
        {
            Chars = chars.Select(p =>
                p is null || string.IsNullOrWhiteSpace(p.Name)
                    ? new SendCharsPacket.CharSlot("", 0)
                    : new SendCharsPacket.CharSlot(p.Name, p.Sprite, p.SpriteSheet)).ToArray()
        };

    // ── Game state ───────────────────────────────────────────────────────────

    public static WelcomePacket Welcome(int index) => new() { Index = index };
    public static PlayerInGamePacket PlayerInGame() => new();
    public static LeftGamePacket LeftGame(int index) => new() { Index = index };

    public static SendPlayerDataPacket PlayerData(int index, PlayerRecord p, int mapNum,
        long graceUntilUtc = 0, long aggressorUntilUtc = 0,
        int? guildId = null, GuildRank? guildRank = null, string? guildName = null, bool? guildOpen = null,
        int? guildColor = null, bool? guildShowRank = null,
        bool? godMode = null) =>
        new()
        {
            Index = index,
            Name = p.Name,
            Sprite = p.Sprite,
            SpriteSheet = p.SpriteSheet,
            X = p.X,
            Y = p.Y,
            Dir = p.Dir,
            Layer = p.Layer,
            Map = mapNum,
            MoveSpeed = p.MoveSpeed,
            Access = p.Access,
            PkExpiryUtc = p.PkExpiryUtc,
            GraceUntilUtc = graceUntilUtc,
            AggressorUntilUtc = aggressorUntilUtc,
            GodMode = godMode,
            GuildId = guildId,
            GuildRank = guildRank,
            GuildName = guildName,
            GuildOpen = guildOpen,
            GuildColor = guildColor,
            GuildShowRank = guildShowRank,
            Dead = p.Dead,
            RespawnReadyUtc = p.RespawnReadyUtc,
        };

    public static AggressorRefreshPacket AggressorRefresh(int index, long aggressorUntilUtc) =>
        new() { Index = index, AggressorUntilUtc = aggressorUntilUtc };

    // ── Map ──────────────────────────────────────────────────────────────────

    // Sends the map's RAW inheritable fields — 0 / null mean "inherit from the MapGroup". Both the
    // editor and the game client resolve the effective value themselves against their cached group (the client
    // caches groups from SendMapGroupsPacket / UpdateMapGroupPacket, then resolves via MapGroupResolve). This is
    // why a group edit needs no map re-send: the map packet never carries a group-derived value to go stale.
    // forEditor = true also carries the NPC entries' authoring pins + the greeting (the game client renders NPCs
    // from live spawn packets and the server speaks the greeting, so both stay off the game-client wire).
    public static SendMapPacket SendMap(int mapNum, MapRecord map, int col = 1, int row = 1, bool forEditor = false)
    {
        var tiles = new List<SendMapPacket.TileData>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var t = map.Tile[x, y];
                // Sparse: omit fully-default tiles; the client rebuilds from a blank grid.
                if (!SendMapPacket.TileData.IsDefault(t))
                    tiles.Add(SendMapPacket.TileData.From(x, y, t));
            }
        }

        return new SendMapPacket
        {
            MapNum = mapNum,
            Col = col,
            Row = row,
            Width = map.Width,
            Height = map.Height,
            Revision = map.Revision,
            Name = map.Name,
            DisplayName = map.DisplayName,
            // Raw inheritable fields (0 / null = inherit); resolved client-side against the cached group.
            Moral = map.Moral,
            Music = map.Music,
            BootMap = map.BootMap,
            BootX = map.BootX,
            BootY = map.BootY,
            Indoors = map.Indoors,
            AlwaysLit = map.AlwaysLit,
            AlwaysDark = map.AlwaysDark,
            MapGroup = map.MapGroup,
            Up = map.Up,
            Down = map.Down,
            Left = map.Left,
            Right = map.Right,
            Tiles = tiles.ToArray(),
            // Editor gets full entries (pins for the authoring round-trip); the game client gets the same NPC
            // types with pins stripped — it renders NPCs from live spawn packets and never needs the pins.
            Npcs = forEditor
                ? map.Npcs.ToArray()
                : map.Npcs.Select(e => e with { PinX = null, PinY = null }).ToArray(),
            Lights = map.Lights.ToArray(),
            // Editor-only authoring data — the SERVER speaks the map greeting from its own MapRecord (client never needs it).
            GreetingSpeaker = forEditor ? map.GreetingSpeaker : "",
            JoinSay = forEditor ? map.JoinSay : "",
            LeaveSay = forEditor ? map.LeaveSay : "",
        };
    }

    public static JoinMapPacket JoinMap(int index) => new() { Index = index };
    public static LeaveMapPacket LeaveMap(int index) => new() { Index = index };
    public static PlayerXYPacket PlayerXY(int index, int x, int y) => new() { Index = index, X = x, Y = y };

    // ── Movement ─────────────────────────────────────────────────────────────

    public static SendPlayerMovePacket PlayerMove(
        int index, int x, int y, Direction dir, MovementType movement, WorldLayer layer = WorldLayer.Ground) =>
        new() { Index = index, X = x, Y = y, Dir = dir, Movement = movement, Layer = layer };

    /// <summary>
    /// Converts a server-side <paramref name="combatExpiresAt"/> to wire-format ms elapsed since the
    /// combat window opened.  Returns <see cref="int.MaxValue"/> when not in combat.  Used by every
    /// sync packet that carries combat state (PartyVitals, SendHp, MapNpcs, TraversalNpc) so the
    /// client can compute the right LastCombatMs stamp on its own clock instead of restarting the
    /// 10s window each time it re-observes an entity.
    /// </summary>
    public static int MsSinceCombat(long combatExpiresAt, long nowMs, long combatDurationMs) =>
        (combatExpiresAt > 0 && nowMs < combatExpiresAt)
            ? (int)(combatDurationMs - (combatExpiresAt - nowMs))
            : int.MaxValue;

    /// <summary>
    /// Snapshot of a partnered player pushed to the partner.  <paramref name="combatExpiresAt"/> = 0
    /// (or in the past) means "not in combat" and lands as int.MaxValue on the wire — the client
    /// converts to its own clock and runs the existing 10 s combat-window check.
    /// </summary>
    public static PartyVitalsPacket PartyVitals(int index, PlayerRecord p, long combatExpiresAt,
        long pkGraceUntilUtc, long nowMs, long combatDurationMs)
    {
        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool showAsPk = p.IsPk(nowUtc) && pkGraceUntilUtc <= nowUtc;
        int msSince = MsSinceCombat(combatExpiresAt, nowMs, combatDurationMs);
        return new()
        {
            Index = index,
            Name = p.TrimmedName,
            MapNum = p.Map, X = p.X, Y = p.Y,
            ShowAsPk = showAsPk,
            Access = p.Access,
            MsSinceCombat = msSince,
        };
    }

    /// <summary>Empty-name notify that tells the recipient to tear down their party overlay.</summary>
    public static PartyVitalsPacket PartyCleared() => new();

    // ── Items ────────────────────────────────────────────────────────────────

    public static SendItemsPacket SendItems(IEnumerable<(int num, ItemRecord item)> items) =>
        new()
        {
            Items = items.Select(x => new SendItemsPacket.ItemData(
                x.num, x.item.Name, x.item.Pic, x.item.Type,
                x.item.Durability, x.item.VitalAmount, x.item.SpellNum, x.item.Power, x.item.LevelReq,
                x.item.NonTradeable, x.item.NonListable, x.item.NonMailable, x.item.DestroyOnDrop,
                x.item.NonJunkable, x.item.Price, x.item.ItemSheet)).ToArray()
        };

    public static UpdateItemPacket UpdateItem(int itemNum, ItemRecord item) =>
        new()
        {
            ItemNum = itemNum,
            Name = item.Name,
            Pic = item.Pic,
            ItemSheet = item.ItemSheet,
            Type = item.Type,
            Durability = item.Durability,
            VitalAmount = item.VitalAmount,
            SpellNum = item.SpellNum,
            Power = item.Power,
            LevelReq = item.LevelReq,
            NonTradeable = item.NonTradeable,
            NonListable = item.NonListable,
            NonMailable = item.NonMailable,
            DestroyOnDrop = item.DestroyOnDrop,
            NonJunkable = item.NonJunkable,
            Price = item.Price,
        };

    // ── Spell ────────────────────────────────────────────────────────────────

    /// <summary>The one place a <see cref="SpellRecord"/> becomes an <see cref="UpdateSpellPacket"/> —
    /// used for the single-spell editor response, the bulk editor list and the post-save broadcast alike,
    /// so a field added to the record reaches all three by editing this.</summary>
    public static UpdateSpellPacket UpdateSpell(int spellNum, SpellRecord spell) =>
        new()
        {
            SpellNum = spellNum,
            Name = spell.Name,
            Type = spell.Type,
            VitalAmount = spell.VitalAmount,
            ItemNum = spell.ItemNum,
            ItemQuantity = spell.ItemQuantity,
            IntReq = spell.IntReq,
            LevelReq = spell.LevelReq,
        };

    // ── Npc / Shop / Class ───────────────────────────────────────────────────

    /// <summary>The one place an <see cref="NpcRecord"/> becomes an <see cref="UpdateNpcPacket"/>.
    /// <paramref name="keeperShopKind"/> is derived from the world (0 none / 1 store / 2 inn), so it is
    /// passed in rather than read off the record.</summary>
    public static UpdateNpcPacket UpdateNpc(int npcNum, NpcRecord npc, int keeperShopKind) =>
        new()
        {
            NpcNum = npcNum,
            Name = npc.Name,
            AttackSay = npc.AttackSay,
            Sprite = npc.Sprite,
            SpriteSheet = npc.SpriteSheet,
            Size = npc.EffectiveSize,
            SpawnSecs = npc.SpawnSecs,
            Behavior = npc.Behavior,
            Group = npc.Group,
            Range = npc.Range,
            // Copied, not aliased: a packet outlives this call and the record stays editable.
            Drops = npc.Drops is null ? null : new List<NpcDrop>(npc.Drops),
            IsBoss = npc.IsBoss,
            EmitsLight = npc.EmitsLight,
            Light = npc.Light,
            KeeperShop = keeperShopKind,
        };

    /// <summary>The one place a <see cref="ShopRecord"/> becomes an <see cref="UpdateShopPacket"/>.</summary>
    public static UpdateShopPacket UpdateShop(int shopNum, ShopRecord shop) =>
        new()
        {
            ShopNum = shopNum,
            Name = shop.Name,
            FixesItems = shop.FixesItems,
            ShopType = shop.ShopType,
            AllowBanking = shop.AllowBanking,
            Keeper = shop.Keeper,
            Barters = shop.BarterItem
                .Select(t => new EditorSaveShopPacket.BarterEntry(
                    t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
                .ToArray(),
            Sales = [.. shop.SalesItem],
        };

    // ── Chat ─────────────────────────────────────────────────────────────────

    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel) =>
        new() { Msg = msg, Color = color, Channel = channel };

    /// <summary>Player-originated chat overload. Carries speaker identity so the client can color
    /// the name and attach a right-click span. ShowAsPk is frozen at send time.</summary>
    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel, string speakerName, AdminLevel speakerAccess, bool speakerShowAsPk) =>
        new()
        {
            Msg = msg,
            Color = color,
            Channel = channel,
            SpeakerName = speakerName,
            SpeakerAccess = speakerAccess,
            SpeakerShowAsPk = speakerShowAsPk,
        };

    public static ChatBubblePacket ChatBubble(int playerIndex, string msg, byte kind) =>
        new() { PlayerIndex = playerIndex, Msg = msg, Kind = kind };

    public static NpcChatBubblePacket NpcChatBubble(int mapNum, int npcSlot, string msg, byte kind) =>
        new() { MapNum = mapNum, NpcSlot = npcSlot, Msg = msg, Kind = kind };

    public static NpcChatBubblePacket TraversalNpcChatBubble(int spawnMap, int spawnSlot, string msg, byte kind) =>
        new() { NpcSlot = 0, SpawnMap = spawnMap, SpawnSlot = spawnSlot, Msg = msg, Kind = kind };

    // ── Weather / time ───────────────────────────────────────────────────────

    public static PlayersOnlinePacket PlayersOnline(int count) => new() { Count = count };
    public static WeatherPacket Weather(WeatherType weather) => new() { Weather = weather };
    public static TimeOfDayPacket TimeOfDay(TimePhase phase, float progress) => new() { Phase = phase, Progress = progress };
}
