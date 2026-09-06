using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>Central client game-state helpers: time-of-day darkness, gold scan, unread-mail count, the
/// map-number → grid-cell routing used to place incoming entity packets, guest collision, and the warp reset.</summary>
[TestFixture]
public class ClientStateTests
{
    // Day is fully lit, Night fully dark, regardless of the interpolated progress within the phase.
    [Test]
    public void GetCurrentDarkness_DayIsZero_NightIsFull()
    {
        var s = new ClientState();
        s.TimePhase = TimePhase.Day;
        Assert.That(s.GetCurrentDarkness(), Is.EqualTo(0f));
        s.TimePhase = TimePhase.Night;
        Assert.That(s.GetCurrentDarkness(), Is.EqualTo(1f));
    }

    [Test]
    public void PlayerGold_ReturnsGoldSlotValue()
    {
        var s = new ClientState { MyIndex = 1 };
        s.Items[10] = new ItemRecord { Name = "Gold" };
        s.Me.Inv[1] = new PlayerInvSlot { Num = 10, Quantity = 750 };
        Assert.That(s.PlayerGold(), Is.EqualTo(750));
    }

    [Test]
    public void PlayerGold_NoGoldItem_ReturnsZero()
    {
        var s = new ClientState { MyIndex = 1 };
        s.Items[10] = new ItemRecord { Name = "Sword" };
        s.Me.Inv[1] = new PlayerInvSlot { Num = 10, Quantity = 1 };
        Assert.That(s.PlayerGold(), Is.EqualTo(0));
    }

    [Test]
    public void UnreadMailCount_CountsUnreadOnly()
    {
        var s = new ClientState();
        s.SetMail(new List<MailMessage>
        {
            new() { IsRead = false },
            new() { IsRead = true },
            new() { IsRead = false },
        }, new List<MailMessage>(), 0);
        Assert.That(s.UnreadMailCount(), Is.EqualTo(2));
    }

    // SetMail bumps the version so the panel rebuilds only on an actual change.
    [Test]
    public void SetMail_BumpsMailVersion()
    {
        var s = new ClientState();
        int before = s.MailVersion;
        s.SetMail(new List<MailMessage>(), new List<MailMessage>(), 0);
        Assert.That(s.MailVersion, Is.GreaterThan(before));
    }

    // Mail from ignored accounts is hidden client-side, so it's excluded from the unread badge too.
    [Test]
    public void UnreadMailCount_ExcludesIgnoredSenders()
    {
        var s = new ClientState();
        s.SetSocialLists(new List<SocialEntry>(), new List<SocialEntry> { new() { Login = "spammer" } });
        s.SetMail(new List<MailMessage>
        {
            new() { Sender = "friend", IsRead = false },
            new() { Sender = "spammer", IsRead = false },   // ignored
            new() { Sender = "SPAMMER", IsRead = false },   // ignored (case-insensitive)
            new() { Sender = "System", IsRead = false },
        }, new List<MailMessage>(), 0);
        Assert.That(s.UnreadMailCount(), Is.EqualTo(2), "ignored senders (any case) don't count toward unread");
    }

    [Test]
    public void IsSenderIgnored_MatchesCaseInsensitively()
    {
        var s = new ClientState();
        s.SetSocialLists(new List<SocialEntry>(), new List<SocialEntry> { new() { Login = "Bob" } });
        Assert.Multiple(() =>
        {
            Assert.That(s.IsSenderIgnored("bob"), Is.True, "case-insensitive login match");
            Assert.That(s.IsSenderIgnored("Alice"), Is.False);
            Assert.That(s.IsSenderIgnored(""), Is.False, "empty sender is never ignored");
            Assert.That(s.IsSenderIgnored("System"), Is.False, "system mail is never ignored");
        });
    }

    [Test]
    public void CellForMap_ResolvesCenterNeighborAndUnknown()
    {
        var s = new ClientState { CenterMapNum = 5 };
        s.NeighborMapNums[2, 1] = 6;
        Assert.Multiple(() =>
        {
            Assert.That(s.CellForMap(5), Is.EqualTo((1, 1)), "center");
            Assert.That(s.CellForMap(6), Is.EqualTo((2, 1)), "neighbor cell");
            Assert.That(s.CellForMap(99), Is.Null, "not loaded");
            Assert.That(s.CellForMap(0), Is.Null, "no map");
        });
    }

    [Test]
    public void NpcsForMap_RoutesToTheCorrectCellArray()
    {
        var s = new ClientState { CenterMapNum = 5 };
        s.NeighborMapNums[0, 2] = 7;
        Assert.Multiple(() =>
        {
            Assert.That(s.NpcsForMap(5), Is.SameAs(s.MapNpcs), "center resolves to the center array");
            Assert.That(s.NpcsForMap(7), Is.SameAs(s.NeighborNpcs[0, 2]), "a neighbor resolves to its cell array");
            Assert.That(s.NpcsForMap(99), Is.Null);
        });
    }

    // Guests live outside the slot arrays, so client collision checks them by (map, tile) directly.
    [Test]
    public void IsGuestOnTile_MatchesByMapAndTile()
    {
        var s = new ClientState();
        s.TraversalNpcs[(6, 1)] = new ClientTraversalNpc { Num = 1, CurrentMapNum = 6, X = 3, Y = 4 };
        Assert.Multiple(() =>
        {
            Assert.That(s.IsGuestOnTile(6, 3, 4), Is.True);
            Assert.That(s.IsGuestOnTile(6, 3, 5), Is.False, "different tile");
            Assert.That(s.IsGuestOnTile(99, 3, 4), Is.False, "different map");
        });
    }

    // A warp keeps the center map + the local player, but drops other players, neighbor maps, and center NPCs.
    [Test]
    public void ClearMapState_KeepsCenterAndSelf_ClearsTheRest()
    {
        var s = new ClientState { MyIndex = 1 };
        var center = s.Map;
        s.Me.Name = "Me";
        s.Players[2].Name = "Other";
        s.NeighborMaps[0, 0] = new MapRecord();
        s.NeighborMapNums[0, 0] = 1;
        s.MapNpcs[1].Num = 3;

        s.ClearMapState();

        Assert.Multiple(() =>
        {
            Assert.That(s.Map, Is.SameAs(center), "the center map is preserved");
            Assert.That(s.Me.Name, Is.EqualTo("Me"), "the local player is preserved");
            Assert.That(s.Players[2].Name, Is.Empty, "other players are cleared");
            Assert.That(s.NeighborMaps[0, 0], Is.Null, "neighbor maps are dropped");
            Assert.That(s.NeighborMapNums[0, 0], Is.EqualTo(0));
            Assert.That(s.MapNpcs[1].Num, Is.EqualTo(0), "center NPCs are cleared");
        });
    }
}
