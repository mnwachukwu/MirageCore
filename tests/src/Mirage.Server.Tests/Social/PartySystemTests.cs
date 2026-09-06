using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Social;

/// <summary>The two-player party state machine: an invite wires up a pending pair (starter flag on the
/// inviter only, not yet joined); accepting completes it; a mutual cross-invite auto-joins; leave/disband/
/// decline tear down BOTH sides; and the guards (target already partied, admins can't party, no pending
/// invite) hold.</summary>
[TestFixture]
public class PartySystemTests
{
    static PlayerManager Pm(params (int idx, string name)[] players)
    {
        var pm = new PlayerManager();
        foreach (var (idx, name) in players)
        {
            var sp = pm[idx];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            sp.Char.Name = name;
        }
        return pm;
    }

    static PartySystem NewParty(PlayerManager pm) => new(pm, new NoOpDispatcher());

    [Test]
    public void SendPartyRequest_WiresUpPendingInvite_NotYetJoined()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"));
        var party = NewParty(pm);

        party.SendPartyRequest(1, "Bob");

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].PartyStarter, Is.True, "the inviter is the starter");
            Assert.That(pm[1].PartyPlayer, Is.EqualTo(2));
            Assert.That(pm[2].PartyPlayer, Is.EqualTo(1), "the target points back at the inviter");
            Assert.That(pm[1].InParty, Is.False, "no one has joined yet");
            Assert.That(pm[2].InParty, Is.False);
        });
    }

    [Test]
    public void JoinParty_CompletesTheHandshake()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"));
        var party = NewParty(pm);

        party.SendPartyRequest(1, "Bob");
        party.JoinParty(2);   // Bob accepts

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].InParty, Is.True);
            Assert.That(pm[2].InParty, Is.True);
            Assert.That(pm[1].PartyPlayer, Is.EqualTo(2));
            Assert.That(pm[2].PartyPlayer, Is.EqualTo(1));
            Assert.That(pm[1].PartyStarter, Is.False, "the starter flag clears once joined");
        });
    }

    // Two players /join each other's names: the second request is treated as an acceptance, not a fresh
    // invite that would flag both as starter and deadlock.
    [Test]
    public void SendPartyRequest_MutualCrossInvite_AutoJoins()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"));
        var party = NewParty(pm);

        party.SendPartyRequest(1, "Bob");
        party.SendPartyRequest(2, "Alice");

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].InParty, Is.True);
            Assert.That(pm[2].InParty, Is.True);
        });
    }

    [Test]
    public void LeaveParty_TearsDownBothSides()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"));
        var party = NewParty(pm);
        party.SendPartyRequest(1, "Bob");
        party.JoinParty(2);

        party.LeaveParty(1);

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].InParty, Is.False);
            Assert.That(pm[1].PartyPlayer, Is.EqualTo(0));
            Assert.That(pm[2].InParty, Is.False, "the partner is dropped too");
            Assert.That(pm[2].PartyPlayer, Is.EqualTo(0));
        });
    }

    [Test]
    public void DisbandParty_OnLogout_ClearsBothSides()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"));
        var party = NewParty(pm);
        party.SendPartyRequest(1, "Bob");
        party.JoinParty(2);

        party.DisbandParty(1);

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].InParty, Is.False);
            Assert.That(pm[2].InParty, Is.False);
            Assert.That(pm[2].PartyPlayer, Is.EqualTo(0));
        });
    }

    [Test]
    public void SendPartyRequest_TargetAlreadyInParty_Refused()
    {
        var pm = Pm((1, "Alice"), (2, "Bob"), (3, "Carol"));
        var party = NewParty(pm);
        party.SendPartyRequest(1, "Bob");
        party.JoinParty(2);                       // Alice + Bob are partied

        party.SendPartyRequest(3, "Bob");         // Carol tries to invite Bob

        Assert.That(pm[3].PartyPlayer, Is.EqualTo(0), "you can't invite someone already in a party");
    }

    [Test]
    public void JoinParty_WithNoPendingInvite_NoOp()
    {
        var pm = Pm((1, "Alice"));
        var party = NewParty(pm);
        party.JoinParty(1);
        Assert.That(pm[1].InParty, Is.False);
    }

    // Admins (Access above Monitor) are barred from forming parties.
    [Test]
    public void SendPartyRequest_ByAdmin_Refused()
    {
        var pm = Pm((1, "Admin"), (2, "Bob"));
        pm[1].Char.Access = AdminLevel.Creator;
        var party = NewParty(pm);

        party.SendPartyRequest(1, "Bob");

        Assert.That(pm[1].PartyPlayer, Is.EqualTo(0), "admins can't form parties");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
