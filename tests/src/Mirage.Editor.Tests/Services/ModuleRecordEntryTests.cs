using Mirage.Editor.Services;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// Naming a game's records in a picker, from wherever this session gets them.
///
/// <para>A <c>RecordRef</c> field on one family's form lists ANOTHER family's records by name, so an
/// author picks "Ember" rather than typing 4. Core's families answer from the name index in the login
/// handshake; a game's family has no such index and answers from the records themselves — the folder's
/// offline, the server's bulk fetch online. A picker with no names is still settable by slot number, so
/// the failure is quiet, which is the reason to pin it.</para>
/// </summary>
[TestFixture]
public class ModuleRecordEntryTests
{
    private static readonly RecordFamily Species = new()
    {
        Id = "Species",
        Directory = "species",
        DefaultLimit = 3,
        LimitIsFixed = true,
        NameFieldKey = "name",
    };

    private static EditorDataService Online()
    {
        var data = new EditorDataService();
        // What connecting does: the handshake's name index for Core's families, and nothing for a game's.
        data.LoadOnline(new EditorDataPacket());
        return data;
    }

    private static (int, AttributeBag)[] Vulpine() =>
        [(2, new AttributeBag().Set("name", "Vulpine"))];

    [Test]
    public void BeforeTheBulkFetch_TheFamilyOffersNoNames()
    {
        Assert.That(Online().EntriesFor(Species.Id), Is.Empty);
    }

    [Test]
    public void AfterTheBulkFetch_EachSlotIsNamed()
    {
        var data = Online();

        data.AdoptOnlineModuleRecords(Species, Vulpine());

        var entries = data.EntriesFor(Species.Id);
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(Species.DefaultLimit + 1));
            Assert.That(entries[0].Name, Is.EqualTo("(none)"), "a reference can always be cleared");
            Assert.That(entries[2].Name, Is.EqualTo("Vulpine"));
            Assert.That(entries[1].Name, Is.Empty, "an unauthored slot is still offered, unnamed");
        });
    }

    /// <summary>The name comes off whichever field the family nominated, which is the only thing the
    /// engine reads out of a game's record.</summary>
    [Test]
    public void AFamilyThatNominatesNoNameField_OffersSlotsWithoutNames()
    {
        var data = Online();
        var unnamed = Species with { Id = "Moves", NameFieldKey = "" };

        data.AdoptOnlineModuleRecords(unnamed, [(1, new AttributeBag().Set("name", "Ember"))]);

        Assert.That(data.EntriesFor("Moves")[1].Name, Is.Empty);
    }

    [Test]
    public void AFamilyNothingHasFetched_OffersNothing()
    {
        var data = Online();
        data.AdoptOnlineModuleRecords(Species, Vulpine());

        Assert.That(data.EntriesFor("Moves"), Is.Empty);
    }

    // A slot number past this world's ceiling has nowhere to land; the entry list is the ceiling's size.
    [Test]
    public void ASlotAboveTheCeiling_IsDropped()
    {
        var data = Online();

        data.AdoptOnlineModuleRecords(Species, [(99, new AttributeBag().Set("name", "Nowhere"))]);

        Assert.That(data.EntriesFor(Species.Id).Any(e => e.Name == "Nowhere"), Is.False);
    }

    /// <summary>What the session holds for a family follows the session: connected, the server's records;
    /// offline, the folder's. One answer per session, never a mix.</summary>
    [Test]
    public void OfflineTheFolderAnswers_AndOnlineTheServerDoes()
    {
        var offline = new EditorDataService();
        var online = Online();
        online.AdoptOnlineModuleRecords(Species, Vulpine());

        Assert.Multiple(() =>
        {
            Assert.That(offline.ModuleRecordsFor(Species), Is.Empty, "no folder is open");
            Assert.That(online.ModuleRecordsFor(Species)[2]["name"].AsText(), Is.EqualTo("Vulpine"));
        });
    }
}
