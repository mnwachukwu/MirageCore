using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// Downloading a world and uploading it back.
///
/// <para>The property that carries the feature is the loop closing: write a world to a folder, read the
/// folder, compare it against the world it came from, and there is nothing to do. Anything that breaks it
/// — a field the writer drops, a blank the reader invents, a runtime value the diff mistakes for authored
/// content — shows up as phantom changes, which is exactly what would be uploaded over somebody's
/// server.</para>
///
/// <para>The three buckets are then pinned one at a time, and hardest of all the removals: they are the
/// only ones that destroy something.</para>
/// </summary>
[TestFixture]
public class WorldTransferTests
{
    private string _dir = "";

    [SetUp]
    public void CreateScratchDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveScratchDir()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Small enough to write in a test, wide enough to cover every family the transfer walks.
    private static readonly RecordLimits Small = new()
    {
        Items = 8, Npcs = 8, Shops = 8, Spells = 8, Quests = 8, Conversations = 8, Maps = 8, MapGroups = 8,
    };

    private static WorldSnapshot Blank(RecordLimits? limits = null)
    {
        var l = limits ?? Small;
        return new WorldSnapshot
        {
            Limits = l,
            Items = Fill<ItemRecord>(l.Items),
            Npcs = Fill<NpcRecord>(l.Npcs),
            Shops = Fill<ShopRecord>(l.Shops),
            Spells = Fill<SpellRecord>(l.Spells),
            Classes = Fill<ClassRecord>(Constants.MaxClasses),
            Quests = Fill<QuestRecord>(l.Quests),
            Conversations = Fill<ConversationRecord>(l.Conversations),
            Maps = Fill<MapRecord>(l.Maps),
            MapGroups = [.. Enumerable.Range(0, l.MapGroups + 1).Select(i => new MapGroupRecord { Index = i })],
        };
    }

    private static T[] Fill<T>(int max) where T : new() => [.. Enumerable.Range(0, max + 1).Select(_ => new T())];

    /// <summary>A world with something authored in every family, so nothing passes by being empty.</summary>
    private static WorldSnapshot Authored()
    {
        var w = Blank();
        w.Items[1] = new ItemRecord { Name = "Bronze Sword", Type = ItemType.Weapon, Power = 12, Pic = 3 };
        w.Items[2] = new ItemRecord { Name = "Gold", Type = ItemType.Currency, Pic = 9 };
        w.Npcs[1] = new NpcRecord { Name = "Cave Troll", Sprite = 42, Str = 20, Behavior = NpcBehavior.AttackOnSight };
        w.Shops[1] = new ShopRecord { Name = "Smithy", Keeper = 1 };
        w.Spells[1] = new SpellRecord { Name = "Ember", Type = SpellType.SubHp, VitalAmount = 15 };
        w.Classes[1] = new ClassRecord { Name = "Warrior", Str = 8, Def = 6, Spd = 3, Int = 3 };
        w.Quests[1] = new QuestRecord { Name = "The Missing Cart" };
        w.Conversations[1] = new ConversationRecord { Name = "Innkeeper", SpeakerNpc = 1 };
        w.MapGroups[1] = new MapGroupRecord { Index = 1, Name = "harbour", DisplayName = "The Harbour", Music = 4 };

        var map = new MapRecord
        {
            Name = "harbour-1", DisplayName = "Drowned Port", Music = 2, MapGroup = 1,
            Up = 2, Down = 3, Left = 4, Right = 5, AlwaysLit = true, Indoors = false,
        };
        map.Tile[1, 1] = new TileRecord { Type = TileType.Blocked }.WithArt(LayerType.Ground, [163]);
        map.Tile[2, 2] = new TileRecord { Type = TileType.Warp, WarpMap = 6, WarpX = 3, WarpY = 4 }.WithArt(LayerType.Ground, [2]);
        map.Npcs.Add(new MapNpcEntry(1, 5, 6));
        w.Maps[1] = map;
        return w;
    }

    // ── The loop closes ──────────────────────────────────────────────────────

    [Test]
    public async Task WriteThenRead_LeavesNothingToUpload()
    {
        var world = Authored();
        await WorldTransfer.WriteFolderAsync(_dir, world);
        var back = await WorldTransfer.ReadFolderAsync(_dir);

        var diff = WorldTransfer.Compare(back, world);
        Assert.That(diff.Changes, Is.Empty,
            "A world written and read back differs from itself: "
            + string.Join(", ", diff.Changes.Select(c => $"{c.Section}#{c.Num} {c.Kind}")));
        Assert.That(diff.OverCeiling, Is.Zero);
    }

    [Test]
    public async Task WriteThenRead_KeepsTheWorldsOwnCeilings()
    {
        await WorldTransfer.WriteFolderAsync(_dir, Blank());
        var back = await WorldTransfer.ReadFolderAsync(_dir);

        Assert.Multiple(() =>
        {
            Assert.That(back.Limits.Items, Is.EqualTo(Small.Items));
            Assert.That(back.Limits.Maps, Is.EqualTo(Small.Maps));
            Assert.That(File.Exists(Path.Combine(_dir, WorldManifest.FileName)), Is.True);
        });
    }

    /// <summary>Blank slots get no file at all. A new world would otherwise arrive as thousands of empty
    /// ones, and a missing file already reads back as a blank record.</summary>
    [Test]
    public async Task BlankSlots_AreNotWrittenToDisk()
    {
        int written = await WorldTransfer.WriteFolderAsync(_dir, Authored());

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(10), "one record per authored slot, and no others");
            Assert.That(Directory.GetFiles(Path.Combine(_dir, "items")), Has.Length.EqualTo(2));
            Assert.That(Directory.GetFiles(Path.Combine(_dir, "maps")), Has.Length.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_dir, "items", "item3.json")), Is.False);
        });
    }

    [Test]
    public async Task AMissingFile_ReadsBackAsABlankRecord()
    {
        await WorldTransfer.WriteFolderAsync(_dir, Authored());
        var back = await WorldTransfer.ReadFolderAsync(_dir);

        // Slot 3 was never written, and slot 3 of a blank world is the same thing.
        var diff = WorldTransfer.Compare(back, Blank());
        Assert.That(diff.Of(WorldChangeKind.Added).Select(c => (c.Section, c.Num)),
            Does.Not.Contain(("Items", 3)));
    }

    // ── The three buckets ────────────────────────────────────────────────────

    [Test]
    public void ARecordOnlyInTheFolder_IsAnAddition()
    {
        var folder = Authored();
        var diff = WorldTransfer.Compare(folder, Blank());

        var added = diff.Of(WorldChangeKind.Added).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(diff.Count(WorldChangeKind.Changed), Is.Zero);
            Assert.That(diff.Count(WorldChangeKind.Removed), Is.Zero);
            Assert.That(added.Select(c => c.Section),
                Is.EquivalentTo(new[] { "Maps", "MapGroups", "Items", "Items", "NPCs", "Shops", "Spells", "Classes", "Quests", "Conversations" }));
            Assert.That(added.First(c => c.Section == "Maps").Name, Is.EqualTo("harbour-1"));
        });
    }

    [Test]
    public void ARecordDifferingOnBothSides_IsAChange()
    {
        var server = Authored();
        var folder = Authored();
        folder.Items[1].Power = 99;

        var diff = WorldTransfer.Compare(folder, server);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Changes, Has.Count.EqualTo(1));
            Assert.That(diff.Changes[0].Kind, Is.EqualTo(WorldChangeKind.Changed));
            Assert.That(diff.Changes[0].Section, Is.EqualTo("Items"));
            Assert.That(diff.Changes[0].Num, Is.EqualTo(1));
            Assert.That(diff.Changes[0].Name, Is.EqualTo("Bronze Sword"));
        });
    }

    /// <summary>The bucket that destroys something. A folder blank where the server is authored reads as a
    /// removal, and it is named after the server's copy — the thing that would be lost.</summary>
    [Test]
    public void ARecordOnlyOnTheServer_IsARemoval_NamedAfterWhatWouldBeLost()
    {
        var diff = WorldTransfer.Compare(Blank(), Authored());

        var removed = diff.Of(WorldChangeKind.Removed).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(diff.Count(WorldChangeKind.Added), Is.Zero);
            Assert.That(diff.Count(WorldChangeKind.Changed), Is.Zero);
            Assert.That(removed, Has.Count.EqualTo(10));
            Assert.That(removed.First(c => c.Section == "NPCs").Name, Is.EqualTo("Cave Troll"));
        });
    }

    // ── What is not a change ─────────────────────────────────────────────────

    /// <summary>The server stamps its own revision on every save, so two copies of one map differ in it
    /// constantly. No upload can carry it, so it is not a difference.</summary>
    [Test]
    public void AMapRevision_IsNotAChange()
    {
        var server = Authored();
        var folder = Authored();
        server.Maps[1].Revision = 41;
        folder.Maps[1].Revision = 7;

        Assert.That(WorldTransfer.Compare(folder, server).Changes, Is.Empty);
    }

    // ── The ceiling ──────────────────────────────────────────────────────────

    /// <summary>A record above the server's ceiling has nowhere to go. Counted and stated: silently
    /// skipping it would read as an upload that took everything.</summary>
    [Test]
    public void ARecordAboveTheServersCeiling_IsReportedRatherThanSkipped()
    {
        var folder = Blank(Small);
        folder.Items[7] = new ItemRecord { Name = "Late Addition", Pic = 4 };

        var server = Blank(Small with { Items = 4 });
        var diff = WorldTransfer.Compare(folder, server);

        Assert.Multiple(() =>
        {
            Assert.That(diff.OverCeiling, Is.EqualTo(1));
            Assert.That(diff.Changes, Is.Empty);
            Assert.That(diff.IsEmpty, Is.False, "something was left behind, so there is still news to give");
        });
    }

    [Test]
    public void ABlankSlotAboveTheServersCeiling_IsNotReported()
    {
        var diff = WorldTransfer.Compare(Blank(Small), Blank(Small with { Items = 4 }));

        Assert.Multiple(() =>
        {
            Assert.That(diff.OverCeiling, Is.Zero);
            Assert.That(diff.IsEmpty, Is.True);
        });
    }
}
