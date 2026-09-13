using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
namespace Mirage.Editor.Tests.RoundTrip;

/// <summary>
/// Locks the MapGroup editor's round-trips: the group row's packet apply/save, the tri-state
/// mapping, and the map's new MapGroup reference traveling through the shared map packet in both
/// directions.
/// </summary>
[TestFixture]
public class MapGroupRoundTripTests
{
    private static MapGroupRowViewModel Row() =>
        new(3, new MapGroupRecord { Index = 3 }, () => [], isLoaded: false);

    private static UpdateMapGroupPacket FullPacket() => new()
    {
        GroupNum = 3,
        Name = "north",
        DisplayName = "Northern Reaches",
        Music = 7,
        PlayersPassThrough = true,
        GreetingSpeaker = "Innkeeper",
        JoinSay = "Welcome.",
        LeaveSay = "Farewell.",
        Indoors = true,
        AlwaysDark = null,     // "inherit" survives the trip as null
        ExitMap = 9,
        ExitX = 2,
        ExitY = 5,
    };

    [Test]
    public void ApplyPacket_ThenToRecord_RoundTripsEveryField()
    {
        var vm = Row();
        vm.ApplyPacket(FullPacket());
        var r = vm.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("north"));
            Assert.That(r.DisplayName, Is.EqualTo("Northern Reaches"));
            Assert.That(r.Music, Is.EqualTo(7));
            Assert.That(r.PlayersPassThrough, Is.True);
            Assert.That(r.GreetingSpeaker, Is.EqualTo("Innkeeper"));
            Assert.That(r.JoinSay, Is.EqualTo("Welcome."));
            Assert.That(r.LeaveSay, Is.EqualTo("Farewell."));
            Assert.That(r.Indoors, Is.True);
            Assert.That(r.AlwaysDark, Is.Null, "a null (inherit) bool must round-trip as null, not false");
            Assert.That(r.ExitMap, Is.EqualTo(9));
            Assert.That(r.ExitX, Is.EqualTo(2));
            Assert.That(r.ExitY, Is.EqualTo(5));
            Assert.That(r.Index, Is.EqualTo(3));
        });
    }

    [Test]
    public void ApplyPacket_DoesNotMarkDirty()
    {
        var vm = Row();
        vm.ApplyPacket(FullPacket());

        Assert.That(vm.IsDirty, Is.False, "loading a group from the server must not mark it dirty");
    }

    [Test]
    public void PlayersPassThrough_TriState_KeepsInheritApartFromAnExplicitNo()
    {
        var vm = Row();

        vm.ApplyPacket(FullPacket() with { PlayersPassThrough = null });
        Assert.That(vm.PlayersPassThrough, Is.Null, "a null group value stays null rather than collapsing to false");

        vm.PlayersPassThrough = false;
        Assert.That(vm.IsDirty, Is.True, "saying no explicitly marks the row dirty");
    }

    [Test]
    public void MapPacket_RoundTripsMapGroupAndNullableFields()
    {
        var map = new MapRecord { MapGroup = 5, PlayersPassThrough = null, Indoors = false, AlwaysDark = null };

        // Editor → wire (the editor authors RAW nullable fields via BuildSaveMapPacket).
        var wire = EditorDataService.BuildSaveMapPacket(7, map).Map;
        Assert.That(wire.MapGroup, Is.EqualTo(5));

        // Wire → editor record.
        var back = EditorDataService.MapRecordFromPacket(wire);
        Assert.Multiple(() =>
        {
            Assert.That(back.MapGroup, Is.EqualTo(5));
            Assert.That(back.PlayersPassThrough, Is.Null);
            Assert.That(back.Indoors, Is.False, "an explicit false must NOT collapse to null");
            Assert.That(back.AlwaysDark, Is.Null, "a null (inherit) must NOT collapse to false");
        });
    }
}
