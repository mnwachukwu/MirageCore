using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.World;

/// <summary>The shipped seed in <c>server/src/Mirage.Server.Host/world</c>, checked against the engine that
/// has to load it.
///
/// <para><b>Why this fixture exists.</b> Every other economy test here is formula-level, so anything a
/// GENERATOR authors was unpinned by construction — and the generators live outside the repo entirely, in
/// <c>.Tools</c>, where no test can reach them. That gap has already produced real bugs of a kind the
/// compiler cannot see: <c>gen-npcs.mjs</c> hand-writes JSON and kept emitting a field the C# side had
/// renamed, which would have made every gold drop read as zero; and <c>gen-items --apply</c> rewrites all
/// 558 item files WITHOUT prices, so regenerating the armory for any reason silently zeroes every price
/// unless <c>seed-prices</c> is run after it. Neither errors. Both are caught below.</para>
///
/// <para><b>This is a content guard, not a unit test, and it is marked <c>[Category("Content")]</c> to
/// keep it out of every unit run.</b> It asserts nothing about code: it reads the shipped seed and holds
/// it to the engine's rules. The seed is tracked — 1,172 files present in every checkout — so an empty
/// read is a failure here, not a skip. Unit tests use their own fixtures; nothing else in the suites
/// reads authored content.</para></summary>
[TestFixture]
[Category("Content")]
public class SeedIntegrityTests
{
    /// <summary>Must MIRROR <c>JsonPersistenceService.Options</c>. The point of this fixture is to read the
    /// seed the way the engine reads it, so a divergence here can pass a file the server would reject — or,
    /// as happened, reject a file the server reads fine. The converter is the part that bites: the server
    /// writes enums as STRINGS (<c>"action": "OpenShop"</c>), and without it every conversation in the seed
    /// failed to deserialize while the running game loaded them without complaint.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static Dictionary<int, ItemRecord> _items = new();
    private static Dictionary<int, NpcRecord> _npcs = new();
    private static Dictionary<int, ConversationRecord> _conversations = new();
    private static Dictionary<int, ShopRecord> _shops = new();
    private static WorldManifest _manifest = new();

    [OneTimeSetUp]
    public void LoadSeed()
    {
        string? data = FindDataDir();
        if (data is null) return;
        _items = LoadAll<ItemRecord>(data, "items", "item");
        _npcs = LoadAll<NpcRecord>(data, "npcs", "npc");
        _conversations = LoadAll<ConversationRecord>(data, "conversations", "conversation");
        _shops = LoadAll<ShopRecord>(data, "shops", "shop");

        string manifest = Path.Combine(data, "world.json");
        if (File.Exists(manifest))
            _manifest = JsonSerializer.Deserialize<WorldManifest>(File.ReadAllText(manifest), Json) ?? new();
    }

    // Walk up from the test binary to the repo root (marked by the solution file), rather than counting
    // "..\..\..\.." — the bin depth changes with configuration and target framework.
    private static string? FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mirage.slnx")))
            dir = dir.Parent;
        if (dir is null) return null;
        string data = Path.Combine(dir.FullName, "server", "src", "Mirage.Server.Host", "world");
        return Directory.Exists(data) ? data : null;
    }

    /// <summary>Keyed by the NUMBER in the filename, never by enumeration order: a directory listing sorts
    /// "item1, item10, item100, item2", so an index into a flat list returns the WRONG record rather than
    /// none — a failure mode that reads as bad data instead of a bad lookup.</summary>
    private static Dictionary<int, T> LoadAll<T>(string data, string folder, string prefix) where T : class
    {
        var result = new Dictionary<int, T>();
        string dir = Path.Combine(data, folder);
        if (!Directory.Exists(dir)) return result;
        foreach (string path in Directory.GetFiles(dir, prefix + "*.json"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(path).AsSpan(prefix.Length), out int num)) continue;
            var rec = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
            if (rec is not null) result[num] = rec;
        }
        return result;
    }

    private static void RequireSeed()
    {
        Assert.That(_items, Is.Not.Empty,
            "No seed loaded from server/src/Mirage.Server.Host/world. It is tracked, so an empty read "
            + "means the folder was emptied or the loader stopped matching its filenames — either way "
            + "this guard has nothing left to check and says so rather than passing.");
    }

    // ── The seed is canonical on disk ─────────────────────────────────────────

    [Test]
    public void EveryRecord_IsAlreadyNormalized()
    {
        RequireSeed();
        // The server runs Normalize on load and the editor on save, and it CLEARS fields that do
        // not apply to a record's type. If running it changes a seed file, the file on disk is carrying
        // values the engine will silently discard — a generator writing Power onto a potion, say. The
        // seed should already be the canonical form of itself.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items)
            {
                var before = JsonSerializer.Serialize(item, Json);
                item.Normalize();
                Assert.That(JsonSerializer.Serialize(item, Json), Is.EqualTo(before),
                    $"item{num} ({item.Name}) is not canonical — Normalize changed it");
            }
            foreach (var (num, npc) in _npcs)
            {
                var before = JsonSerializer.Serialize(npc, Json);
                npc.Normalize();
                Assert.That(JsonSerializer.Serialize(npc, Json), Is.EqualTo(before),
                    $"npc{num} ({npc.Name}) is not canonical — Normalize changed it");
            }
        });
    }

    // ── Referential integrity ─────────────────────────────────────────────────

    [Test]
    public void EveryDropLine_NamesAnItemThatExists()
    {
        RequireSeed();
        // A drop naming a missing item is INERT, not loud: the roller skips it. So a drop table that lost
        // its footing during a renumber pays out nothing and reports nothing. The armory has been
        // renumbered twice in two days (potions 3 → 15 tiers, then treasure), and both times this is the
        // check that would have caught a stale table.
        Assert.Multiple(() =>
        {
            foreach (var (num, npc) in _npcs)
                foreach (var d in npc.Drops ?? [])
                    Assert.That(_items.ContainsKey(d.ItemNum), Is.True,
                        $"npc{num} ({npc.Name}) drops item {d.ItemNum}, which does not exist");
        });
    }

    /// <summary>The world's starting loadout names items that exist. An authored line pointing at a blank
    /// slot is skipped in silence at creation, so the only symptom is a character arriving with less than
    /// the world meant to give it.</summary>
    [Test]
    public void EveryStartingLoadout_NamesThingsThatExist()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var start in _manifest.StartingItems)
                Assert.That(_items.ContainsKey(start.ItemNum), Is.True,
                    $"the world starts characters with item {start.ItemNum}, which does not exist");
        });
    }


    // ── What the generators own, and nothing else guards ──────────────────────

    [Test]
    public void EveryPriceableItem_CarriesItsSeededPrice()
    {
        RequireSeed();
        // THE TRAP THIS EXISTS FOR: gen-items --apply rewrites every item file and does NOT write price;
        // that is seed-prices' stage. Regenerating the armory without re-running it leaves 558 items at
        // price 0 — a world of free gear, with nothing raising a hand. Core derives no price, so an
        // unpriced piece of gear is worth nothing to buy, sell or mend and nothing else will notice.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items)
            {
                if (!ItemRecord.IsEquipment(item.Type) && !ItemRecord.IsConsumable(item.Type)) continue;
                Assert.That(item.Price, Is.GreaterThan(0),
                    $"item{num} ({item.Name}) is unpriced — run seed-prices.cs after any gen-items --apply");
            }
        });
    }

    [Test]
    public void Treasure_IsPricedAndProtected()
    {
        RequireSeed();
        // Treasure is typed None and sold through a fence rather than a vendor, so what protects it is
        // the NonJunkable flag rather than its type.
        var treasure = _items.Where(kv => kv.Value.Type == ItemType.None && kv.Value.Price > 0).ToArray();
        Assert.That(treasure, Is.Not.Empty, "the seed authors no treasure");

        Assert.Multiple(() =>
        {
            foreach (var (num, t) in treasure)
            {
                Assert.That(t.NonJunkable, Is.True,
                    $"item{num} ({t.Name}) is treasure but junkable — the generic vendor would buy it and "
                    + "the fence would be pointless");
                Assert.That(t.Name, Is.Not.Empty, $"item{num} is priced treasure with no name");
            }
            Assert.That(_items.Values.Any(i => i.Type == ItemType.Currency && i.NonJunkable), Is.True,
                "gold must be NonJunkable — dumping currency for a fraction of itself is nonsense");
        });
    }

    [Test]
    public void GoldDrops_CarryARealQuantity()
    {
        RequireSeed();
        // The bug this is written for actually happened. gen-npcs.mjs hand-writes its JSON, so when the C#
        // field was renamed Value → Quantity the generator kept emitting the old key; every gold line
        // would have deserialized to quantity 0 (clamped to 1 at roll time) and the entire gold economy
        // would have vanished with no error anywhere. A JS generator is outside the compiler's reach, and
        // this is the only thing standing where the compiler would otherwise be.
        var goldLines = _npcs.SelectMany(kv => (kv.Value.Drops ?? []).Select(d => (Npc: kv.Value, Drop: d)))
                             .Where(x => x.Drop.ItemNum == Constants.GoldItemIndex)
                             .ToArray();
        Assert.That(goldLines, Is.Not.Empty, "no NPC drops gold — a renamed field reads exactly like this");

        Assert.Multiple(() =>
        {
            foreach (var (npc, drop) in goldLines)
                Assert.That(drop.Quantity, Is.GreaterThan(0),
                    $"{npc.Name} drops gold with no quantity — check that gen-npcs.mjs still emits "
                    + "'quantity', which the compiler cannot verify for a JS generator");
        });
    }

    // ── Conversations ─────────────────────────────────────────────────────────
    // gen-conversations.cs runs these same structural checks before it writes. They are repeated here
    // because the generator only validates its own INTENT — a tree edited afterwards in the editor, or
    // by hand, reaches the world without passing through it. This fixture checks what is on DISK.

    private static void RequireConversations()
    {
        RequireSeed();
        Assert.That(_conversations, Is.Not.Empty, "the seed authors no conversations");
    }

    /// <summary>A conversation names its NPC by number, and an unresolvable number is not an error
    /// anywhere in the engine — <c>GameWorld.ConversationForNpc</c> simply finds nothing and the NPC
    /// says its Says instead. Authored dialogue that can never open is therefore silent, and this
    /// catches it.
    ///
    /// <para>One thing has to hold: it names an NPC that exists. WHICH one is free — where a world puts
    /// its talkers is the world's business, not the engine's.</para></summary>
    [Test]
    public void EveryConversation_SpeaksForAnNpcThatExists()
    {
        RequireConversations();
        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.OrderBy(kv => kv.Key))
            {
                Assert.That(_npcs.ContainsKey(conv.SpeakerNpc), Is.True,
                    $"conversation{num} ({conv.TrimmedName}) speaks for npc {conv.SpeakerNpc}, which does not exist");
            }
        });
    }

    /// <summary>Two conversations claiming the same NPC is not an error anywhere in the engine —
    /// <c>GameWorld.ConversationForNpc</c> takes the FIRST non-empty match, so the second one simply never
    /// opens. Authored dialogue that silently never appears is exactly what this fixture is for.</summary>
    [Test]
    public void NoTwoConversations_ClaimTheSameNpc()
    {
        RequireConversations();
        var dupes = _conversations.Where(kv => kv.Value.TrimmedName.Length > 0)
                                  .GroupBy(kv => kv.Value.SpeakerNpc)
                                  .Where(g => g.Count() > 1);
        Assert.That(dupes.Select(g => $"npc {g.Key} claimed by conversations {string.Join(", ", g.Select(kv => kv.Key))}"),
            Is.Empty, "only the lowest-numbered conversation for an NPC is ever reachable");
    }

    /// <summary>Every branch must land somewhere real. An unresolvable NextNodeId does not throw — it ends
    /// the conversation, exactly as the 0 sentinel does — so a typo reads in-game as an NPC who abruptly
    /// stops talking.</summary>
    [Test]
    public void EveryConversationChoice_ResolvesOrDeliberatelyEnds()
    {
        RequireConversations();
        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.OrderBy(kv => kv.Key))
            {
                if (conv.TrimmedName.Length == 0) continue;
                var ids = conv.Nodes.Select(n => n.Id).ToHashSet();
                Assert.That(conv.RootNode, Is.Not.Null, $"conversation{num} has no reachable root node");

                foreach (var node in conv.Nodes)
                {
                    Assert.That(node.Choices, Is.Not.Empty,
                        $"conversation{num} node {node.Id} offers no choices — the player is trapped in it");

                    foreach (var ch in node.Choices)
                        if (ch.Action == ConversationAction.None && ch.NextNodeId != 0)
                            Assert.That(ids, Does.Contain(ch.NextNodeId),
                                $"conversation{num} node {node.Id} choice \"{ch.Label}\" points at "
                                + $"node {ch.NextNodeId}, which does not exist");

                    // An exit must be REACHABLE, not immediate. A node whose branches all continue one
                    // more step is fine authoring (a joke with a forced punchline is exactly that);
                    // what is unacceptable is a cycle with no exit anywhere in it.
                    var walked = new HashSet<int> { node.Id };
                    var pending = new Queue<ConversationNode>([node]);
                    bool escapes = false;
                    while (pending.Count > 0 && !escapes)
                    {
                        var at = pending.Dequeue();
                        if (at.Choices.Any(ch => ch.Action != ConversationAction.None || ch.NextNodeId == 0))
                        {
                            escapes = true;
                            break;
                        }
                        foreach (var ch in at.Choices)
                            if (ch.NextNodeId != 0 && walked.Add(ch.NextNodeId)
                                && conv.NodeById(ch.NextNodeId) is { } next)
                                pending.Enqueue(next);
                    }
                    Assert.That(escapes, Is.True,
                        $"conversation{num} node {node.Id} can never reach an exit — the player is stuck");
                }
            }
        });
    }

    /// <summary>Authored text nobody can reach is the failure mode a word count hides: the tree looks full,
    /// and a whole branch is orphaned because the choice that pointed at it was retargeted.</summary>
    [Test]
    public void NoConversationNode_IsUnreachableFromItsRoot()
    {
        RequireConversations();
        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.OrderBy(kv => kv.Key))
            {
                if (conv.TrimmedName.Length == 0 || conv.RootNode is null) continue;

                var seen = new HashSet<int> { conv.RootNode.Id };
                var queue = new Queue<int>([conv.RootNode.Id]);
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    var node = conv.NodeById(id);
                    if (node is null) continue;
                    foreach (var ch in node.Choices)
                        if (ch.Action == ConversationAction.None && ch.NextNodeId != 0
                            && conv.NodeById(ch.NextNodeId) is not null && seen.Add(ch.NextNodeId))
                            queue.Enqueue(ch.NextNodeId);
                }

                foreach (var node in conv.Nodes)
                    Assert.That(seen, Does.Contain(node.Id),
                        $"conversation{num} ({conv.TrimmedName}) node {node.Id} is unreachable from the root");
            }
        });
    }

    /// <summary>The editor enforces both caps on the way in; nothing enforces them on a file written by a
    /// generator or edited by hand, and the choice cap is a real render limit rather than a round
    /// number.</summary>
    [Test]
    public void EveryConversation_StaysWithinTheEngineCaps()
    {
        RequireConversations();
        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.OrderBy(kv => kv.Key))
            {
                Assert.That(conv.Nodes, Has.Count.LessThanOrEqualTo(Constants.MaxConversationNodes),
                    $"conversation{num} exceeds MaxConversationNodes");
                foreach (var node in conv.Nodes)
                    Assert.That(node.Choices, Has.Count.LessThanOrEqualTo(Constants.MaxConversationChoices),
                        $"conversation{num} node {node.Id} exceeds MaxConversationChoices — the panel "
                        + "renders a menu, and the editor caps this for a reason");
            }
        });
    }

    // ── The content chain closes ──────────────────────────────────────────────

    /// <summary>The last link. Conversations reserve npc numbers 125+, and shops are authored against
    /// them, so a generator that has not run leaves every one of those references pointing at nothing.
    /// None of it errors at runtime — an unresolvable SpeakerNpc means "no conversation", a missing
    /// keeper means "no shop" — which is exactly why it is checked here.</summary>
    [Test]
    public void EveryAuthoredReference_NamesAnNpcThatExists()
    {
        RequireSeed();
        Assert.That(_npcs, Is.Not.Empty, "the seed authors no NPCs");

        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.Where(kv => kv.Value.TrimmedName.Length > 0))
                Assert.That(_npcs, Does.ContainKey(conv.SpeakerNpc),
                    $"conversation{num} ({conv.TrimmedName}) speaks for npc {conv.SpeakerNpc}, which does not exist");

            foreach (var (num, shop) in _shops.Where(kv => kv.Value.Keeper > 0))
                Assert.That(_npcs, Does.ContainKey(shop.Keeper),
                    $"shop{num} ({shop.TrimmedName}) is kept by npc {shop.Keeper}, which does not exist");
        });
    }

    /// <summary>Anyone who carries content must be non-hostile and must not be loot. A shopkeeper on
    /// A behavior that notices would walk off after the customer; a drop table turns a storefront into a farm.</summary>
    [Test]
    public void NoContentCarrier_IsAlsoLoot()
    {
        RequireSeed();
        var carriers = _conversations.Values.Where(c => c.TrimmedName.Length > 0).Select(c => c.SpeakerNpc)
            .Concat(_shops.Values.Where(s => s.Keeper > 0).Select(s => s.Keeper))
            .Distinct().Where(_npcs.ContainsKey).ToArray();
        Assert.That(carriers, Is.Not.Empty, "no NPC carries content — the roster lost its townsfolk");

        Assert.Multiple(() =>
        {
            foreach (int num in carriers)
            {
                var npc = _npcs[num];
                Assert.That(npc.Drops ?? [], Is.Empty,
                    $"npc {num} ({npc.TrimmedName}) carries content AND a drop table — killing the shopkeeper pays");
            }
        });
    }

    // ── Shops ─────────────────────────────────────────────────────────────────

    private static void RequireShops()
    {
        RequireSeed();
        Assert.That(_shops, Is.Not.Empty, "the seed authors no shops");
    }

    /// <summary>Interaction is TALK-FIRST (<c>PacketHandler.HandleNpcInteract</c>):
    /// conversation, then a visible quest, then the keeper shop. So a keeper who HAS a conversation is
    /// only ever reached through it, and a shop whose keeper's tree carries no <c>OpenShop</c> choice can
    /// never be opened by any player — the NPC just talks. Nothing in the engine reports this.</summary>
    [Test]
    public void EveryShopKeeper_CanActuallyBeAskedToOpenIt()
    {
        RequireShops();
        Assert.That(_conversations, Is.Not.Empty, "the seed authors no conversations to check against");

        Assert.Multiple(() =>
        {
            foreach (var (num, shop) in _shops.OrderBy(kv => kv.Key))
            {
                if (shop.Keeper == 0) continue;   // an unassigned shop is unreachable by design, not by accident

                var conv = _conversations.Values.FirstOrDefault(c =>
                    c.TrimmedName.Length > 0 && c.SpeakerNpc == shop.Keeper);
                if (conv is null) continue;   // no conversation at all: interact falls through to the shop

                bool opensShop = conv.Nodes.Any(n => n.Choices.Any(ch => ch.Action == ConversationAction.OpenShop));
                Assert.That(opensShop, Is.True,
                    $"shop{num} ({shop.TrimmedName}) is kept by npc {shop.Keeper}, whose conversation has no "
                    + "OpenShop choice — talk-first means the storefront can never be reached");
            }
        });
    }

    /// <summary>One keeper, one shop. <c>GameWorld.ShopAssignedToNpc</c> resolves the first match, so a
    /// second shop on the same NPC is simply invisible — an authored storefront nobody can open.</summary>
    [Test]
    public void NoTwoShops_ShareAKeeper()
    {
        RequireShops();
        var dupes = _shops.Where(kv => kv.Value.Keeper > 0)
                          .GroupBy(kv => kv.Value.Keeper)
                          .Where(g => g.Count() > 1);
        Assert.That(dupes.Select(g => $"npc {g.Key} keeps shops {string.Join(", ", g.Select(kv => kv.Key))}"),
            Is.Empty, "only the first shop found for a keeper is ever opened");
    }

    /// <summary>A sales row priced at 0 is dead: <c>ShopSystem.Buy</c> refuses it rather than giving the
    /// item away, so it renders in the panel and does nothing when clicked. This is also the tripwire for
    /// a regenerated armory — <c>gen-items --apply</c> rewrites every item WITHOUT prices, so forgetting
    /// seed-prices afterwards empties every storefront in the world without erroring.</summary>
    [Test]
    public void EveryShopSalesRow_IsPricedAndPurchasable()
    {
        RequireShops();
        Assert.Multiple(() =>
        {
            foreach (var (num, shop) in _shops.OrderBy(kv => kv.Key))
                foreach (int itemNum in shop.SalesItem)
                {
                    Assert.That(_items, Does.ContainKey(itemNum), $"shop{num} sells missing item {itemNum}");
                    if (!_items.TryGetValue(itemNum, out var item)) continue;
                    Assert.That(item.Price, Is.GreaterThan(0),
                        $"shop{num} sells \"{item.TrimmedName}\" at price 0 — Buy refuses it, so the row is dead");
                    Assert.That(item.NonJunkable, Is.False,
                        $"shop{num} sells NonJunkable \"{item.TrimmedName}\" — that is currency or treasure");
                }
        });
    }

    /// <summary>Treasure is <c>NonJunkable</c>, so the universal 25% sell-back cannot touch it — a fence's
    /// barter row is the ONLY way it converts to gold. #58 moved roughly 30% of the bestiary's gold income
    /// into treasure, so a treasure with no buyer is that share of the economy stranded in bags.</summary>
    [Test]
    public void EveryTreasure_HasSomewhereToBeSold()
    {
        RequireShops();
        var treasures = _items.Where(kv => kv.Value.NonJunkable && kv.Value.Price > 0
                                        && kv.Key != Constants.GoldItemIndex)
                              .Select(kv => kv.Key).ToArray();
        Assert.That(treasures, Is.Not.Empty, "the seed authors no treasure");

        var bought = _shops.Values.SelectMany(s => s.BarterItem).Select(t => t.GiveItem).ToHashSet();
        Assert.Multiple(() =>
        {
            foreach (int t in treasures)
                Assert.That(bought, Does.Contain(t),
                    $"\"{_items[t].TrimmedName}\" is NonJunkable and no shop's trade table buys it — "
                    + "it can never become gold");
        });
    }

    /// <summary>
    /// A banner musters more than one kind of mob.
    ///
    /// <para>An attack-on-sight mob picks a fight with any hostile in range that is neither its own kind nor
    /// a same-group ally, scanning the whole 3×3 map neighbourhood. <c>Group</c> 0 already means "allied with
    /// my own kind only", so a number held by exactly one template grants nothing that group 0 did not — it
    /// is a warband whose comrades were moved off it, and it reads as a faction while behaving as a loner.</para>
    ///
    /// <para>Numbers are world-wide rather than per area: one reused between two areas that TOUCH would
    /// silently ally them across the seam.</para>
    /// </summary>
    [Test]
    public void NoBanner_MustersASingleKind()
    {
        RequireSeed();
        var hostile = _npcs.Where(n => n.Value.Behavior is NpcBehavior.Pursue)
            .ToDictionary(k => k.Key, v => v.Value);
        Assume.That(hostile, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var banner in hostile.Where(kv => kv.Value.Group != 0).GroupBy(kv => kv.Value.Group).OrderBy(g => g.Key))
            {
                Assert.That(banner.Count(), Is.GreaterThan(1),
                    $"group {banner.Key} musters only \"{banner.First().Value.TrimmedName}\", which same-kind "
                    + "peace already covers");
            }
        });
    }

    /// <summary>A side means nothing on an NPC the notice scan never looks at, and one wearing a number it
    /// can never act on reads as a faction member that has wandered out of its faction.</summary>
    [Test]
    public void NothingElse_CarriesASide()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, npc) in _npcs.OrderBy(k => k.Key))
            {
                if (npc.Behavior is NpcBehavior.Pursue) continue;
                Assert.That(npc.Group, Is.Zero,
                    $"npc{num} \"{npc.TrimmedName}\" is {npc.Behavior} and carries group {npc.Group}");
            }
        });
    }

    /// <summary>Every mob on a CREATURE row is on the same side. Each of these rows draws one family — the
    /// wolves, the birdmen, the gravebound, the orcs, the birds — and a family fights as one, so a lone orc
    /// carrying a company's number is a mis-set group. The human rows say nothing either way: a company, a
    /// cult and a lone scavenger all wear them, since the roster strides neighbors across the pool so they
    /// do not arrive looking like twins.</summary>
    [Test]
    public void EveryCreatureRow_IsOneSide()
    {
        RequireSeed();
        // Rows of sheet 0 that draw a creature rather than a person: 8/11/12/13/14/18/19 monsters,
        // 20/21/22 birds, 46 orc. The sheet has to be named: a row number is only unique within one
        // sheet, so row 8 of a second sheet is a different creature entirely.
        const int creatureSheet = 0;
        int[] creatureRows = [8, 11, 12, 13, 14, 18, 19, 20, 21, 22, 46];
        var byRow = _npcs.Values
            .Where(n => n.Behavior is NpcBehavior.Pursue)
            .Where(n => n.SpriteSheet == creatureSheet && creatureRows.Contains(n.Sprite))
            .GroupBy(n => n.Sprite);

        Assert.Multiple(() =>
        {
            foreach (var row in byRow)
            {
                var sides = row.Select(n => n.Group).Distinct().ToList();
                Assert.That(sides, Has.Count.EqualTo(1),
                    $"sprite row {row.Key} draws one family across several sides: "
                    + string.Join(", ", row.Select(n => $"{n.TrimmedName}={n.Group}")));
            }
        });
    }

}
