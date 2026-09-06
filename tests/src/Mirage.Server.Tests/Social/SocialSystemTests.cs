using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Tests.World;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Social;

/// <summary>Per-account friends/ignore lists (stored by account LOGIN). Locks the invariants: the two lists
/// are mutually exclusive (adding to one clears the other), a target must be ONLINE and can't be yourself
/// (even another character on your own account), rows don't duplicate, and ignore matches case-insensitively.</summary>
[TestFixture]
public class SocialSystemTests
{
    static (PlayerManager pm, SocialSystem social) Setup()
    {
        var pm = new PlayerManager();
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var social = new SocialSystem(pm, new NoOpDispatcher(), saver, NullLogger<SocialSystem>.Instance);
        return (pm, social);
    }

    static void MakePlayer(PlayerManager pm, int index, string login, string charName)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = login;
        sp.Char.Name = charName;
    }

    [Test]
    public void AddFriend_AddsTargetLogin_AndClearsPriorIgnore()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        MakePlayer(pm, 2, "bob", "Bob");
        pm[1].Ignore.Add("bob");

        social.AddFriend(1, "Bob");

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].Friends, Does.Contain("bob"));
            Assert.That(pm[1].Ignore, Does.Not.Contain("bob"), "friending someone clears a prior ignore");
        });
    }

    [Test]
    public void AddIgnore_AddsTargetLogin_AndClearsPriorFriend()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        MakePlayer(pm, 2, "bob", "Bob");
        pm[1].Friends.Add("bob");

        social.AddIgnore(1, "Bob");

        Assert.Multiple(() =>
        {
            Assert.That(pm[1].Ignore, Does.Contain("bob"));
            Assert.That(pm[1].Friends, Does.Not.Contain("bob"), "ignoring someone clears a prior friend");
        });
    }

    [Test]
    public void AddFriend_OwnCharacter_Rejected()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        social.AddFriend(1, "Alice");
        Assert.That(pm[1].Friends, Is.Empty, "you can't friend yourself");
    }

    // Another online character on your OWN account resolves to your login, so it's still "self".
    [Test]
    public void AddFriend_AltCharOnSameAccount_Rejected()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        MakePlayer(pm, 3, "alice", "AliceAlt");
        social.AddFriend(1, "AliceAlt");
        Assert.That(pm[1].Friends, Is.Empty, "another char on your own account is still you");
    }

    [Test]
    public void AddFriend_OfflineTarget_Rejected()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        social.AddFriend(1, "Ghost");
        Assert.That(pm[1].Friends, Is.Empty, "a target must be online to be added");
    }

    [Test]
    public void AddFriend_AlreadyFriend_NoDuplicate()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        MakePlayer(pm, 2, "bob", "Bob");
        social.AddFriend(1, "Bob");
        social.AddFriend(1, "Bob");
        Assert.That(pm[1].Friends.FindAll(x => x == "bob"), Has.Count.EqualTo(1), "no duplicate friend rows");
    }

    [Test]
    public void RemoveFriend_ByLogin_Removes()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        pm[1].Friends.Add("bob");
        social.RemoveFriend(1, "bob");
        Assert.That(pm[1].Friends, Does.Not.Contain("bob"));
    }

    [Test]
    public void IsIgnoring_MatchesCaseInsensitively()
    {
        var (pm, social) = Setup();
        MakePlayer(pm, 1, "alice", "Alice");
        pm[1].Ignore.Add("bob");
        Assert.Multiple(() =>
        {
            Assert.That(social.IsIgnoring(1, "BOB"), Is.True);
            Assert.That(social.IsIgnoring(1, "carol"), Is.False);
        });
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
