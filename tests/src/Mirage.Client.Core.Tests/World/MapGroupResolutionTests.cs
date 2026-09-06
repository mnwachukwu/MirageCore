using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.World;

/// <summary>MapGroup is an independent client-cached def: the client holds the group and resolves a
/// map's effective inheritable values against it on demand (ClientState.*Of), the client-side mirror of the
/// server's GameWorld.*Of. These lock (a) the override > inherit > default resolution + null-group safety,
/// and (b) that a live UpdateMapGroup changes what a member map resolves to with NO map re-send — the whole
/// point of the redesign (a group edit reaches online players without touching any map).</summary>
[TestFixture]
public class MapGroupResolutionTests
{
    static readonly MethodInfo HandleSend = typeof(ClientPacketHandler)
        .GetMethod("HandleSendMapGroups", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static readonly MethodInfo HandleUpdate = typeof(ClientPacketHandler)
        .GetMethod("HandleUpdateMapGroup", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The handlers touch neither sender nor mapCache, so both are null! (per the client-test convention).
    static void Apply(ClientState state, MethodInfo handler, object packet)
        => handler.Invoke(new ClientPacketHandler(state, null!, null!), new[] { packet });

    // ── Resolution: the map's own value wins, else the group's, else the hard default ──

    [Test]
    public void InheritsFromGroup_WhenMapLeavesValueUnset()
    {
        var state = new ClientState();
        state.MapGroups[3] = new MapGroupRecord { Index = 3, Moral = MapMoral.Safe, Music = 9, Indoors = true, AlwaysDark = true };
        var map = new MapRecord { MapGroup = 3 };   // Moral null, Music 0, bools null → all inherit

        Assert.Multiple(() =>
        {
            Assert.That(state.MoralOf(map), Is.EqualTo(MapMoral.Safe));
            Assert.That(state.MusicOf(map), Is.EqualTo(9));
            Assert.That(state.IndoorsOf(map), Is.True);
            Assert.That(state.LightingOf(map), Is.EqualTo(MapLighting.AlwaysDark));
        });
    }

    [Test]
    public void MapOwnValueOverridesGroup()
    {
        var state = new ClientState();
        state.MapGroups[3] = new MapGroupRecord { Index = 3, Moral = MapMoral.Safe, Music = 9 };
        // Explicit own values (MapMoral.None is a real value, not "unset"; a non-zero Music is an override).
        var map = new MapRecord { MapGroup = 3, Moral = MapMoral.None, Music = 4 };

        Assert.Multiple(() =>
        {
            Assert.That(state.MoralOf(map), Is.EqualTo(MapMoral.None), "an explicit Moral overrides the group");
            Assert.That(state.MusicOf(map), Is.EqualTo(4), "an explicit Music overrides the group");
        });
    }

    [Test]
    public void GrouplessUnknownAndNull_ResolveSafely()
    {
        var state = new ClientState();
        var groupless = new MapRecord { MapGroup = 0, Music = 2 };
        var dangling = new MapRecord { MapGroup = 7 };   // group 7 never received

        Assert.Multiple(() =>
        {
            Assert.That(state.MusicOf(groupless), Is.EqualTo(2), "no group -> the map's own value");
            Assert.That(state.MoralOf(groupless), Is.EqualTo(MapMoral.None), "no group + unset -> hard default");
            Assert.That(state.MoralOf(dangling), Is.EqualTo(MapMoral.None), "an unknown group resolves to the default");
            Assert.That(state.MoralOf(null), Is.EqualTo(MapMoral.None), "a null map (unloaded neighbor cell) is safe");
        });
    }

    // ── The live-propagation guarantee: a group edit re-resolves member maps with no map re-send ──

    [Test]
    public void UpdateMapGroup_ReResolvesMemberMapLive_WithoutReSendingTheMap()
    {
        var state = new ClientState();
        // Bulk at join: group 3 is a safe, music-9 zone; map 5 inherits it. The map object is created ONCE and
        // never touched again — mirroring a client that has the map cached.
        Apply(state, HandleSend, new SendMapGroupsPacket
        {
            Groups = new[] { new SendMapGroupsPacket.GroupData(3, "Old", MapMoral.Safe, 9, null, null, null, 0, 0, 0) },
        });
        var map = new MapRecord { MapGroup = 3 };
        Assert.That(state.MoralOf(map), Is.EqualTo(MapMoral.Safe), "map inherits the joined group state");
        Assert.That(state.MusicOf(map), Is.EqualTo(9));

        // A live editor save arrives for the same group — now non-safe with different music. The SAME cached map
        // must resolve to the new values immediately, with no map packet involved.
        Apply(state, HandleUpdate, new UpdateMapGroupPacket
        {
            GroupNum = 3, DisplayName = "New", Moral = MapMoral.None, Music = 4,
        });
        Assert.Multiple(() =>
        {
            Assert.That(state.MoralOf(map), Is.EqualTo(MapMoral.None), "the group edit re-resolves the member map live");
            Assert.That(state.MusicOf(map), Is.EqualTo(4));
            Assert.That(state.MapGroups[3]!.DisplayName, Is.EqualTo("New"));
        });
    }

    [Test]
    public void SendMapGroups_IsAFullSnapshot_DropsGroupsNoLongerPresent()
    {
        var state = new ClientState();
        state.MapGroups[2] = new MapGroupRecord { Index = 2, Music = 1 };   // a group from a prior snapshot
        Apply(state, HandleSend, new SendMapGroupsPacket
        {
            Groups = new[] { new SendMapGroupsPacket.GroupData(3, "G3", null, 0, null, null, null, 0, 0, 0) },
        });
        Assert.Multiple(() =>
        {
            Assert.That(state.MapGroups[2], Is.Null, "a bulk snapshot clears groups no longer present");
            Assert.That(state.MapGroups[3], Is.Not.Null, "and installs the ones that are");
        });
    }
}
