using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Each ceiling has to come back on the family it was typed for.
///
/// <para>The dialog builds a row per family and reads the values back to rebuild
/// <see cref="RecordLimits"/>. Matching the two by position works right up until a row is added,
/// removed or reordered — and then every ceiling after the moved one lands on the wrong family, with
/// nothing to see: the dialog looks right, the numbers are all plausible, and the world quietly gets
/// the item ceiling applied to its NPCs.</para>
/// </summary>
[TestFixture]
public class WorldSettingsRowTests
{
    private static WorldSettingsDialogViewModel Open(RecordLimits limits) =>
        new(new WorldManifest { Name = "w", Records = limits }, isOnline: false);

    private static WorldManifest Confirmed(WorldSettingsDialogViewModel vm)
    {
        WorldManifest? result = null;
        vm.Confirmed += m => result = m;
        vm.ConfirmCommand.Execute(null);
        return result!;
    }

    private static RecordLimits ConfirmedLimits(WorldSettingsDialogViewModel vm) => Confirmed(vm).Records;

    /// <summary>Every configurable ceiling is given a different value, so a swapped pair cannot look
    /// correct by coincidence.</summary>
    private static readonly RecordLimits Distinct = new()
    {
        Maps = 101, MapGroups = 102, Items = 103, Npcs = 104,
        Shops = 105, Quests = 107, Conversations = 108,
    };

    [Test]
    public void EveryCeilingComesBackOnTheFamilyItWasTypedFor()
    {
        var back = ConfirmedLimits(Open(Distinct));

        Assert.Multiple(() =>
        {
            Assert.That(back.Maps, Is.EqualTo(101));
            Assert.That(back.MapGroups, Is.EqualTo(102));
            Assert.That(back.Items, Is.EqualTo(103));
            Assert.That(back.Npcs, Is.EqualTo(104));
            Assert.That(back.Shops, Is.EqualTo(105));
            Assert.That(back.Quests, Is.EqualTo(107));
            Assert.That(back.Conversations, Is.EqualTo(108));
        });
    }

    /// <summary>Editing one row moves that family's ceiling and leaves every other alone.</summary>
    [Test]
    public void EditingOneRowMovesOnlyThatFamily()
    {
        var vm = Open(Distinct);
        var npcRow = vm.Rows.First(r => r.FamilyId == CoreRecordFamilies.Npcs);

        npcRow.Value = 555;
        var back = ConfirmedLimits(vm);

        Assert.Multiple(() =>
        {
            Assert.That(back.Npcs, Is.EqualTo(555));
            Assert.That(back.Items, Is.EqualTo(103), "the row before it");
            Assert.That(back.Shops, Is.EqualTo(105), "the row after it");
        });
    }

    [Test]
    public void EveryRowNamesAFamilyThatExists()
    {
        Assert.Multiple(() =>
        {
            foreach (var row in Open(Distinct).Rows)
            {
                Assert.That(CoreRecordFamilies.Find(row.FamilyId), Is.Not.Null, row.FamilyId);
            }
        });
    }

    [Test]
    public void TheDialogOffersEveryConfigurableFamily()
    {
        Assert.That(Open(Distinct).Rows.Select(r => r.FamilyId),
                    Is.EquivalentTo(CoreRecordFamilies.World.Where(f => !f.LimitIsFixed).Select(f => f.Id)));
    }

    /// <summary>The dialog edits three things and must not touch the rest of the manifest. A world's
    /// authored appearance roster is not something a settings dialog should be able to delete by not
    /// mentioning it — and a manifest rebuilt from its parts does exactly that, silently.</summary>
    [Test]
    public void SavingKeepsThePartsOfTheWorldThisDialogDoesNotEdit()
    {
        var authored = new WorldManifest
        {
            Name = "w",
            Records = Distinct,
            Appearances =
            [
                new CharacterAppearance { Name = "Villager", Sprite = 3, SpriteSheet = 0 },
                new CharacterAppearance { Name = "Knight", Sprite = 17, SpriteSheet = 2 },
            ],
        };

        var vm = new WorldSettingsDialogViewModel(authored, isOnline: false);
        vm.WorldName = "renamed";
        var back = Confirmed(vm);

        Assert.Multiple(() =>
        {
            Assert.That(back.Appearances, Has.Count.EqualTo(2));
            Assert.That(back.Appearances[0].Name, Is.EqualTo("Villager"));
            Assert.That(back.Appearances[1].Sprite, Is.EqualTo(17));
            Assert.That(back.Appearances[1].SpriteSheet, Is.EqualTo(2));
            Assert.That(back.Name, Is.EqualTo("renamed"), "the fields it DOES edit still change");
        });
    }

    [Test]
    public void ACeilingAboveWhatAWorldTakesIsBroughtBackDown()
    {
        var vm = Open(Distinct);
        vm.Rows.First(r => r.FamilyId == CoreRecordFamilies.Items).Value = RecordLimits.Ceiling + 5_000;

        Assert.That(ConfirmedLimits(vm).Items, Is.LessThanOrEqualTo(RecordLimits.Ceiling));
    }
}
