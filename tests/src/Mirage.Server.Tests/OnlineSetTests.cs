using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.Players;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>
/// <see cref="PlayerManager.Online"/> is what every broadcast walks, so it has to be exactly right: a slot
/// missing from it is a player who stops hearing anything, and a stale one is a write into a dead channel.
///
/// <para>The reason it is maintained by <see cref="ServerPlayer.IsConnected"/>'s setter and nowhere else is
/// that occupancy is expressed by assigning that flag — production does it in three places, this test suite
/// does it in about thirty. An index kept anywhere else would be a second copy of the truth.</para>
/// </summary>
[TestFixture]
public sealed class OnlineSetTests
{
    private static PlayerManager Manager(int slots = 8) =>
        new(ServerConfig.Default with { MaxPlayers = slots });

    private static int[] Online(PlayerManager pm)
    {
        var copy = pm.Online.ToArray();
        Array.Sort(copy);
        return copy;
    }

    [Test]
    public void StartsEmpty()
    {
        Assert.That(Manager().Online.Length, Is.Zero);
    }

    [Test]
    public void SettingTheFlagIsWhatAddsASlot()
    {
        var pm = Manager();
        pm[3].IsConnected = true;
        pm[5].IsConnected = true;

        Assert.That(Online(pm), Is.EqualTo(new[] { 3, 5 }));
    }

    [Test]
    public void ClearingTheFlagRemovesIt()
    {
        var pm = Manager();
        pm[3].IsConnected = true;
        pm[3].IsConnected = false;

        Assert.That(pm.Online.Length, Is.Zero);
    }

    [Test]
    public void RemovingFromTheMiddleKeepsEveryoneElse()
    {
        // The removal swaps the last entry down into the hole. Whoever got moved must still be there.
        var pm = Manager();
        for (int i = 1; i <= 5; i++) pm[i].IsConnected = true;

        pm[2].IsConnected = false;

        Assert.That(Online(pm), Is.EqualTo(new[] { 1, 3, 4, 5 }));
    }

    [Test]
    public void SurvivesAChurnOfConnectsAndDisconnects()
    {
        var pm = Manager(16);
        var expected = new SortedSet<int>();

        // Deterministic churn: connect every slot, then drop every third, then reconnect every other one.
        for (int i = 1; i <= 16; i++) { pm[i].IsConnected = true; expected.Add(i); }
        for (int i = 1; i <= 16; i += 3) { pm[i].IsConnected = false; expected.Remove(i); }
        for (int i = 1; i <= 16; i += 2) { pm[i].IsConnected = true; expected.Add(i); }
        for (int i = 16; i >= 1; i -= 4) { pm[i].IsConnected = false; expected.Remove(i); }

        Assert.That(Online(pm), Is.EqualTo(expected.ToArray()));
    }

    [Test]
    public void SettingTheSameValueTwiceDoesNotDuplicate()
    {
        var pm = Manager();
        pm[4].IsConnected = true;
        pm[4].IsConnected = true;

        Assert.That(pm.Online.Length, Is.EqualTo(1));
    }

    [Test]
    public void ClearingASlotThatWasNeverConnectedIsHarmless()
    {
        var pm = Manager();
        pm[2].IsConnected = true;
        pm[7].IsConnected = false;

        Assert.That(Online(pm), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void ACombatGhostIsNotOnline()
    {
        // A ghost keeps playing with no socket. Leaving it out is what the set is FOR — the dispatcher
        // would only be enqueueing into a channel that was already torn down.
        var pm = Manager();
        pm[1].IsConnected = true;
        pm[1].InGame = true;
        pm[1].IsGhost = true;
        pm[1].IsConnected = false;

        Assert.That(pm[1].IsPlaying, Is.True, "a ghost is still in the world");
        Assert.That(pm.Online.Length, Is.Zero, "but there is nothing to send to");
    }

    [Test]
    public void TotalOnlineCountsTheSameThingItAlwaysDid()
    {
        var pm = Manager();
        for (int i = 1; i <= 4; i++) pm[i].IsConnected = true;
        pm[1].InGame = true;
        pm[2].InGame = true;

        Assert.That(pm.TotalOnline, Is.EqualTo(2), "connected but not in-game is not online");
    }

    [Test]
    public void EveryConnectedSlotIsReachableFromTheSet()
    {
        // The property that matters most: whatever the set says, no connected slot may be missing from it.
        var pm = Manager(12);
        for (int i = 1; i <= 12; i += 2) pm[i].IsConnected = true;
        pm[5].IsConnected = false;
        pm[8].IsConnected = true;

        var set = new HashSet<int>(pm.Online.ToArray());
        for (int i = 1; i <= 12; i++)
            Assert.That(set.Contains(i), Is.EqualTo(pm[i].IsConnected), $"slot {i}");
    }
}
