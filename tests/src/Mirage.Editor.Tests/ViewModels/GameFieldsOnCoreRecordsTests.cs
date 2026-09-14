using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// A game's own fields on one of the engine's records, authored on that record's own screen.
///
/// <para>🔴 <b>A record the engine owns has two halves, and they travel by different routes.</b> An
/// item's power is a property Core acts on: it rides the item's own packet, which normalizes what it is
/// sent. A game's fields on that same item are keys the engine has never heard of, so they ride the
/// generic record packet instead. Both halves are one record and one press of Save.</para>
///
/// <para>⚠ The half that fails silently is the offline one. A typed row builds a fresh record out of
/// the properties it knows about, so without the bag being carried across, an ordinary save would clear
/// every field a game added — and nothing would report anything.</para>
/// </summary>
[TestFixture]
public class GameFieldsOnCoreRecordsTests
{
    private string _dir = "";
    private string _restore = "";

    [SetUp]
    public void OpenAWorld()
    {
        _restore = EditorPaths.Data;
        _dir = Directory.CreateTempSubdirectory("mirage-gamefields-").FullName;
        Directory.CreateDirectory(Path.Combine(_dir, "items"));
        EditorPaths.OpenWorld(_dir);
    }

    [TearDown]
    public void CloseIt()
    {
        EditorPaths.OpenWorld(_restore);
        WorldFamilies.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>🔴 The fields a game declared on Items reach the item screen, captioned as the game
    /// named them.</summary>
    [Test]
    public void AGamesFieldsOnItems_ShowAsAFormUnderTheItem()
    {
        var vm = ItemEditor(Extended());

        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        Assert.Multiple(() =>
        {
            Assert.That(vm.GameFields, Is.Not.Null);
            Assert.That(vm.GameFields!.HasFields, Is.True);
            Assert.That(vm.GameFields.IsShown, Is.True);
            Assert.That(vm.GameFields.Form!.Fields.Select(f => f.Key),
                Is.EquivalentTo(new[] { "levelReq", "teaches" }));
        });
    }

    /// <summary>A world whose game added nothing shows no section at all, rather than an empty
    /// heading.</summary>
    [Test]
    public void AWorldWithNoGameFields_ShowsNothing()
    {
        var vm = ItemEditor(CoreRegistry.CoreOnly.Schema);

        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        Assert.Multiple(() =>
        {
            Assert.That(vm.GameFields!.HasFields, Is.False);
            Assert.That(vm.GameFields.IsShown, Is.False);
        });
    }

    /// <summary>The form opens on whatever is authored, and follows the selection.</summary>
    [Test]
    public void TheFormReadsTheOpenRecordAndFollowsTheSelection()
    {
        var vm = ItemEditor(Extended(), Authored());

        vm.SelectedItem = vm.Items.First(i => i.Index == 1);
        Assert.That(Value(vm, "levelReq"), Is.EqualTo(12L), "what the first item carries");

        vm.SelectedItem = vm.Items.First(i => i.Index == 2);

        Assert.Multiple(() =>
        {
            Assert.That(vm.GameFields!.Num, Is.EqualTo(2));
            Assert.That(Value(vm, "levelReq"), Is.EqualTo(40L), "and the second");
        });
    }

    /// <summary>Editing one counts as editing the record: Save lights up for an item whose own
    /// properties nobody touched.</summary>
    [Test]
    public void EditingAGameField_MakesTheRecordDirty()
    {
        var vm = ItemEditor(Extended(), Authored());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);

        Assert.That(vm.IsSelectedDirty, Is.False, "nothing has been touched yet");

        Set(vm, "levelReq", 33L);

        Assert.Multiple(() =>
        {
            Assert.That(vm.GameFields!.IsDirty, Is.True);
            Assert.That(vm.IsSelectedDirty, Is.True, "the record is dirty, not just the form");
            Assert.That(vm.HasAnyDirty, Is.True);
        });
    }

    /// <summary>And Save writes it, on a record whose own rows never changed.</summary>
    [Test]
    public async Task SavingWritesTheGameFields()
    {
        var vm = ItemEditor(Extended(), Authored());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);
        Set(vm, "levelReq", 33L);

        await vm.SaveCommand.ExecuteAsync(null);

        var onDisk = await Reloaded();

        Assert.Multiple(() =>
        {
            Assert.That(onDisk.OfflineItems[1].Attributes["levelReq"].AsLong(), Is.EqualTo(33L));
            Assert.That(onDisk.OfflineItems[1].Name, Is.EqualTo("Rusted Dagger"),
                "and the engine's own half is still there");
            Assert.That(vm.IsSelectedDirty, Is.False, "the form is clean again");
        });
    }

    /// <summary>⚠ <b>An ordinary save keeps what a game hung on the record.</b> A typed row is built
    /// from the properties the engine acts on and holds none of a game's, so the fields already on disk
    /// are carried across rather than written over with nothing.</summary>
    [Test]
    public async Task SavingAnItemsOwnPropertiesKeepsTheGamesFields()
    {
        var vm = ItemEditor(Extended(), Authored());
        var row = vm.Items.First(i => i.Index == 1);
        vm.SelectedItem = row;

        row.Name = "Polished Dagger";
        await vm.SaveCommand.ExecuteAsync(null);

        var onDisk = await Reloaded();

        Assert.Multiple(() =>
        {
            Assert.That(onDisk.OfflineItems[1].Name, Is.EqualTo("Polished Dagger"));
            Assert.That(onDisk.OfflineItems[1].Attributes["levelReq"].AsLong(), Is.EqualTo(12L),
                "the game's field survived a save that never carried it");
        });
    }

    /// <summary>Discard puts the form back to what the record holds.</summary>
    [Test]
    public async Task DiscardingPutsTheGameFieldsBack()
    {
        var vm = ItemEditor(Extended(), Authored());
        vm.SelectedItem = vm.Items.First(i => i.Index == 1);
        Set(vm, "levelReq", 99L);

        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(Value(vm, "levelReq"), Is.EqualTo(12L));
            Assert.That(vm.IsSelectedDirty, Is.False);
        });
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>The schema a world running MSR reports: the engine's Items, with the two fields that
    /// game adds to them.</summary>
    private static RecordSchema Extended()
    {
        var families = CoreRegistry.CoreOnly.Schema.Families
            .Select(f => f.Id == CoreRecordFamilies.Items
                ? f with
                {
                    Fields =
                    [
                        new FieldDescriptor { Key = "levelReq", LabelKey = "Level needed", Kind = FieldKind.Integer, Max = 255 },
                        new FieldDescriptor { Key = "teaches", LabelKey = "Spell written on it", Kind = FieldKind.Integer },
                    ],
                }
                : f)
            .ToList();

        return new RecordSchema { Families = families };
    }

    /// <summary>Two items, each carrying one of the game's fields.</summary>
    private static ItemRecord[] Authored()
    {
        var items = new[]
        {
            new ItemRecord(),
            new ItemRecord { Name = "Rusted Dagger" },
            new ItemRecord { Name = "Oak Shield" },
        };
        items[1].Attributes.Set("levelReq", AttributeValue.From(12L));
        items[2].Attributes.Set("levelReq", AttributeValue.From(40L));
        return items;
    }

    private ItemEditorViewModel ItemEditor(RecordSchema schema, ItemRecord[]? items = null)
    {
        WorldFamilies.Adopt(schema);

        var data = new EditorDataService();
        items ??= [new ItemRecord(), new ItemRecord { Name = "Rusted Dagger" }, new ItemRecord { Name = "Oak Shield" }];
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineItems))!.SetValue(data, items);

        // Written where the editor will read them back from, so a save can be checked on disk rather
        // than only in memory.
        for (int i = 1; i < items.Length; i++)
        {
            File.WriteAllText(Path.Combine(_dir, "items", $"item{i}.json"),
                System.Text.Json.JsonSerializer.Serialize(items[i]));
        }

        var vm = new ItemEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    /// <summary>The world folder, read fresh — what another editor opening it would see.</summary>
    private static async Task<EditorDataService> Reloaded()
    {
        var data = new EditorDataService();
        await data.LoadOfflineAsync();
        return data;
    }

    private static long Value(ItemEditorViewModel vm, string key) =>
        vm.GameFields!.Record.TryGet(key, out AttributeValue held) ? held.AsLong() : 0L;

    private static void Set(ItemEditorViewModel vm, string key, long value) =>
        vm.GameFields!.Form!.Fields.First(f => f.Key == key).NumberValue = value;
}
