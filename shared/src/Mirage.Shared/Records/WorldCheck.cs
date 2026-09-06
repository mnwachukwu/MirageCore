namespace Mirage.Shared.Records;

/// <summary>Which record family a finding was found ON, so whoever shows it knows where to send the author.</summary>
public enum WorldRecordKind
{
    Map,
    Item,
    Npc,
    Shop,
    Spell,
    Quest,
    Conversation,
    Class,
}

/// <summary>What one world-check finding is about. The kind decides the wording; the numbers on the issue
/// itself say where.</summary>
public enum WorldIssueKind
{
    // ── The map graph ────────────────────────────────────────────────────────

    /// <summary>A link joins two maps of different sizes. World coordinates run straight across a seam, so
    /// a step over this one lands somewhere other than where it looks.</summary>
    LinkSizeMismatch,

    /// <summary>A link names a map that does not answer with the opposite link, so the seam works one way.</summary>
    LinkNotReciprocal,

    /// <summary>A link names a map number the world has no room for.</summary>
    LinkOutOfRange,

    /// <summary>A warp names a map number the world has no room for.</summary>
    WarpMapMissing,

    /// <summary>A warp names a tile outside its destination map. The server refuses this warp when a player
    /// steps on it, so the tile is authored but unusable.</summary>
    WarpTileOutside,

    /// <summary>A boot point names a map number the world has no room for.</summary>
    BootMapMissing,

    /// <summary>A boot point names a tile outside the map it sends players to.</summary>
    BootTileOutside,

    /// <summary>A map names a group that has no record.</summary>
    MapGroupMissing,

    /// <summary>A fixed NPC spawn pin sits outside the map that holds it.</summary>
    SpawnPinOutside,

    /// <summary>A placed light sits outside the map that holds it.</summary>
    LightOutside,

    // ── References between records ───────────────────────────────────────────
    // "Missing" means the same thing throughout: the number is outside the world's range, or it names a slot
    // nobody has authored. Both leave the reference pointing at nothing.

    /// <summary>Something names an NPC that is not there.</summary>
    NpcMissing,

    /// <summary>Something names an item that is not there.</summary>
    ItemMissing,

    /// <summary>Something names a spell that is not there.</summary>
    SpellMissing,

    /// <summary>Something names a quest that is not there.</summary>
    QuestMissing,

    /// <summary>Something restricts itself to a class that is not there.</summary>
    ClassMissing,

    /// <summary>A conversation names a node it does not contain.</summary>
    ConversationNodeMissing,

    // ── Reachability ─────────────────────────────────────────────────────────
    // A reference can resolve and the content still be unreachable, which is worth as much as a broken
    // number: the record exists and no player can ever get to it.

    /// <summary>A shop names no keeper, so nothing in the world opens it.</summary>
    ShopHasNoKeeper,

    /// <summary>A conversation offers to open a shop, but its speaker keeps none.</summary>
    ConversationOpensNoShop,

    /// <summary>A conversation offers to open a quest menu, but its speaker neither gives nor takes one.</summary>
    ConversationOpensNoQuests,

    /// <summary>A quest's prerequisite chain loops, so no player can ever accept it.</summary>
    QuestPrereqCycle,
}

/// <summary>One finding. <see cref="OwnerKind"/> and <see cref="OwnerNum"/> name the record it was found on;
/// <see cref="X"/>/<see cref="Y"/> the tile where the finding is tile-scoped, and -1 otherwise.
/// <see cref="Detail"/> carries the specifics a message needs, already formatted.</summary>
public readonly record struct WorldIssue(
    WorldIssueKind Kind, WorldRecordKind OwnerKind, int OwnerNum, int X, int Y, string Detail)
{
    /// <summary>A map-scoped finding, which is most of them.</summary>
    public static WorldIssue OnMap(WorldIssueKind kind, int mapNum, string detail) =>
        new(kind, WorldRecordKind.Map, mapNum, -1, -1, detail);

    /// <summary>A finding about one tile of one map.</summary>
    public static WorldIssue OnTile(WorldIssueKind kind, int mapNum, int x, int y, string detail) =>
        new(kind, WorldRecordKind.Map, mapNum, x, y, detail);

    /// <summary>True when this points at one tile rather than at the whole record.</summary>
    public bool HasTile => X >= 0 && Y >= 0;
}

/// <summary>Every record family a world holds, 1-based with index 0 unused, plus the one family that lives
/// in a dictionary rather than an array.</summary>
public sealed record WorldContent
{
    public MapRecord?[] Maps { get; init; } = [];
    public ItemRecord?[] Items { get; init; } = [];
    public NpcRecord?[] Npcs { get; init; } = [];
    public ShopRecord?[] Shops { get; init; } = [];
    public SpellRecord?[] Spells { get; init; } = [];
    public QuestRecord?[] Quests { get; init; } = [];
    public ConversationRecord?[] Conversations { get; init; } = [];
    public ClassRecord?[] Classes { get; init; } = [];

    /// <summary>Whether a map-group index is backed by a record. Groups live in a dictionary — only files
    /// that exist are loaded — so they cannot be range-checked like the rest.</summary>
    public Func<int, bool> GroupExists { get; init; } = _ => true;
}

/// <summary>
/// Reads a world and reports what does not hold together.
///
/// <para>Every check here is about the RECORDS agreeing with each other — a link naming a map of another
/// size, a warp naming a tile that map does not have, a shop nobody keeps. None of it is game logic, so it
/// needs no server: give it the arrays and it answers.</para>
///
/// <para>Numbers, not names: an issue carries the record, the tile and the values involved, and whoever shows
/// it decides how to word that.</para>
/// </summary>
public static class WorldCheck
{
    /// <summary>Sweeps a whole world.</summary>
    public static List<WorldIssue> Run(WorldContent world)
    {
        var found = new List<WorldIssue>();
        // Answered once per map rather than once per reference: it reads every tile, and a world of warps
        // would otherwise walk the destination map again for each one.
        var authored = new bool[world.Maps.Length];
        for (int i = 1; i < world.Maps.Length; i++) authored[i] = world.Maps[i] is { IsBlank: false };

        CheckMaps(found, world, authored);
        CheckNpcs(found, world);
        CheckItems(found, world);
        CheckSpells(found, world);
        CheckShops(found, world);
        CheckQuests(found, world);
        CheckConversations(found, world);
        CheckClasses(found, world);
        return found;
    }

    // ── "Is this there?" ─────────────────────────────────────────────────────
    // A slot inside the range but never authored is as broken a target as one outside it, and far more
    // common: renumbering content leaves references pointing at blanks. A record counts as authored when it
    // has a name, which is the same test the editor's own lists use to label a slot "(empty)".
    //
    // A map answers it differently — see HasMap — because a place can be painted and never titled.

    private static bool Has<T>(T?[] all, int num, Func<T, string> nameOf) where T : class =>
        num >= 1 && num < all.Length && all[num] is { } r && !string.IsNullOrWhiteSpace(nameOf(r));

    // A map proves it was authored by holding something rather than by being named: a place can be fully
    // painted and never titled, and a name is the only thing a padded slot and a finished map both lack.
    private static bool HasMap(bool[] authored, int num) => num >= 1 && num < authored.Length && authored[num];

    private static bool HasNpc(WorldContent w, int n) => Has(w.Npcs, n, r => r.Name);
    private static bool HasItem(WorldContent w, int n) => Has(w.Items, n, r => r.Name);
    private static bool HasSpell(WorldContent w, int n) => Has(w.Spells, n, r => r.Name);
    private static bool HasQuest(WorldContent w, int n) => Has(w.Quests, n, r => r.Name);
    private static bool HasClass(WorldContent w, int n) => Has(w.Classes, n, r => r.Name);

    // Every family iterates the same way: 1-based, skipping slots nobody has authored.
    private static IEnumerable<(int Num, T Record)> Authored<T>(T?[] all, Func<T, string> nameOf) where T : class
    {
        for (int i = 1; i < all.Length; i++)
        {
            if (all[i] is { } r && !string.IsNullOrWhiteSpace(nameOf(r))) yield return (i, r);
        }
    }

    private static void Ref(List<WorldIssue> found, bool present, WorldIssueKind kind,
                            WorldRecordKind owner, int ownerNum, string detail)
    {
        if (!present) found.Add(new WorldIssue(kind, owner, ownerNum, -1, -1, detail));
    }

    private static void Classes(List<WorldIssue> found, WorldContent w, List<short>? gate,
                                WorldRecordKind owner, int ownerNum)
    {
        if (gate is null) return;
        foreach (short c in gate)
            Ref(found, HasClass(w, c), WorldIssueKind.ClassMissing, owner, ownerNum, $"{c}");
    }

    // ── Maps ─────────────────────────────────────────────────────────────────

    private static void CheckMaps(List<WorldIssue> found, WorldContent w, bool[] authored)
    {
        var maps = w.Maps;
        for (int m = 1; m < maps.Length; m++)
        {
            var map = maps[m];
            // A padded slot holds nothing to disagree with anything, so the tile walk below is skipped for
            // the hundreds of them a world is padded out to.
            if (map is null || !authored[m]) continue;

            CheckLinks(found, maps, m, map, authored);
            CheckBootPoint(found, maps, m, map, authored);

            if (map.MapGroup != 0 && !w.GroupExists(map.MapGroup))
                found.Add(WorldIssue.OnMap(WorldIssueKind.MapGroupMissing, m, $"{map.MapGroup}"));

            foreach (var e in map.Npcs)
            {
                if (e.Npc != 0)
                    Ref(found, HasNpc(w, e.Npc), WorldIssueKind.NpcMissing, WorldRecordKind.Map, m, $"{e.Npc}");

                if (e.HasPin && !map.Contains(e.PinX!.Value, e.PinY!.Value))
                {
                    found.Add(WorldIssue.OnMap(WorldIssueKind.SpawnPinOutside, m,
                        $"{e.Npc} at ({e.PinX},{e.PinY}) of {map.Width}x{map.Height}"));
                }
            }

            foreach (var l in map.Lights)
            {
                if (!map.Contains(l.X, l.Y))
                {
                    found.Add(WorldIssue.OnTile(WorldIssueKind.LightOutside, m, l.X, l.Y,
                        $"({l.X},{l.Y}) of {map.Width}x{map.Height}"));
                }
            }

            CheckTiles(found, w, m, map, authored);
        }
    }

    private static readonly (Func<MapRecord, int> Get, Func<MapRecord, int> Back, string Name)[] Edges =
    [
        (m => m.Up,    m => m.Down,  "Up"),
        (m => m.Down,  m => m.Up,    "Down"),
        (m => m.Left,  m => m.Right, "Left"),
        (m => m.Right, m => m.Left,  "Right"),
    ];

    private static void CheckLinks(List<WorldIssue> found, MapRecord?[] maps, int m, MapRecord map, bool[] authored)
    {
        foreach (var (get, back, name) in Edges)
        {
            int target = get(map);
            if (target == 0) continue;

            if (!HasMap(authored, target))
            {
                found.Add(WorldIssue.OnMap(WorldIssueKind.LinkOutOfRange, m, $"{name} -> {target}"));
                continue;
            }

            var other = maps[target]!;
            if (other.Width != map.Width || other.Height != map.Height)
            {
                found.Add(WorldIssue.OnMap(WorldIssueKind.LinkSizeMismatch, m,
                    $"{name} -> {target} ({other.Width}x{other.Height} vs {map.Width}x{map.Height})"));
            }

            // Reported from the low-numbered side only, so one broken seam is one finding rather than two.
            if (back(other) != m && m < target)
                found.Add(WorldIssue.OnMap(WorldIssueKind.LinkNotReciprocal, m, $"{name} -> {target}"));
        }
    }

    private static void CheckBootPoint(List<WorldIssue> found, MapRecord?[] maps, int m, MapRecord map, bool[] authored)
    {
        if (map.BootMap == 0) return;

        if (!HasMap(authored, map.BootMap))
        {
            found.Add(WorldIssue.OnMap(WorldIssueKind.BootMapMissing, m, $"{map.BootMap}"));
            return;
        }

        var dest = maps[map.BootMap]!;
        if (!dest.Contains(map.BootX, map.BootY))
        {
            found.Add(WorldIssue.OnMap(WorldIssueKind.BootTileOutside, m,
                $"{map.BootMap} ({map.BootX},{map.BootY}) of {dest.Width}x{dest.Height}"));
        }
    }

    private static void CheckTiles(List<WorldIssue> found, WorldContent w, int m, MapRecord map, bool[] authored)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var tile = map.Tile[x, y];
                CheckAttr(found, w, m, x, y, LayerLogic.AttrFor(tile, WorldLayer.Ground), authored);
                CheckAttr(found, w, m, x, y, LayerLogic.AttrFor(tile, WorldLayer.Fringe), authored);
            }
        }
    }

    private static void CheckAttr(List<WorldIssue> found, WorldContent w, int m, int x, int y, TileAttr attr,
                                  bool[] authored)
    {
        switch (attr.Type)
        {
            case TileType.Warp:
                if (!HasMap(authored, attr.WarpMap))
                {
                    found.Add(WorldIssue.OnTile(WorldIssueKind.WarpMapMissing, m, x, y, $"{attr.WarpMap}"));
                    break;
                }

                var dest = w.Maps[attr.WarpMap]!;
                if (!dest.Contains(attr.WarpX, attr.WarpY))
                {
                    found.Add(WorldIssue.OnTile(WorldIssueKind.WarpTileOutside, m, x, y,
                        $"{attr.WarpMap} ({attr.WarpX},{attr.WarpY}) of {dest.Width}x{dest.Height}"));
                }

                break;

            case TileType.Item:
                if (!HasItem(w, attr.ItemNum))
                    found.Add(WorldIssue.OnTile(WorldIssueKind.ItemMissing, m, x, y, $"{attr.ItemNum}"));
                break;

            case TileType.Key:
            case TileType.KeyOpen:
                if (!HasItem(w, attr.KeyItemNum))
                    found.Add(WorldIssue.OnTile(WorldIssueKind.ItemMissing, m, x, y, $"{attr.KeyItemNum}"));
                break;
        }
    }

    // ── The other families ───────────────────────────────────────────────────

    private static void CheckNpcs(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, npc) in Authored(w.Npcs, r => r.Name))
        {
            foreach (var d in npc.Drops ?? [])
                Ref(found, HasItem(w, d.ItemNum), WorldIssueKind.ItemMissing, WorldRecordKind.Npc, num, $"{d.ItemNum}");
        }
    }

    private static void CheckItems(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, item) in Authored(w.Items, r => r.Name))
        {
            // SpellNum only means a spell on a scroll; on every other type the field carries nothing.
            if (item.Type == ItemType.Spell)
                Ref(found, HasSpell(w, item.SpellNum), WorldIssueKind.SpellMissing, WorldRecordKind.Item, num, $"{item.SpellNum}");

            Classes(found, w, item.AllowedClasses, WorldRecordKind.Item, num);
        }
    }

    private static void CheckSpells(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, spell) in Authored(w.Spells, r => r.Name))
        {
            // A GiveItem spell hands over an item; the other types put a magnitude in this field.
            if (spell.Type == SpellType.GiveItem)
                Ref(found, HasItem(w, spell.ItemNum), WorldIssueKind.ItemMissing, WorldRecordKind.Spell, num, $"{spell.ItemNum}");

            Classes(found, w, spell.AllowedClasses, WorldRecordKind.Spell, num);
        }
    }

    private static void CheckShops(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, shop) in Authored(w.Shops, r => r.Name))
        {
            if (shop.Keeper == 0)
                found.Add(new WorldIssue(WorldIssueKind.ShopHasNoKeeper, WorldRecordKind.Shop, num, -1, -1, ""));
            else
                Ref(found, HasNpc(w, shop.Keeper), WorldIssueKind.NpcMissing, WorldRecordKind.Shop, num, $"{shop.Keeper}");

            foreach (int i in shop.SalesItem)
                Ref(found, HasItem(w, i), WorldIssueKind.ItemMissing, WorldRecordKind.Shop, num, $"{i}");

            foreach (var b in shop.BarterItem)
            {
                Ref(found, HasItem(w, b.GiveItem), WorldIssueKind.ItemMissing, WorldRecordKind.Shop, num, $"{b.GiveItem}");
                Ref(found, HasItem(w, b.GetItem), WorldIssueKind.ItemMissing, WorldRecordKind.Shop, num, $"{b.GetItem}");
            }
        }
    }

    private static void CheckQuests(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, quest) in Authored(w.Quests, r => r.Name))
        {
            if (quest.GiverNpc != 0)
                Ref(found, HasNpc(w, quest.GiverNpc), WorldIssueKind.NpcMissing, WorldRecordKind.Quest, num, $"{quest.GiverNpc}");
            if (quest.TurnInNpc != 0)
                Ref(found, HasNpc(w, quest.TurnInNpc), WorldIssueKind.NpcMissing, WorldRecordKind.Quest, num, $"{quest.TurnInNpc}");
            if (quest.PrereqQuest != 0)
                Ref(found, HasQuest(w, quest.PrereqQuest), WorldIssueKind.QuestMissing, WorldRecordKind.Quest, num, $"{quest.PrereqQuest}");

            foreach (var o in quest.Objectives)
            {
                // Target 0 is a wildcard — "any target of this kind" — and names nothing to check.
                if (o.Target == 0) continue;
                switch (o.Kind)
                {
                    case ObjectiveKind.Kill:
                        Ref(found, HasNpc(w, o.Target), WorldIssueKind.NpcMissing, WorldRecordKind.Quest, num, $"{o.Target}");
                        break;
                    case ObjectiveKind.Fetch:
                    case ObjectiveKind.Gather:
                        Ref(found, HasItem(w, o.Target), WorldIssueKind.ItemMissing, WorldRecordKind.Quest, num, $"{o.Target}");
                        break;
                    case ObjectiveKind.Explore:
                        Ref(found, w.Maps.ElementAtOrDefault(o.Target) is { IsBlank: false },
                            WorldIssueKind.WarpMapMissing, WorldRecordKind.Quest, num, $"{o.Target}");
                        break;
                }
            }

            foreach (var r in quest.RewardItems.Concat(quest.RepeatRewardItems))
                Ref(found, HasItem(w, r.ItemNum), WorldIssueKind.ItemMissing, WorldRecordKind.Quest, num, $"{r.ItemNum}");

            Classes(found, w, quest.AllowedClasses, WorldRecordKind.Quest, num);

            if (PrereqLoops(w, num))
                found.Add(new WorldIssue(WorldIssueKind.QuestPrereqCycle, WorldRecordKind.Quest, num, -1, -1, ""));
        }
    }

    // Walks the prerequisite chain from one quest. A quest that requires itself, directly or through any
    // number of steps, can never be accepted by anyone.
    private static bool PrereqLoops(WorldContent w, int start)
    {
        var seen = new HashSet<int> { start };
        int at = start;
        while (true)
        {
            var q = at >= 1 && at < w.Quests.Length ? w.Quests[at] : null;
            int next = q?.PrereqQuest ?? 0;
            if (next == 0) return false;
            if (!seen.Add(next)) return true;
            at = next;
        }
    }

    private static void CheckConversations(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, conv) in Authored(w.Conversations, r => r.Name))
        {
            if (conv.SpeakerNpc != 0)
            {
                Ref(found, HasNpc(w, conv.SpeakerNpc), WorldIssueKind.NpcMissing,
                    WorldRecordKind.Conversation, num, $"{conv.SpeakerNpc}");
            }

            var nodeIds = conv.Nodes.Select(n => n.Id).ToHashSet();
            if (conv.RootNodeId != 0 && !nodeIds.Contains(conv.RootNodeId))
            {
                found.Add(new WorldIssue(WorldIssueKind.ConversationNodeMissing, WorldRecordKind.Conversation,
                    num, -1, -1, $"{conv.RootNodeId}"));
            }

            bool opensShop = false, opensQuests = false;
            foreach (var node in conv.Nodes)
            {
                foreach (var choice in node.Choices)
                {
                    // NextNodeId 0 ends the conversation, which is an ending rather than a dangling link.
                    if (choice.NextNodeId != 0 && !nodeIds.Contains(choice.NextNodeId))
                    {
                        found.Add(new WorldIssue(WorldIssueKind.ConversationNodeMissing,
                            WorldRecordKind.Conversation, num, -1, -1, $"{choice.NextNodeId}"));
                    }

                    opensShop |= choice.Action == ConversationAction.OpenShop;
                    opensQuests |= choice.Action == ConversationAction.OpenQuests;
                }
            }

            // Both actions hand off to a role the SPEAKER holds, so a speaker without that role leaves the
            // choice doing nothing when a player picks it.
            if (opensShop && !Authored(w.Shops, r => r.Name).Any(s => s.Record.Keeper == conv.SpeakerNpc))
            {
                found.Add(new WorldIssue(WorldIssueKind.ConversationOpensNoShop, WorldRecordKind.Conversation,
                    num, -1, -1, $"{conv.SpeakerNpc}"));
            }

            if (opensQuests && !Authored(w.Quests, r => r.Name)
                    .Any(q => q.Record.GiverNpc == conv.SpeakerNpc || q.Record.EffectiveTurnInNpc == conv.SpeakerNpc))
            {
                found.Add(new WorldIssue(WorldIssueKind.ConversationOpensNoQuests, WorldRecordKind.Conversation,
                    num, -1, -1, $"{conv.SpeakerNpc}"));
            }
        }
    }

    private static void CheckClasses(List<WorldIssue> found, WorldContent w)
    {
        foreach (var (num, cls) in Authored(w.Classes, r => r.Name))
        {
            foreach (var s in cls.StartingItems ?? [])
                Ref(found, HasItem(w, s.ItemNum), WorldIssueKind.ItemMissing, WorldRecordKind.Class, num, $"{s.ItemNum}");

            foreach (int s in cls.StartingSpells ?? [])
                Ref(found, HasSpell(w, s), WorldIssueKind.SpellMissing, WorldRecordKind.Class, num, $"{s}");
        }
    }
}
