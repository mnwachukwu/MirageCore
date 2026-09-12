using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The family table names folders and filenames that already exist in every world on disk.
///
/// <para><b>Getting one wrong is silent.</b> A server pointed at a folder nothing wrote reads an empty
/// family and pads it out to its ceiling with blanks — a world that loads, starts, and has nothing in
/// it. No exception, no log line, and the authored files are still sitting in the folder next door. So
/// the strings are asserted literally here rather than derived in the test, which would only prove the
/// derivation agrees with itself.</para>
/// </summary>
[TestFixture]
public class CoreRecordFamiliesTests
{
    /// <summary>Every world family, with the folder and filename its records are stored under.</summary>
    private static readonly (string Id, string Directory, string FirstFile)[] OnDisk =
    [
        (CoreRecordFamilies.Maps, "maps", "map1.json"),
        (CoreRecordFamilies.MapGroups, "map_groups", "map_group1.json"),
        (CoreRecordFamilies.Items, "items", "item1.json"),
        (CoreRecordFamilies.Npcs, "npcs", "npc1.json"),
        (CoreRecordFamilies.Shops, "shops", "shop1.json"),
        (CoreRecordFamilies.Quests, "quests", "quest1.json"),
        (CoreRecordFamilies.Conversations, "conversations", "conversation1.json"),
    ];

    [TestCaseSource(nameof(OnDisk))]
    public void AFamilyNamesTheFolderAndFileItsRecordsAreActuallyIn((string Id, string Directory, string FirstFile) expected)
    {
        var family = CoreRecordFamilies.Get(expected.Id);

        Assert.Multiple(() =>
        {
            Assert.That(family.EffectiveDirectory, Is.EqualTo(expected.Directory));
            Assert.That(family.FileNameFor(1), Is.EqualTo(expected.FirstFile));
        });
    }

    [Test]
    public void TheTableCoversEveryFamilyAndNothingElse()
    {
        Assert.That(CoreRecordFamilies.World.Select(f => f.Id),
                    Is.EquivalentTo(OnDisk.Select(e => e.Id)));
    }

    [Test]
    public void NoTwoFamiliesShareAnIdOrAFolder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CoreRecordFamilies.World.Select(f => f.Id).Distinct().Count(),
                        Is.EqualTo(CoreRecordFamilies.World.Count));
            Assert.That(CoreRecordFamilies.World.Select(f => f.EffectiveDirectory).Distinct().Count(),
                        Is.EqualTo(CoreRecordFamilies.World.Count));
        });
    }

    /// <summary>A row left at its defaults would read as a family with no folder and no room, which the
    /// persistence layer would act on rather than reject.</summary>
    [Test]
    public void NoRowIsLeftAtItsDefaults()
    {
        Assert.Multiple(() =>
        {
            foreach (var family in CoreRecordFamilies.World)
            {
                Assert.That(family.Id, Is.Not.Empty);
                Assert.That(family.EffectiveDirectory, Is.Not.Empty);
                Assert.That(family.EffectiveFilePrefix, Is.Not.Empty);
                Assert.That(family.DefaultLimit, Is.GreaterThan(0), family.Id);
            }
        });
    }

    /// <summary>Maps and map groups are fetched one file at a time; everything else is read as a whole
    /// numbered set at boot.</summary>
    [Test]
    public void OnlyMapsAndMapGroupsLoadOneFileAtATime()
    {
        Assert.That(CoreRecordFamilies.World.Where(f => f.LoadsIndividually).Select(f => f.Id),
                    Is.EquivalentTo(new[] { CoreRecordFamilies.Maps, CoreRecordFamilies.MapGroups }));
    }

    [Test]
    public void AnUnknownIdIsRefusedRatherThanGuessedAt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CoreRecordFamilies.Find("Creatures"), Is.Null);
            Assert.That(() => CoreRecordFamilies.Get("Creatures"), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    /// <summary>Ids are settings keys and folders are paths, so a lookup that ignored case would find a
    /// row for a spelling nothing on disk uses.</summary>
    [Test]
    public void LookupIsCaseSensitive()
    {
        Assert.That(CoreRecordFamilies.Find("items"), Is.Null, "the id is \"Items\"");
    }
}
