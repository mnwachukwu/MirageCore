using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Services;

/// <summary>
/// A connected editor's slot range belongs to the SERVER. The editor's offline folder is a different
/// world with its own ceiling, and must not bound what a connected picker offers: too small drops the
/// server's top slots, too large invents ones the server would refuse to save.
/// </summary>
[TestFixture]
public class OnlineRecordLimitsTests
{
    private static EditorDataPacket.NameEntry[] Slots(int count, params (int Num, string Name)[] named)
    {
        var all = Enumerable.Range(1, count)
            .Select(i => new EditorDataPacket.NameEntry(i, "")).ToArray();
        foreach (var (num, name) in named) all[num - 1] = new EditorDataPacket.NameEntry(num, name);
        return all;
    }

    private static EditorDataService Online(EditorDataPacket pkt, RecordLimits? limits = null)
    {
        var data = new EditorDataService();
        data.LoadOnline(pkt, limits);
        return data;
    }

    [Test]
    public void ASlotAboveWhatTheOfflineFolderHoldsIsStillOffered()
    {
        // The offline folder is empty here, so only the server's list can be bounding this.
        var data = Online(new EditorDataPacket { Items = Slots(500, (500, "Last Item")) });

        var entries = data.LiveItemEntries;
        Assert.That(entries.Length, Is.EqualTo(501), "0 plus the server's 500 slots");
        Assert.That(entries[500].Name, Is.EqualTo("Last Item"));
    }

    [Test]
    public void TheListStopsAtTheServersLastSlot_NotTheEditorsOwn()
    {
        var data = Online(new EditorDataPacket { Items = Slots(10, (10, "Tenth")) });

        Assert.That(data.LiveItemEntries.Length, Is.EqualTo(11));
    }

    [Test]
    public void SlotZeroStaysTheNoneRow()
    {
        var data = Online(new EditorDataPacket { Items = Slots(5) });

        Assert.That(data.LiveItemEntries[0].Name, Is.EqualTo("(none)"));
    }

    [Test]
    public void EveryFamilyIsSizedFromItsOwnList()
    {
        var data = Online(new EditorDataPacket
        {
            Items = Slots(300),
            Npcs = Slots(120),
            Spells = Slots(270),
            Maps = Slots(1000),
        });

        Assert.That(data.LiveItemEntries.Length, Is.EqualTo(301));
        Assert.That(data.LiveNpcEntries.Length, Is.EqualTo(121));
        Assert.That(data.LiveSpellEntries.Length, Is.EqualTo(271));
        Assert.That(data.LiveMapEntries.Length, Is.EqualTo(1001));
    }

    [Test]
    public void AnEmptyFamilyIsAnEmptyList_NotACrash()
    {
        var data = Online(new EditorDataPacket { Items = Slots(1) });

        Assert.That(data.LiveNpcEntries, Is.Empty);
    }

    [Test]
    public void TheServersLimitsAreTakenFromItsGreeting()
    {
        var limits = RecordLimits.Default with { Items = 500, Maps = 2000 };

        var data = Online(new EditorDataPacket { Items = Slots(1) }, limits);

        Assert.That(data.Limits.Items, Is.EqualTo(500));
        Assert.That(data.Limits.Maps, Is.EqualTo(2000));
    }

    [Test]
    public void AServerTooOldToGreetFallsBackToTheProtocolDefaults()
    {
        var data = Online(new EditorDataPacket { Items = Slots(1) }, limits: null);

        Assert.That(data.Limits.Items, Is.EqualTo(RecordLimits.Default.Items));
    }

    [Test]
    public void DisconnectingReturnsToTheProtocolDefaults()
    {
        var data = Online(new EditorDataPacket { Items = Slots(1) },
            RecordLimits.Default with { Items = 500 });

        data.ClearOnline();

        Assert.That(data.Limits.Items, Is.EqualTo(RecordLimits.Default.Items));
        Assert.That(data.IsOnline, Is.False);
    }
}
