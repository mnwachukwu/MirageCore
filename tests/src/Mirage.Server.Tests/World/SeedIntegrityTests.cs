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
    private static Dictionary<int, SpellRecord> _spells = new();
    private static Dictionary<int, ClassRecord> _classes = new();
    private static Dictionary<int, ConversationRecord> _conversations = new();
    private static Dictionary<int, QuestRecord> _quests = new();
    private static Dictionary<int, ShopRecord> _shops = new();

    [OneTimeSetUp]
    public void LoadSeed()
    {
        string? data = FindDataDir();
        if (data is null) return;
        _items = LoadAll<ItemRecord>(data, "items", "item");
        _npcs = LoadAll<NpcRecord>(data, "npcs", "npc");
        _spells = LoadAll<SpellRecord>(data, "spells", "spell");
        _classes = LoadAll<ClassRecord>(data, "classes", "class");
        _conversations = LoadAll<ConversationRecord>(data, "conversations", "conversation");
        _quests = LoadAll<QuestRecord>(data, "quests", "quest");
        _shops = LoadAll<ShopRecord>(data, "shops", "shop");
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
        // Normalize is what the server runs on load and the editor on save, and it CLEARS fields that do
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

    [Test]
    public void EveryStartingLoadout_NamesThingsThatExist()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, cls) in _classes)
            {
                foreach (var s in cls.StartingItems ?? [])
                    Assert.That(_items.ContainsKey(s.ItemNum), Is.True,
                        $"class{num} ({cls.Name}) starts with item {s.ItemNum}, which does not exist");
                foreach (int spellNum in cls.StartingSpells ?? [])
                    Assert.That(_spells.ContainsKey(spellNum), Is.True,
                        $"class{num} ({cls.Name}) starts knowing spell {spellNum}, which does not exist");
            }
        });
    }

    [Test]
    public void EveryScroll_TeachesASpellThatExists()
    {
        RequireSeed();
        // A scroll is the ONLY way to learn a spell, so a scroll pointing at nothing is not a cosmetic
        // fault — it is a permanently unreachable ability.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items.Where(kv => kv.Value.Type == ItemType.Spell))
                Assert.That(_spells.ContainsKey(item.SpellNum), Is.True,
                    $"item{num} ({item.Name}) teaches spell {item.SpellNum}, which does not exist");
        });
    }

    // ── What the generators own, and nothing else guards ──────────────────────

    [Test]
    public void EveryPriceableItem_CarriesItsSeededPrice()
    {
        RequireSeed();
        // THE TRAP THIS EXISTS FOR: gen-items --apply rewrites every item file and does NOT write price;
        // that is seed-prices' stage. Regenerating the armory without re-running it leaves 558 items at
        // price 0 — a world of free gear, with nothing raising a hand.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items)
            {
                int derived = EconomyFormulas.ItemValue(item, _spells.GetValueOrDefault(item.SpellNum));
                if (derived <= 0) continue;   // currency, keys, treasure — authored or genuinely worthless
                Assert.That(item.Price, Is.EqualTo(derived),
                    $"item{num} ({item.Name}) is priced {item.Price} but the formula says {derived} — "
                    + "run seed-prices.cs after any gen-items --apply");
            }
        });
    }

    [Test]
    public void Durability_RisesWithTier_AndWithBulk()
    {
        RequireSeed();
        var gear = _items.Values.Where(i => ItemRecord.IsEquipment(i.Type)).ToArray();
        Assert.That(gear, Is.Not.Empty, "the seed authors no equipment — nothing left to hold to the tier curve");

        // The curve lives in gen-items (sqrt of level x bulk) and is unreachable from here, so what is
        // pinned is its SHAPE: a higher tier is sturdier, and within a tier a heavier piece is sturdier
        // than a lighter one. Bulk is not stored, but Power carries it — both come off the same
        // multiplier — so within a slot and tier, more Power must mean more durability.
        var tiers = gear.Select(i => (int)i.LevelReq).Distinct().OrderBy(l => l).ToArray();
        Assert.Multiple(() =>
        {
            // Compared BAND TO BAND — floor against floor and ceiling against ceiling — not every piece
            // against every piece. A tier-5 Tower Shield genuinely outlasts a tier-10 Buckler, because
            // bulk spans 0.75-1.25 while one rung is a smaller step than that. What must hold is that the
            // whole range shifts up, and comparing the max of one tier to the min of the next would
            // forbid the bulk spread rather than test the curve.
            for (int i = 1; i < tiers.Length; i++)
            {
                var lower = gear.Where(g => g.LevelReq == tiers[i - 1]).ToArray();
                var higher = gear.Where(g => g.LevelReq == tiers[i]).ToArray();
                Assert.That(higher.Min(g => g.Durability), Is.GreaterThan(lower.Min(g => g.Durability)),
                    $"tier {tiers[i]}'s lightest piece is no sturdier than tier {tiers[i - 1]}'s");
                Assert.That(higher.Max(g => g.Durability), Is.GreaterThan(lower.Max(g => g.Durability)),
                    $"tier {tiers[i]}'s heaviest piece is no sturdier than tier {tiers[i - 1]}'s");
            }

            foreach (var slot in gear.GroupBy(g => (g.Type, g.LevelReq)))
            {
                var byPower = slot.OrderBy(g => g.Power).ToArray();
                for (int i = 1; i < byPower.Length; i++)
                    Assert.That(byPower[i].Durability, Is.GreaterThanOrEqualTo(byPower[i - 1].Durability),
                        $"{byPower[i].Name} carries more Power than {byPower[i - 1].Name} but less durability");
            }
        });
    }

    [Test]
    public void Treasure_IsPricedAndProtected()
    {
        RequireSeed();
        // Treasure is typed None and priced by hand — the one item family whose worth is authored rather
        // than derived, and therefore the one nothing else can check.
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
                Assert.That(EconomyFormulas.ItemValue(t), Is.Zero,
                    $"item{num} ({t.Name}) must not be derivable, or seed-prices would overwrite it");
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
    /// says its AttackSay instead. So authored dialogue that can never open is silent, and this is what
    /// catches it.
    ///
    /// <para>Two things have to hold, and only two: the speaker sits above the hostile bestiary, and it
    /// names an NPC that exists. The number itself is free — a conversation may speak for any friendly,
    /// which is what lets a talker be added without disturbing a block that shops and quests already
    /// point at.</para></summary>
    [Test]
    public void EveryConversation_SpeaksForItsReservedNpc()
    {
        RequireConversations();
        const int firstFriendlyNpc = 125;   // 1-124 is the hostile bestiary
        Assert.Multiple(() =>
        {
            foreach (var (num, conv) in _conversations.OrderBy(kv => kv.Key))
            {
                Assert.That(conv.SpeakerNpc, Is.GreaterThanOrEqualTo(firstFriendlyNpc),
                    $"conversation{num} ({conv.TrimmedName}) speaks for npc {conv.SpeakerNpc}, inside the "
                    + "hostile bestiary — a mob would open a dialogue tree instead of fighting");
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

    // ── Quests ────────────────────────────────────────────────────────────────

    private static void RequireQuests()
    {
        RequireSeed();
        Assert.That(_quests, Is.Not.Empty, "the seed authors no quests");
    }

    /// <summary>Expected gold-equivalent per kill off an NPC's authored drop table — the same figure
    /// gen-quests sizes rewards from, recomputed here so the two cannot drift apart silently.</summary>
    private static double Yield(int npcNum)
    {
        if (!_npcs.TryGetValue(npcNum, out var npc) || npc.Drops is null) return 0;
        double total = 0;
        foreach (var d in npc.Drops)
        {
            if (!_items.TryGetValue(d.ItemNum, out var item)) continue;
            int unit = d.ItemNum == Constants.GoldItemIndex ? 1 : item.Price;
            total += Math.Min((int)d.Chance, 100) / 100.0 * Math.Max((int)d.Quantity, 1) * unit;
        }
        return total;
    }

    /// <summary>ObjectiveKind declares Kill, Fetch, Gather and Explore, but
    /// <c>ObjectiveSystem.RecordNpcKill</c> is the only advance site in the engine and it advances Kill
    /// alone — the rest are declared plumbing. A Fetch quest would be accepted, tracked, and sit at 0/1
    /// forever with no error anywhere. This is the check that stops an editor session authoring one.</summary>
    [Test]
    public void EveryQuestObjective_IsAKillOnARealNpc()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0) continue;
                Assert.That(quest.Objectives, Is.Not.Empty, $"quest{num} has no objectives to complete");
                foreach (var o in quest.Objectives)
                {
                    Assert.That(o.Kind, Is.EqualTo(ObjectiveKind.Kill),
                        $"quest{num} ({quest.TrimmedName}) uses {o.Kind}, which no engine path advances — "
                        + "it can never be completed");
                    Assert.That(_npcs, Does.ContainKey(o.Target),
                        $"quest{num} targets npc {o.Target}, which is not in the seed");
                    Assert.That(o.Count, Is.GreaterThan(0), $"quest{num} objective needs a positive count");
                }
            }
        });
    }

    /// <summary>The cross-collection reachability rule. Interaction is TALK-FIRST
    /// (<c>PacketHandler.HandleNpcInteract</c>): conversation, then a visible quest, then the shop. So an
    /// NPC that has a conversation is only reached THROUGH it, and a quest-giver whose tree carries no
    /// <c>OpenQuests</c> choice offers a quest that no player can ever see or hand in. Nothing in the
    /// engine reports this — the quest simply never appears.</summary>
    [Test]
    public void EveryQuestGiver_CanActuallyBeAskedForIt()
    {
        RequireQuests();
        Assert.That(_conversations, Is.Not.Empty, "the seed authors no conversations to check against");

        bool OffersQuests(int npcNum) =>
            _conversations.Values.Any(c => c.TrimmedName.Length > 0 && c.SpeakerNpc == npcNum
                && c.Nodes.Any(n => n.Choices.Any(ch => ch.Action == ConversationAction.OpenQuests)));

        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0 || quest.GiverNpc == 0) continue;

                Assert.That(OffersQuests(quest.GiverNpc), Is.True,
                    $"quest{num} ({quest.TrimmedName}) is given by npc {quest.GiverNpc}, whose conversation "
                    + "has no OpenQuests choice — talk-first means the quest is unreachable");
                Assert.That(OffersQuests(quest.EffectiveTurnInNpc), Is.True,
                    $"quest{num} turns in at npc {quest.EffectiveTurnInNpc}, whose conversation has no "
                    + "OpenQuests choice — the quest could be accepted but never handed in");
            }
        });
    }

    /// <summary>THE ANTI-FARM INVARIANT. A repeatable quest that pays more gold per kill than the kills
    /// themselves yield turns questing into a strictly better loop than playing, and it compounds without
    /// limit because the quest resets. One-shot quests are deliberately exempt: they anchor to a share of
    /// the level instead, which is safe precisely because it cannot repeat.</summary>
    [Test]
    public void NoRepeatableQuest_OutPaysTheGrindItAsksFor()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0 || !quest.Repeatable) continue;

                int kills = quest.Objectives.Sum(o => o.Count);
                if (kills == 0) continue;
                double grind = quest.Objectives.Sum(o => Yield(o.Target) * o.Count);
                if (grind <= 0) continue;   // targets with no drop table are caught by their own test

                // Subsequent completions pay the repeat set — or the main set when no repeat set exists.
                var payingSet = quest.HasRepeatRewards ? quest.RepeatRewardItems : quest.RewardItems;
                long gold = payingSet.Where(r => r.ItemNum == Constants.GoldItemIndex).Sum(r => (long)r.Quantity);

                Assert.That(gold, Is.LessThan(grind),
                    $"quest{num} ({quest.TrimmedName}) is repeatable and pays {gold:n0} gold for a grind "
                    + $"worth {grind:n0} — turning it in beats killing, forever");
            }
        });
    }

    /// <summary>A chain must be walkable: the prerequisite has to exist and be reachable at or below the
    /// level of the quest it unlocks, or the player meets the second door before the first.</summary>
    [Test]
    public void EveryQuestChain_CanBeWalkedInOrder()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0 || quest.PrereqQuest == 0) continue;

                Assert.That(_quests, Does.ContainKey(quest.PrereqQuest),
                    $"quest{num} requires quest {quest.PrereqQuest}, which does not exist");
                if (!_quests.TryGetValue(quest.PrereqQuest, out var prereq)) continue;

                Assert.That(prereq.ReqLevel, Is.LessThanOrEqualTo(quest.ReqLevel),
                    $"quest{num} ({quest.TrimmedName}) unlocks at level {quest.ReqLevel} but its "
                    + $"prerequisite \"{prereq.TrimmedName}\" needs {prereq.ReqLevel}");
                Assert.That(prereq.GiverNpc, Is.EqualTo(quest.GiverNpc),
                    $"quest{num} chains off a quest given by a different NPC — the seed's three bands "
                    + "have no content between them, so a cross-hub chain cannot be walked");
            }
        });
    }

    /// <summary>A chain whose fights get harder while the ask stays flat reads as filler, so an objective
    /// may never shrink as its chain deepens. The one exception is a shrink TO A SINGLE TARGET — that is
    /// a boss step, and asking for one of something is the point of a boss.</summary>
    [Test]
    public void EveryQuestChain_EscalatesItsObjective()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0 || quest.PrereqQuest == 0) continue;
                if (!_quests.TryGetValue(quest.PrereqQuest, out var prereq)) continue;

                int kills = quest.Objectives.Sum(o => o.Count);
                int before = prereq.Objectives.Sum(o => o.Count);
                if (kills == 1 || before == 1) continue;   // a boss step on either side

                Assert.That(kills, Is.GreaterThanOrEqualTo(before),
                    $"quest{num} ({quest.TrimmedName}) asks for {kills} kills, fewer than its "
                    + $"prerequisite \"{prereq.TrimmedName}\" at {before} — a chain must not get easier");
            }
        });
    }

    /// <summary>A quest flagged <c>Repeatable</c> with <c>Cadence.None</c> is a trap the engine cannot
    /// report: <c>QuestSystem.PeriodKeyFor</c> returns "" for None, and <c>IsOnRepeatCooldown</c> compares
    /// the stored key against it — so the empty key equals its own and the quest reports a PERMANENT
    /// cooldown. It advertises itself as repeatable in the panel and then never re-opens.</summary>
    [Test]
    public void EveryRepeatableQuest_DeclaresACadenceThatCanRollOver()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
            {
                if (quest.TrimmedName.Length == 0) continue;
                if (quest.Repeatable)
                    Assert.That(quest.Cadence, Is.Not.EqualTo(QuestCadence.None),
                        $"quest{num} ({quest.TrimmedName}) is repeatable with no cadence — its period key "
                        + "never changes, so it reports a permanent cooldown and never re-opens");
                else
                    Assert.That(quest.Cadence, Is.EqualTo(QuestCadence.None),
                        $"quest{num} ({quest.TrimmedName}) carries a {quest.Cadence} cadence but is not "
                        + "repeatable — the cadence does nothing and misleads the next reader");
            }
        });
    }

    // ── The content chain closes ──────────────────────────────────────────────

    /// <summary>The last link. Conversations reserve npc numbers 125+, and shops and quests are authored
    /// against them, so a generator that has not run leaves every one of those references pointing at
    /// nothing. None of it errors at runtime — an unresolvable SpeakerNpc means "no conversation", a
    /// missing keeper means "no shop" — which is exactly why it is checked here.</summary>
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

            foreach (var (num, quest) in _quests.Where(kv => kv.Value.GiverNpc > 0))
            {
                Assert.That(_npcs, Does.ContainKey(quest.GiverNpc),
                    $"quest{num} ({quest.TrimmedName}) is given by npc {quest.GiverNpc}, which does not exist");
                Assert.That(_npcs, Does.ContainKey(quest.EffectiveTurnInNpc),
                    $"quest{num} turns in at npc {quest.EffectiveTurnInNpc}, which does not exist");
            }
        });
    }

    /// <summary>Anyone who carries content must be non-hostile and must not be loot. A shopkeeper on
    /// AttackOnSight would attack the customer; one with a drop table turns a storefront into a farm.</summary>
    [Test]
    public void EveryContentCarrier_IsFriendlyAndCarriesNoLoot()
    {
        RequireSeed();
        var carriers = _conversations.Values.Where(c => c.TrimmedName.Length > 0).Select(c => c.SpeakerNpc)
            .Concat(_shops.Values.Where(s => s.Keeper > 0).Select(s => s.Keeper))
            .Concat(_quests.Values.Where(q => q.GiverNpc > 0).Select(q => q.GiverNpc))
            .Distinct().Where(_npcs.ContainsKey).ToArray();
        Assert.That(carriers, Is.Not.Empty, "no NPC carries content — the roster lost its townsfolk");

        Assert.Multiple(() =>
        {
            foreach (int num in carriers)
            {
                var npc = _npcs[num];
                Assert.That(npc.Behavior, Is.EqualTo(NpcBehavior.Friendly),
                    $"npc {num} ({npc.TrimmedName}) carries content but is {npc.Behavior} — it would fight its own customers");
                Assert.That(npc.Drops ?? [], Is.Empty,
                    $"npc {num} ({npc.TrimmedName}) carries content AND a drop table — killing the shopkeeper pays");
            }
        });
    }

    /// <summary>A guard is an unwinnable fight, but LEVEL is the wrong lever for that: it is a derived
    /// number, and pushing it past the ceiling only makes the stat line strange. Guards sit AT the player
    /// ceiling and are made unwinnable by <see cref="NpcRecord.ExtraHp"/>.
    ///
    /// <para>Two shapes. A melee guard runs INT 0 so it never casts; a caster guard runs STR 0, so
    /// <c>P(cast) = Int/(Int+Str)</c> makes it cast every beat. Dropping the unused stat is what lets the
    /// other three read high at 255, so a guard with all four populated has lost its teeth.</para></summary>
    [Test]
    public void EveryGuard_IsAMaxedWallThatDropsNothing()
    {
        RequireSeed();
        var guards = _npcs.Where(kv => kv.Value.Behavior == NpcBehavior.Guard).ToArray();
        Assert.That(guards, Is.Not.Empty, "the roster authors no guards");

        Assert.Multiple(() =>
        {
            foreach (var (num, g) in guards)
            {
                string who = $"guard {num} ({g.TrimmedName})";
                int level = StatFormulas.NpcLevel(g.Str, g.Def, g.Int, g.Spd);

                Assert.That(level, Is.EqualTo(Constants.MaxLevel),
                    $"{who} computes to level {level} — guards sit exactly at the player ceiling");
                Assert.That(g.ExtraHp, Is.GreaterThan(0),
                    $"{who} has no ExtraHp — at level {Constants.MaxLevel} with no HP wall it is just a "
                    + "very good player, and a maxed character can kill it");
                Assert.That(g.Spd, Is.GreaterThan(0),
                    $"{who} has no SPD — guards always run, and one that cannot close is one you walk away from");
                Assert.That(g.Str == 0 || g.Int == 0, Is.True,
                    $"{who} carries both STR and INT — a guard commits to melee (INT 0) or to casting "
                    + "(STR 0); splitting the budget four ways makes all of it mediocre");
                Assert.That(g.Drops ?? [], Is.Empty,
                    $"{who} carries loot — killing guards must never be worth doing");
            }
        });
    }

    // ── Shops ─────────────────────────────────────────────────────────────────

    private static void RequireShops()
    {
        RequireSeed();
        Assert.That(_shops, Is.Not.Empty, "the seed authors no shops");
    }

    /// <summary>The companion to <see cref="EveryQuestGiver_CanActuallyBeAskedForIt"/>, and the reason
    /// #50 came before #52. Interaction is TALK-FIRST (<c>PacketHandler.HandleNpcInteract</c>):
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
                                        && kv.Key != Constants.GoldItemIndex && kv.Key != Constants.ValorItemIndex)
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

    /// <summary>Rewards must name real items. Gold is item #1 like anywhere else in the engine, so this
    /// also catches a reward that forgot to be currency.</summary>
    [Test]
    public void EveryQuestReward_NamesAnItemThatExists()
    {
        RequireQuests();
        Assert.Multiple(() =>
        {
            foreach (var (num, quest) in _quests.OrderBy(kv => kv.Key))
                foreach (var reward in quest.RewardItems.Concat(quest.RepeatRewardItems))
                {
                    Assert.That(_items, Does.ContainKey(reward.ItemNum),
                        $"quest{num} rewards item {reward.ItemNum}, which is not in the seed");
                    Assert.That(reward.Quantity, Is.GreaterThan(0),
                        $"quest{num} rewards item {reward.ItemNum} with no quantity");
                }
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
        var hostile = _npcs.Where(n => n.Value.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)
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

    /// <summary>Each of the two upper bands holds exactly two sides — a cult and the fauna it shares its water
    /// or its ash with. Two is what makes a band worth walking through: one side and nothing in it ever fights
    /// anything but the player, three and a number has gone astray. Nothing up there is ungrouped, either,
    /// since a loner among a whole band of one faction is a mob that fights every neighbour it has.</summary>
    [Test]
    public void EachUpperBand_HoldsTwoSides()
    {
        RequireSeed();
        foreach (var (lo, hi, band) in new[] { (100, 120, "the Sunken Reach"), (235, 255, "the Ashen Throne") })
        {
            var inBand = _npcs.Values
                .Where(n => n.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)
                .Where(n => StatFormulas.NpcLevel(n) >= lo && StatFormulas.NpcLevel(n) <= hi)
                .ToList();
            Assume.That(inBand, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(inBand.Where(n => n.Group == 0).Select(n => n.TrimmedName).ToList(), Is.Empty,
                    $"{band} ({lo}-{hi}) holds a mob on no side at all");
                Assert.That(inBand.Select(n => n.Group).Distinct().ToList(), Has.Count.EqualTo(2),
                    $"{band} ({lo}-{hi}) is split across "
                    + string.Join(", ", inBand.GroupBy(n => n.Group).OrderBy(g => g.Key)
                        .Select(g => $"{g.Key}x{g.Count()}")));
            });
        }
    }

    /// <summary>A side means nothing on an NPC the hostility scan never looks at — it skips Friendly,
    /// Stationary and Guard outright — and a guard wearing one reads as a guard that has taken a side.</summary>
    [Test]
    public void NothingElse_CarriesASide()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, npc) in _npcs.OrderBy(k => k.Key))
            {
                if (npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked) continue;
                Assert.That(npc.Group, Is.Zero,
                    $"npc{num} \"{npc.TrimmedName}\" is {npc.Behavior} and carries group {npc.Group}");
            }
        });
    }

    /// <summary>
    /// Everyone who LIVES here carries a light.
    ///
    /// <para>The bestiary lights its hostiles — every one that has hands for a torch — so a world where the
    /// townsfolk and wardens are dark is one whose only night-time glow belongs to the things hunting you.
    /// A town reads as abandoned and a warden becomes something that runs at you out of the black.</para>
    ///
    /// <para>This is guarded because the gap was invisible: friendlies and guards come from a different
    /// generator than the mobs, and that one simply never mentioned light, so every one of them defaulted
    /// dark and nothing anywhere said so.</para>
    /// </summary>
    [Test]
    public void EverythingThatLivesHere_CarriesALight()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, npc) in _npcs.OrderBy(k => k.Key))
            {
                if (npc.Behavior is not (NpcBehavior.Friendly or NpcBehavior.Guard)) continue;
                Assert.That(npc.EmitsLight, Is.True,
                    $"npc{num} \"{npc.TrimmedName}\" is {npc.Behavior} and stands in the dark");
            }
        });
    }

    /// <summary>Every mob on a CREATURE row is on the same side. Each of these rows draws one family — the
    /// wolves, the birdmen, the gravebound, the orcs, the birds — and a family fights as one, so a lone orc
    /// carrying a company's number is a mis-set group. The human rows say nothing either way: a company, a
    /// cult and a lone scavenger all wear them, since the roster strides neighbours across the pool so they
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
            .Where(n => n.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.AttackWhenAttacked)
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

    /// <summary>Every class opens on exactly <see cref="Constants.PlayerBaseStatTotal"/> across the four
    /// stats. It is the number the whole progression system is measured from: a level's stat budget is that
    /// total plus three per level since, the NPC virtual level inverts the same arithmetic, and the account
    /// editor calls a character sheet impossible when it holds more than its level allows.
    ///
    /// <para>A class authored one point over would put every character it ever creates over budget from the
    /// moment of creation, and one point under would quietly hand its players a permanently smaller sheet.
    /// Neither shows up anywhere at runtime — the class simply loads.</para></summary>
    [Test]
    public void EveryClass_OpensOnTheBaseStatTotal()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, cls) in _classes.OrderBy(k => k.Key))
            {
                Assert.That(cls.Str + cls.Def + cls.Spd + cls.Int, Is.EqualTo(Constants.PlayerBaseStatTotal),
                    $"class{num} \"{cls.TrimmedName}\" opens on STR {cls.Str} / DEF {cls.Def} / SPD {cls.Spd} "
                    + $"/ INT {cls.Int}, which is not the {Constants.PlayerBaseStatTotal} every level's stat "
                    + "budget is measured from");
            }
        });
    }

    /// <summary>Every class must be able to hurt something on the day it is created — a granted WEAPON or a
    /// granted SubHp spell, since those are the only two things that deal damage.
    ///
    /// <para>Both halves are gated on the class's base stats, so a class can fall through the middle: too
    /// little STR to lift the lightest dagger AND too little INT for the weakest attack spell. Nothing else
    /// notices. The seed loads, the world runs, and the class is simply unplayable until it finds a weapon
    /// it cannot yet use — which is a first hour nobody would sit through.</para>
    ///
    /// <para>Asked through <see cref="StartingLoadout"/> rather than by reading the authored lists, because
    /// an authored line whose gates the class fails is SKIPPED at creation. What matters is what the
    /// character RECEIVES, not what the class file offers.</para></summary>
    [Test]
    public void EveryClass_StartsAbleToDealDamage()
    {
        RequireSeed();
        var items = BuildIndexedArray(_items);
        var spells = BuildIndexedArray(_spells);

        Assert.Multiple(() =>
        {
            foreach (var (num, cls) in _classes.OrderBy(k => k.Key))
            {
                bool hasWeapon = StartingLoadout.ResolveItems(cls, num, items)
                    .Any(g => g.Type == ItemType.Weapon);
                bool hasAttackSpell = StartingLoadout.ResolveSpells(cls, num, spells)
                    .Any(n => n < spells.Length && spells[n].Type == SpellType.SubHp);

                Assert.That(hasWeapon || hasAttackSpell, Is.True,
                    $"class{num} \"{cls.TrimmedName}\" (STR {cls.Str}, INT {cls.Int}) starts with neither a "
                    + "weapon it can lift nor an attack spell it can cast, so it cannot fight at all");
            }
        });
    }

    /// <summary>One opener per class, never two. A class that clears both gates takes the weapon and leaves
    /// the attack spell — the SubHp spell is what a class with no STR uses INSTEAD of steel, not a second
    /// helping for a class that already swings.
    ///
    /// <para>Carrying both is not just surplus: the spell drags the reagent economy into a kit that has a
    /// weapon and no use for it, so a melee-ish generalist would open owing upkeep on a resource it never
    /// needed to spend.</para></summary>
    [Test]
    public void NoClass_StartsWithBothAWeaponAndAnAttackSpell()
    {
        RequireSeed();
        var items = BuildIndexedArray(_items);
        var spells = BuildIndexedArray(_spells);

        Assert.Multiple(() =>
        {
            foreach (var (num, cls) in _classes.OrderBy(k => k.Key))
            {
                bool hasWeapon = StartingLoadout.ResolveItems(cls, num, items)
                    .Any(g => g.Type == ItemType.Weapon);
                bool hasAttackSpell = StartingLoadout.ResolveSpells(cls, num, spells)
                    .Any(n => n < spells.Length && spells[n].Type == SpellType.SubHp);

                Assert.That(hasWeapon && hasAttackSpell, Is.False,
                    $"class{num} \"{cls.TrimmedName}\" opens with a weapon AND an attack spell — one or the "
                    + "other, and the weapon wins when a class can hold both");
            }
        });
    }

    /// <summary>Exactly one class opens with a heal. Restoring HP is the healer's defining trick, and a
    /// restorative in every caster's opening book makes it the common case instead — which is a balance
    /// decision that can be made by accident, since the starting book is DERIVED from whatever each class
    /// happens to be able to cast.
    ///
    /// <para>Counts what is GRANTED, not what is authored: a line the class fails is skipped at creation,
    /// so an authored heal on a class that cannot cast it would read as a violation that never happens.</para></summary>
    [Test]
    public void ExactlyOneClass_StartsWithAHeal()
    {
        RequireSeed();
        var spells = BuildIndexedArray(_spells);

        var healers = _classes.OrderBy(k => k.Key)
            .Where(kv => StartingLoadout.ResolveSpells(kv.Value, kv.Key, spells)
                .Any(n => n < spells.Length && spells[n].Type == SpellType.AddHp))
            .Select(kv => kv.Value.TrimmedName)
            .ToList();

        Assert.That(healers, Has.Count.EqualTo(1),
            $"classes opening with a heal: [{string.Join(", ", healers)}] — the healer's trick belongs to "
            + "exactly one class, and nobody holding it is as wrong as everybody holding it");
    }

    /// <summary>Turns a number-keyed seed map into the 1-based array the shared gates expect, sized to the
    /// highest key so a gap reads as a blank record rather than shifting everything after it.</summary>
    private static T[] BuildIndexedArray<T>(Dictionary<int, T> byNumber) where T : new()
    {
        int max = byNumber.Count == 0 ? 0 : byNumber.Keys.Max();
        var arr = new T[max + 1];
        for (int i = 0; i <= max; i++) arr[i] = byNumber.TryGetValue(i, out var rec) ? rec : new T();
        return arr;
    }
}
