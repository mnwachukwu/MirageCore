using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// The world check finds records that do not agree with each other.
///
/// <para>Each case here is something the game only discovers at the worst moment — a warp that refuses the
/// one player who steps on it, a seam that renders wrong, a shop nobody can open. The point of the sweep is
/// that an author learns instead, at the time they can still fix it.</para>
///
/// <para><b>A reference is broken two ways</b>, and both are checked as one: a number outside the world's
/// range, and a number naming a slot nobody has authored. The second is the common one, since renumbering
/// content leaves references pointing at blanks.</para>
/// </summary>
[TestFixture]
public class WorldCheckTests
{
    // Index 0 unused, matching every record family. Slots 1..3 of each family are authored, so a reference
    // to 1 resolves and a reference to 4 does not.
    private static WorldContent World(int mapCount = 6)
    {
        var maps = new MapRecord?[mapCount];
        // Slots 1..3 are places; the rest are the blank slots a world is padded out to.
        for (int i = 1; i < Math.Min(4, mapCount); i++) maps[i] = new MapRecord(16, 12) { Name = $"Map {i}" };
        for (int i = 4; i < mapCount; i++) maps[i] = new MapRecord(16, 12);

        return new WorldContent
        {
            Maps = maps,
            Items = Family(i => new ItemRecord { Name = $"Item {i}" }),
            Npcs = Family(i => new NpcRecord { Name = $"Npc {i}" }),
            Shops = Family(i => new ShopRecord { Name = $"Shop {i}", Keeper = 1 }),
            Spells = Family(i => new SpellRecord { Name = $"Spell {i}" }),
            Quests = Family(i => new QuestRecord { Name = $"Quest {i}" }),
            Conversations = Family(i => new ConversationRecord { Name = $"Conv {i}" }),
            Classes = Family(i => new ClassRecord { Name = $"Class {i}" }),
        };
    }

    private static T?[] Family<T>(Func<int, T> make) where T : class
    {
        var all = new T?[5];
        for (int i = 1; i <= 3; i++) all[i] = make(i);
        return all;
    }

    private const int Absent = 4;   // in range, never authored
    private const int Present = 1;

    private static IEnumerable<WorldIssueKind> Kinds(WorldContent w) => WorldCheck.Run(w).Select(i => i.Kind);

    private static void Join(WorldContent w, int a, int b)
    {
        w.Maps[a]!.Right = b;
        w.Maps[b]!.Left = a;
    }

    // ── Nothing wrong ────────────────────────────────────────────────────────

    [Test]
    public void AWorldThatHoldsTogether_ReportsNothing()
    {
        var w = World();
        Join(w, 1, 2);
        w.Maps[1]!.Tile[3, 3] = w.Maps[1]!.Tile[3, 3].WithGroundAttr(
            new TileAttr { Type = TileType.Warp, WarpMap = 2, WarpX = 5, WarpY = 5 });

        Assert.That(WorldCheck.Run(w), Is.Empty);
    }

    /// <summary>A slot nobody has authored is not a fault in itself. A thousand blank maps must report
    /// nothing, or the report is unreadable on every world.</summary>
    [Test]
    public void BlankSlots_ReportNothing()
    {
        Assert.That(WorldCheck.Run(World(1000)), Is.Empty);
    }

    // ── The map graph ────────────────────────────────────────────────────────

    [Test]
    public void ALinkBetweenDifferentSizes_IsFound()
    {
        var w = World();
        w.Maps[2] = new MapRecord(24, 20) { Name = "Map 2" };
        Join(w, 1, 2);

        var issue = WorldCheck.Run(w).First(i => i.Kind == WorldIssueKind.LinkSizeMismatch);

        Assert.That(issue.Detail, Does.Contain("24x20").And.Contain("16x12"));
    }

    [Test]
    public void AOneWayLink_IsFoundOnceFromTheLowerMap()
    {
        var w = World();
        w.Maps[1]!.Right = 2;   // map 2 never answers

        var issues = WorldCheck.Run(w).Where(i => i.Kind == WorldIssueKind.LinkNotReciprocal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Count.EqualTo(1), "one broken seam is one finding");
            Assert.That(issues[0].OwnerNum, Is.EqualTo(1));
        });
    }

    [Test]
    public void ALinkPastTheCeiling_IsFound()
    {
        var w = World();
        w.Maps[1]!.Up = 999;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.LinkOutOfRange));
    }

    /// <summary>The case per-map dimensions created: a warp aimed at a tile its destination does not have.
    /// The server refuses it when a player steps on it and tells only that player.</summary>
    [Test]
    public void AWarpOntoATileTheDestinationDoesNotHave_IsFound()
    {
        var w = World();
        w.Maps[2] = new MapRecord(8, 8) { Name = "Map 2" };
        w.Maps[1]!.Tile[1, 1] = w.Maps[1]!.Tile[1, 1].WithGroundAttr(
            new TileAttr { Type = TileType.Warp, WarpMap = 2, WarpX = 12, WarpY = 2 });

        var issue = WorldCheck.Run(w).First(i => i.Kind == WorldIssueKind.WarpTileOutside);

        Assert.Multiple(() =>
        {
            Assert.That(issue.OwnerKind, Is.EqualTo(WorldRecordKind.Map));
            Assert.That(issue.OwnerNum, Is.EqualTo(1));
            Assert.That((issue.X, issue.Y), Is.EqualTo((1, 1)), "the finding names the tile that is wrong");
            Assert.That(issue.HasTile, Is.True);
            Assert.That(issue.Detail, Does.Contain("8x8"));
        });
    }

    [Test]
    public void AWarpOntoTheFringe_IsCheckedToo()
    {
        var w = World();
        w.Maps[2] = new MapRecord(8, 8) { Name = "Map 2" };
        w.Maps[1]!.Tile[4, 4] = w.Maps[1]!.Tile[4, 4] with
        {
            FringeAttr = new FringeAttr { Type = TileType.Warp, WarpMap = 2, WarpX = 30, WarpY = 1 },
        };

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.WarpTileOutside));
    }

    [Test]
    public void ABootPointOutsideItsMap_IsFound()
    {
        var w = World();
        w.Maps[1]!.BootMap = 2;
        w.Maps[1]!.BootX = 40;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.BootTileOutside));
    }

    [Test]
    public void AGroupWithNoRecord_IsFound()
    {
        var w = World() with { GroupExists = _ => false };
        w.Maps[1]!.MapGroup = 7;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.MapGroupMissing));
    }

    [Test]
    public void ASpawnPinOutsideItsMap_IsFound()
    {
        var w = World();
        w.Maps[1]!.Npcs.Add(new MapNpcEntry(Present, 40, 2));

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.SpawnPinOutside));
    }

    /// <summary>A map trimmed smaller can strand a light outside it, and a light nothing draws is invisible
    /// to every other way of noticing.</summary>
    [Test]
    public void ALightOutsideItsMap_IsFound()
    {
        var w = World();
        w.Maps[1]!.Lights.Add(new PlacedLight(Guid.NewGuid(), 40, 2, new LightSpec()));

        var issue = WorldCheck.Run(w).First(i => i.Kind == WorldIssueKind.LightOutside);

        Assert.That((issue.X, issue.Y), Is.EqualTo((40, 2)));
    }

    // ── A reference is broken by a blank slot as well as by a bad number ──────

    [Test]
    public void AMapSpawningAnUnauthoredNpc_IsFound()
    {
        var w = World();
        w.Maps[1]!.Npcs.Add(new MapNpcEntry(Absent, null, null));

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.NpcMissing));
    }

    [Test]
    public void ATileSpawningAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Maps[1]!.Tile[2, 2] = w.Maps[1]!.Tile[2, 2].WithGroundAttr(
            new TileAttr { Type = TileType.Item, ItemNum = Absent, ItemQuantity = 1 });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ItemMissing));
    }

    [Test]
    public void ALockedDoorWithNoKey_IsFound()
    {
        var w = World();
        w.Maps[1]!.Tile[2, 2] = w.Maps[1]!.Tile[2, 2].WithGroundAttr(
            new TileAttr { Type = TileType.Key, KeyItemNum = 0 });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ItemMissing));
    }

    // ── NPCs, items, spells ──────────────────────────────────────────────────

    [Test]
    public void AnNpcDroppingAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Npcs[1]!.Drops = [new NpcDrop { ItemNum = Absent, Quantity = 1, Chance = 50 }];

        var issue = WorldCheck.Run(w).First(i => i.Kind == WorldIssueKind.ItemMissing);

        Assert.Multiple(() =>
        {
            Assert.That(issue.OwnerKind, Is.EqualTo(WorldRecordKind.Npc));
            Assert.That(issue.OwnerNum, Is.EqualTo(1));
        });
    }

    [Test]
    public void AScrollTeachingAnUnauthoredSpell_IsFound()
    {
        var w = World();
        w.Items[1]!.Type = ItemType.Spell;
        w.Items[1]!.SpellNum = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.SpellMissing));
    }

    /// <summary>The field only means a spell on a scroll. On every other type it carries something else, so
    /// reading it as a reference would report a fault on almost every item in the world.</summary>
    [Test]
    public void ANonScrollItem_IsNotReadAsASpellReference()
    {
        var w = World();
        w.Items[1]!.Type = ItemType.Weapon;
        w.Items[1]!.SpellNum = 999;

        Assert.That(Kinds(w), Does.Not.Contain(WorldIssueKind.SpellMissing));
    }

    [Test]
    public void AGiveItemSpellNamingAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Spells[1]!.Type = SpellType.GiveItem;
        w.Spells[1]!.ItemNum = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ItemMissing));
    }

    [Test]
    public void AGateNamingAnUnauthoredClass_IsFound()
    {
        var w = World();
        w.Items[1]!.AllowedClasses = [Absent];

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ClassMissing));
    }

    // ── Shops ────────────────────────────────────────────────────────────────

    /// <summary>A shop is opened by talking to its keeper and by nothing else, so one with no keeper is a
    /// shop no player can ever reach.</summary>
    [Test]
    public void AShopWithNoKeeper_IsFound()
    {
        var w = World();
        w.Shops[1]!.Keeper = 0;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ShopHasNoKeeper));
    }

    [Test]
    public void AShopKeptByAnUnauthoredNpc_IsFound()
    {
        var w = World();
        w.Shops[1]!.Keeper = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.NpcMissing));
    }

    [Test]
    public void AShopTradingAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Shops[1]!.SalesItem.Add(Absent);
        w.Shops[2]!.BarterItem.Add(new BarterItemRecord { GiveItem = Present, GetItem = Absent });

        Assert.That(WorldCheck.Run(w).Count(i => i.Kind == WorldIssueKind.ItemMissing), Is.EqualTo(2));
    }

    // ── Quests ───────────────────────────────────────────────────────────────

    [Test]
    public void AQuestNamingUnauthoredNpcs_IsFound()
    {
        var w = World();
        w.Quests[1]!.GiverNpc = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.NpcMissing));
    }

    [Test]
    public void AKillObjectiveNamingAnUnauthoredNpc_IsFound()
    {
        var w = World();
        w.Quests[1]!.Objectives.Add(new Objective { Kind = ObjectiveKind.Kill, Target = Absent, Count = 1 });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.NpcMissing));
    }

    /// <summary>Target 0 is the wildcard — "any target of this kind" — and names nothing to check.</summary>
    [Test]
    public void AWildcardObjective_IsNotAReference()
    {
        var w = World();
        w.Quests[1]!.Objectives.Add(new Objective { Kind = ObjectiveKind.Kill, Target = 0, Count = 1 });

        Assert.That(WorldCheck.Run(w), Is.Empty);
    }

    [Test]
    public void AQuestRewardingAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Quests[1]!.RewardItems.Add(new QuestReward { ItemNum = Absent, Quantity = 1 });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ItemMissing));
    }

    [Test]
    public void AQuestRequiringAnUnauthoredQuest_IsFound()
    {
        var w = World();
        w.Quests[1]!.PrereqQuest = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.QuestMissing));
    }

    /// <summary>A quest that requires itself, directly or around a loop, can never be accepted by anyone.</summary>
    [TestCase(1, 1)]
    [TestCase(2, 3)]
    public void APrerequisiteLoop_IsFound(int a, int b)
    {
        var w = World();
        w.Quests[a]!.PrereqQuest = b;
        w.Quests[b]!.PrereqQuest = a;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.QuestPrereqCycle));
    }

    [Test]
    public void AChainOfPrerequisitesThatEnds_IsNotACycle()
    {
        var w = World();
        w.Quests[3]!.PrereqQuest = 2;
        w.Quests[2]!.PrereqQuest = 1;

        Assert.That(Kinds(w), Does.Not.Contain(WorldIssueKind.QuestPrereqCycle));
    }

    // ── Conversations ────────────────────────────────────────────────────────

    [Test]
    public void AConversationSpokenByAnUnauthoredNpc_IsFound()
    {
        var w = World();
        w.Conversations[1]!.SpeakerNpc = Absent;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.NpcMissing));
    }

    [Test]
    public void AChoiceLeadingToANodeThatIsNotThere_IsFound()
    {
        var w = World();
        var conv = w.Conversations[1]!;
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "on", NextNodeId = 99 }],
        });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ConversationNodeMissing));
    }

    /// <summary>Zero ends the conversation, which is an ending rather than a dangling link.</summary>
    [Test]
    public void AChoiceThatEndsTheConversation_IsNotADanglingLink()
    {
        var w = World();
        var conv = w.Conversations[1]!;
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "bye", NextNodeId = 0 }],
        });

        Assert.That(Kinds(w), Does.Not.Contain(WorldIssueKind.ConversationNodeMissing));
    }

    [Test]
    public void ARootNodeThatIsNotThere_IsFound()
    {
        var w = World();
        w.Conversations[1]!.RootNodeId = 42;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ConversationNodeMissing));
    }

    /// <summary>Both hand-off actions open a role the SPEAKER holds, so a speaker without that role leaves
    /// the choice doing nothing when a player picks it.</summary>
    [Test]
    public void AChoiceOpeningAShopTheSpeakerDoesNotKeep_IsFound()
    {
        var w = World();
        w.Shops[1]!.Keeper = 1;
        var conv = w.Conversations[1]!;
        conv.SpeakerNpc = 2;   // keeps nothing
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "shop", Action = ConversationAction.OpenShop }],
        });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ConversationOpensNoShop));
    }

    [Test]
    public void AChoiceOpeningAShopTheSpeakerDoesKeep_IsFine()
    {
        var w = World();
        w.Shops[1]!.Keeper = 2;
        var conv = w.Conversations[1]!;
        conv.SpeakerNpc = 2;
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "shop", Action = ConversationAction.OpenShop }],
        });

        Assert.That(Kinds(w), Does.Not.Contain(WorldIssueKind.ConversationOpensNoShop));
    }

    [Test]
    public void AChoiceOpeningQuestsFromAnNpcWithNone_IsFound()
    {
        var w = World();
        var conv = w.Conversations[1]!;
        conv.SpeakerNpc = 2;
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "quests", Action = ConversationAction.OpenQuests }],
        });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.ConversationOpensNoQuests));
    }

    /// <summary>A quest names its turn-in NPC, or falls back to its giver when it names none.</summary>
    [Test]
    public void AChoiceOpeningQuestsFromTheirGiver_IsFine()
    {
        var w = World();
        w.Quests[1]!.GiverNpc = 2;
        var conv = w.Conversations[1]!;
        conv.SpeakerNpc = 2;
        conv.RootNodeId = 1;
        conv.Nodes.Add(new ConversationNode
        {
            Id = 1,
            Choices = [new ConversationChoice { Label = "quests", Action = ConversationAction.OpenQuests }],
        });

        Assert.That(Kinds(w), Does.Not.Contain(WorldIssueKind.ConversationOpensNoQuests));
    }

    // ── Classes ──────────────────────────────────────────────────────────────

    [Test]
    public void AClassStartingWithAnUnauthoredItem_IsFound()
    {
        var w = World();
        w.Classes[1]!.StartingItems = [new ClassStartingItem { ItemNum = Absent, Quantity = 1 }];

        var issue = WorldCheck.Run(w).First(i => i.Kind == WorldIssueKind.ItemMissing);

        Assert.That(issue.OwnerKind, Is.EqualTo(WorldRecordKind.Class));
    }

    [Test]
    public void AClassStartingWithAnUnauthoredSpell_IsFound()
    {
        var w = World();
        w.Classes[1]!.StartingSpells = [Absent];

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.SpellMissing));
    }

    // ── What counts as a map being there ─────────────────────────────────────

    /// <summary>The case the padded world creates: a warp into one of the hundreds of slots a world is
    /// padded out to. The slot exists, so a range check passes it; nothing is there, so a player who steps
    /// on the tile arrives nowhere.</summary>
    [Test]
    public void AWarpIntoABlankPaddedSlot_IsFound()
    {
        var w = World();
        w.Maps[1]!.Tile[2, 2] = w.Maps[1]!.Tile[2, 2].WithGroundAttr(
            new TileAttr { Type = TileType.Warp, WarpMap = 5, WarpX = 1, WarpY = 1 });

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.WarpMapMissing));
    }

    [Test]
    public void ALinkToABlankPaddedSlot_IsFound()
    {
        var w = World();
        w.Maps[1]!.Up = 5;

        Assert.That(Kinds(w), Does.Contain(WorldIssueKind.LinkOutOfRange));
    }

    /// <summary>A map is authored by holding something, not by being named — a place can be fully painted
    /// and never titled, so naming is the one thing that must not be required.</summary>
    [Test]
    public void APaintedMapWithNoName_CountsAsThere()
    {
        var w = World();
        w.Maps[5]!.Tile[0, 0] = w.Maps[5]!.Tile[0, 0].WithGroundAttr(new TileAttr { Type = TileType.Blocked });
        w.Maps[1]!.Tile[2, 2] = w.Maps[1]!.Tile[2, 2].WithGroundAttr(
            new TileAttr { Type = TileType.Warp, WarpMap = 5, WarpX = 1, WarpY = 1 });

        Assert.That(WorldCheck.Run(w), Is.Empty);
    }

    /// <summary>Every way of putting something on a map counts, not only tiles.</summary>
    [TestCase("npc")]
    [TestCase("light")]
    [TestCase("music")]
    [TestCase("displayName")]
    [TestCase("greeting")]
    public void AMapHoldingAnythingAtAll_IsNotBlank(string what)
    {
        var map = new MapRecord(16, 12);
        switch (what)
        {
            case "npc": map.Npcs.Add(new MapNpcEntry(1, null, null)); break;
            case "light": map.Lights.Add(new PlacedLight(Guid.NewGuid(), 1, 1, new LightSpec())); break;
            case "music": map.Music = 3; break;
            case "displayName": map.DisplayName = "Harbour"; break;
            case "greeting": map.JoinSay = "Welcome."; break;
        }

        Assert.That(map.IsBlank, Is.False);
    }

    [Test]
    public void AFreshMapOfAnySize_IsBlank()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new MapRecord().IsBlank, Is.True);
            Assert.That(new MapRecord(64, 64).IsBlank, Is.True, "a resized map nobody painted is still empty");
        });
    }
}
