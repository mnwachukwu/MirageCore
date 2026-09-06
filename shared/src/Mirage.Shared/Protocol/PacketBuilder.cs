using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Shared.Protocol;

/// <summary>
/// Factory methods that construct typed packet POCOs from game state values.
/// Pass the returned packet to PacketSerializer.Serialize() for sending.
/// </summary>
public static class PacketBuilder
{
    // ── Account ──────────────────────────────────────────────────────────────

    public static AlertMsgPacket Alert(string message) =>
        new() { Message = message };

    public static AlertMsgPacket Alert(string message, AlertCode code) =>
        new() { Message = message, Code = code };

    public static SendClassesPacket SendClasses(IEnumerable<ClassRecord> classes) =>
        new()
        {
            Classes = classes.Select(c => new SendClassesPacket.ClassData(
                c.Name, c.SpriteMale, c.SpriteFemale, c.Str, c.Def, c.Spd, c.Int, c.Description,
                Worn: null, Carried: null, Spells: null,
                SpriteSheetMale: c.SpriteSheetMale, SpriteSheetFemale: c.SpriteSheetFemale)).ToArray()
        };

    /// <summary>The class list for the character-create screen, with each class's resolved starting
    /// loadout and the definitions needed to describe it.
    ///
    /// <para><paramref name="classes"/>, <paramref name="items"/> and <paramref name="spells"/> are the
    /// world's own 1-based tables; index 0 is skipped, so the packet's array is 0-based and class number
    /// <c>n</c> is at position <c>n-1</c> — the same shape <see cref="SendClasses"/> produces.</para>
    ///
    /// <para>Blank class slots are kept rather than filtered: the client picks by list position and
    /// already skips nameless entries, and dropping them here would renumber every class after the
    /// gap.</para></summary>
    public static NewCharClassesPacket NewCharClasses(ClassRecord[] classes, ItemRecord[] items, SpellRecord[] spells)
    {
        var data = new SendClassesPacket.ClassData[Math.Max(0, classes.Length - 1)];
        // Sorted so the catalogs come out in a stable order — a packet that differs only in dictionary
        // ordering between two runs is a diff nobody wants to read.
        var itemNums = new SortedSet<int>();
        var spellNums = new SortedSet<int>();

        for (int n = 1; n < classes.Length; n++)
        {
            var c = classes[n];
            var granted = StartingLoadout.ResolveItems(c, n, items);
            var known = StartingLoadout.ResolveSpells(c, n, spells);

            var worn = granted.Where(g => g.Worn).Select(g => g.Num).ToArray();
            var carried = granted.Where(g => !g.Worn)
                .Select(g => new SendClassesPacket.CarriedItem(g.Num, g.Value)).ToArray();

            foreach (var g in granted) itemNums.Add(g.Num);
            foreach (int s in known) spellNums.Add(s);

            // Empty groups go out as null (and so off the wire entirely) rather than as []: a class that
            // starts with no armor is a real design statement the screen shows, but it is the SHAPE of
            // the loadout that says so, not an empty array in the payload.
            data[n - 1] = new SendClassesPacket.ClassData(
                c.Name, c.SpriteMale, c.SpriteFemale, c.Str, c.Def, c.Spd, c.Int, c.Description,
                worn.Length > 0 ? worn : null,
                carried.Length > 0 ? carried : null,
                known.Count > 0 ? [.. known] : null,
                c.SpriteSheetMale, c.SpriteSheetFemale);
        }

        // The casting reagent rides along if any starting spell drains HP. It is not granted to anyone —
        // it is there because that spell's tooltip quotes a per-cast reagent cost BY NAME, and the name
        // lives on the item record. Without it the very first spell a caster sees reads "?: 3".
        if (Constants.CastingReagentItemIndex < items.Length
            && !string.IsNullOrEmpty(items[Constants.CastingReagentItemIndex].Name)
            && spellNums.Any(s => spells[s].Type == SpellType.SubHp))
        {
            itemNums.Add(Constants.CastingReagentItemIndex);
        }

        return new NewCharClassesPacket
        {
            Classes = data,
            ItemDefs = itemNums.Select(n => ItemDefOf(n, items[n])).ToArray(),
            SpellDefs = spellNums.Select(n => SpellDefOf(n, spells[n])).ToArray(),
        };
    }

    private static NewCharClassesPacket.ItemDef ItemDefOf(int num, ItemRecord it) =>
        new(num, it.Name, it.Pic, it.Type, it.Durability, it.VitalAmount, it.Power, it.LevelReq,
            it.AllowedClasses, it.ItemSheet);

    private static NewCharClassesPacket.SpellDef SpellDefOf(int num, SpellRecord sp) =>
        new(num, sp.Name, sp.Type, sp.VitalAmount, sp.IntReq, sp.LevelReq, sp.AllowedClasses);

    public static SendCharsPacket SendChars(IEnumerable<PlayerRecord?> chars, ClassRecord[] classes) =>
        new()
        {
            Chars = chars.Select(p =>
            {
                if (p is null || string.IsNullOrWhiteSpace(p.Name))
                    return new SendCharsPacket.CharSlot("", 0, 0, 0, "");
                string cls = p.Class > 0 && p.Class < classes.Length && classes[p.Class]?.Name is { Length: > 0 } n
                    ? n.Trim() : "";
                return new SendCharsPacket.CharSlot(p.Name, p.Level, p.Class, p.Sprite, cls, p.SpriteSheet);
            }).ToArray()
        };

    // ── Game state ───────────────────────────────────────────────────────────

    public static WelcomePacket Welcome(int index) => new() { Index = index };
    public static PlayerInGamePacket PlayerInGame() => new();
    public static LeftGamePacket LeftGame(int index) => new() { Index = index };

    public static SendPlayerDataPacket PlayerData(int index, PlayerRecord p, int mapNum,
        long graceUntilUtc = 0, long aggressorUntilUtc = 0,
        int? guildId = null, GuildRank? guildRank = null, string? guildName = null, bool? guildOpen = null,
        int? guildColor = null, bool? guildShowRank = null, int? guildStanding = null,
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
            Level = p.Level,
            Class = p.Class,
            Sex = p.Sex,
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
            GuildStanding = guildStanding,
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

    // ── Vitals ───────────────────────────────────────────────────────────────

    public static SendHpPacket SendHp(int index, int hp, int maxHp,
        bool showFloat = false, bool isCrit = false, int damage = 0, int msSinceCombat = int.MaxValue) =>
        new() { Index = index, Hp = hp, MaxHp = maxHp, ShowFloat = showFloat, IsCrit = isCrit, Damage = damage, MsSinceCombat = msSinceCombat };

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

    public static SendMpPacket SendMp(int index, int mp, int maxMp,
        bool showFloat = false) =>
        new() { Index = index, Mp = mp, MaxMp = maxMp, ShowFloat = showFloat };

    public static SendSpPacket SendSp(int index, int sp, int maxSp,
        bool showFloat = false) =>
        new() { Index = index, Sp = sp, MaxSp = maxSp, ShowFloat = showFloat };

    public static SendStatsPacket SendStats(PlayerRecord p) =>
        new()
        {
            Str = p.Str,
            Def = p.Def,
            Spd = p.Spd,
            Int = p.Int,
            Points = p.Points,
            Level = p.Level,
            Exp = p.Exp,
        };

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
            Level = p.Level,
            Hp = p.Hp, MaxHp = p.MaxHp,
            Mp = p.Mp, MaxMp = p.MaxMp,
            Sp = p.Sp, MaxSp = p.MaxSp,
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
                // Copied, not aliased: a packet outlives this call and the record stays editable.
                x.item.AllowedClasses is null ? null : new List<short>(x.item.AllowedClasses),
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
            AllowedClasses = item.AllowedClasses is null ? null : new List<short>(item.AllowedClasses),
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
            // Copied, not aliased: a packet outlives this call and the record stays editable.
            AllowedClasses = spell.AllowedClasses is null ? null : new List<short>(spell.AllowedClasses),
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
            Str = npc.Str,
            Def = npc.Def,
            Spd = npc.Spd,
            Int = npc.Int,
            ExtraHp = npc.ExtraHp,
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

    /// <summary>The one place a <see cref="ClassRecord"/> becomes an <see cref="UpdateClassPacket"/>.</summary>
    public static UpdateClassPacket UpdateClass(int classNum, ClassRecord cls) =>
        new()
        {
            ClassNum = classNum,
            Name = cls.Name,
            Description = cls.Description,
            SpriteMale = cls.SpriteMale,
            SpriteFemale = cls.SpriteFemale,
            SpriteSheetMale = cls.SpriteSheetMale,
            SpriteSheetFemale = cls.SpriteSheetFemale,
            Str = cls.Str,
            Def = cls.Def,
            Spd = cls.Spd,
            Int = cls.Int,
            StartingItems = cls.StartingItems is null ? null : new List<ClassStartingItem>(cls.StartingItems),
            StartingSpells = cls.StartingSpells is null ? null : new List<int>(cls.StartingSpells),
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
