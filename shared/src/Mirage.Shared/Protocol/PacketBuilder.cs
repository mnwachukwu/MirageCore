using Mirage.Shared.Extensibility;
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

    /// <summary>Where a character may wear something in this game, in display order.</summary>
    public static EquipSlotsPacket EquipSlots(Extensibility.EquipSlotSet slots) =>
        new()
        {
            Slots = [.. slots.Slots.Select(s => new EquipSlotsPacket.Row(s.Key, s.LabelKey, s.Ordinal))],
        };

    /// <summary>What a character is wearing. Only the occupied slots travel — a slot missing from the
    /// list is a slot with nothing in it, so a game with twenty slots and one worn item sends one entry.</summary>
    public static EquippedGearPacket EquippedGear(int index, PlayerRecord p) =>
        new()
        {
            Index = index,
            Worn = [.. p.Equipped.Select(kv => new EquippedGearPacket.Entry(kv.Key, kv.Value))],
        };

    public static AlertMsgPacket Alert(string message) =>
        new() { Message = message };

    public static AlertMsgPacket Alert(string message, AlertCode code) =>
        new() { Message = message, Code = code };

    /// <summary>The account's characters, with whatever the loaded game says about each.
    ///
    /// <para>⚠ Projected HERE rather than on the client, because the client has not been told this
    /// world's attribute numbering yet — that arrives with the world, and this screen comes before
    /// it. Passing no fields gives a name and a sprite, which is Core by itself.</para></summary>
    public static SendCharsPacket SendChars(IEnumerable<PlayerRecord?> chars,
                                            DisplayFieldSet? fields = null) =>
        new()
        {
            Chars = chars.Select(p =>
                p is null || string.IsNullOrWhiteSpace(p.Name)
                    ? new SendCharsPacket.CharSlot("", 0)
                    : new SendCharsPacket.CharSlot(
                        p.Name, p.Sprite, p.SpriteSheet,
                        fields?.Project(DisplaySurfaces.CharacterSelect, p.Attributes).Rows)).ToArray()
        };

    // ── Game state ───────────────────────────────────────────────────────────

    public static WelcomePacket Welcome(int index, int guildCost = 0) =>
        new() { Index = index, GuildCost = guildCost };
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
            MarkedUntilUtc = p.MarkedUntilUtc,
            GraceUntilUtc = graceUntilUtc,
            AggressorUntilUtc = aggressorUntilUtc,
            GodMode = godMode,
            GuildId = guildId,
            GuildRank = guildRank,
            GuildName = guildName,
            GuildOpen = guildOpen,
            GuildColor = guildColor,
            GuildShowRank = guildShowRank,
            Downed = p.Downed,
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
            PlayersPassThrough = map.PlayersPassThrough,
            Music = map.Music,
            ExitMap = map.ExitMap,
            ExitX = map.ExitX,
            ExitY = map.ExitY,
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
    /// sync packet that carries a body's live state (MapNpcs, TraversalNpc) so the
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
    public static PartyPartnerPacket PartyPartner(int index, PlayerRecord p, OverheadBarSet bars,
        long combatExpiresAt, long pkGraceUntilUtc, long nowMs, long combatDurationMs)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bars);

        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool showAsMarked = p.IsMarked(nowUtc) && pkGraceUntilUtc <= nowUtc;
        int msSince = MsSinceCombat(combatExpiresAt, nowMs, combatDurationMs);

        // Read here rather than on the client: the recipient is somebody who usually cannot SEE this
        // body, so they have never been sent its attributes and never will be.
        var rows = new float[bars.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = bars.At(i)?.FractionIn(p.Attributes) ?? OverheadBar.Absent;

        return new()
        {
            Index = index,
            Name = p.TrimmedName,
            MapNum = p.Map, X = p.X, Y = p.Y,
            ShowAsMarked = showAsMarked,
            Access = p.Access,
            MsSinceCombat = msSince,
            Bars = rows,
        };
    }

    /// <summary>Empty-name notify that tells the recipient to tear down their party overlay.</summary>
    public static PartyPartnerPacket PartyCleared() => new();

    // ── Items ────────────────────────────────────────────────────────────────

    public static SendItemsPacket SendItems(IEnumerable<(int num, ItemRecord item)> items) =>
        new()
        {
            Items = items.Select(x => new SendItemsPacket.ItemData(
                x.num, x.item.Name, x.item.Pic, x.item.Type,
                x.item.Durability, x.item.VitalAmount, x.item.Power, x.item.Tier,
                x.item.NonTradeable, x.item.NonListable, x.item.NonMailable, x.item.DestroyOnDrop,
                x.item.NonJunkable, x.item.Price, x.item.ItemSheet, x.item.EquipSlot)).ToArray()
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
            Power = item.Power,
            Tier = item.Tier,
            EquipSlot = item.EquipSlot,
            NonTradeable = item.NonTradeable,
            NonListable = item.NonListable,
            NonMailable = item.NonMailable,
            DestroyOnDrop = item.DestroyOnDrop,
            NonJunkable = item.NonJunkable,
            Price = item.Price,
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
            Says = npc.Says,
            Sprite = npc.Sprite,
            SpriteSheet = npc.SpriteSheet,
            Size = npc.EffectiveSize,
            SpawnSecs = npc.SpawnSecs,
            Behavior = npc.Behavior,
            Group = npc.Group,
            Range = npc.Range,
            Standoff = npc.Standoff,
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

    public static ChatMsgPacket ChatMsg(string msg, int color, string channel) =>
        new() { Msg = msg, Color = color, Channel = channel };

    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel) =>
        ChatMsg(msg, color, Mirage.Shared.Protocol.ChatChannels.Name(channel));

    /// <summary>Player-originated chat overload. Carries speaker identity so the client can color
    /// the name and attach a right-click span. ShowAsMarked is frozen at send time.</summary>
    public static ChatMsgPacket ChatMsg(string msg, int color, string channel, string speakerName, AdminLevel speakerAccess, bool speakerShowAsMarked) =>
        new()
        {
            Msg = msg,
            Color = color,
            Channel = channel,
            SpeakerName = speakerName,
            SpeakerAccess = speakerAccess,
            SpeakerShowAsMarked = speakerShowAsMarked,
        };

    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel, string speakerName, AdminLevel speakerAccess, bool speakerShowAsMarked) =>
        ChatMsg(msg, color, Mirage.Shared.Protocol.ChatChannels.Name(channel), speakerName, speakerAccess, speakerShowAsMarked);

    /// <summary>S→C, once per session: the chat channels this game declared, beside Core's own.</summary>
    public static ChatChannelsPacket ChatChannels(ChatChannelSet channels) =>
        new() { Channels = channels.Channels };

    public static ChatBubblePacket ChatBubble(int playerIndex, string msg, byte kind) =>
        new() { PlayerIndex = playerIndex, Msg = msg, Kind = kind };

    public static NpcChatBubblePacket NpcChatBubble(int mapNum, int npcSlot, string msg) =>
        new() { MapNum = mapNum, NpcSlot = npcSlot, Msg = msg };

    /// <summary>The same line from a creature that is away from home — addressed by the identity it
    /// spawned with, since a guest holds no slot on the map it is standing on.</summary>
    public static NpcChatBubblePacket TraversalNpcChatBubble(int spawnMap, int spawnSlot, string msg) =>
        new() { NpcSlot = 0, SpawnMap = spawnMap, SpawnSlot = spawnSlot, Msg = msg };

    // ── Weather / time ───────────────────────────────────────────────────────

    public static PlayersOnlinePacket PlayersOnline(int count) => new() { Count = count };
    public static WeatherPacket Weather(WeatherType weather) => new() { Weather = weather };
    public static TimeOfDayPacket TimeOfDay(TimePhase phase, float progress) => new() { Phase = phase, Progress = progress };
}
