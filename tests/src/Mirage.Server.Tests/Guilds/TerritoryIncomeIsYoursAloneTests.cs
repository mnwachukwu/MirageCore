using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Tests.Guilds;

/// <summary>
/// What the Territories tab is told about money, and who is told it.
///
/// <para>Last week's income is public: a settled figure saying what a piece of land is worth, which is the
/// reason it is listed at all. The live ones are not. Pending income tracks a guild's hunting hour by hour,
/// so handing it to a rival says when they are on and how hard they are going — it goes to the guild that
/// earned it and nobody else.</para>
///
/// <para>🔴 Withholding a number and reporting it as zero are different claims, and a table cannot tell
/// them apart from the value alone. <c>OwnedByUs</c> is what lets the row show a dash instead of a figure
/// that would read as "this territory earned nothing".</para>
/// </summary>
[TestFixture]
public class TerritoryIncomeIsYoursAloneTests
{
    const int Group = 2;

    /// <summary>The row the server actually builds, for a viewer in <paramref name="viewerGuild"/>. Drives
    /// the shipped shaping through reflection rather than restating it.</summary>
    private static TerritoryView Row(int viewerGuild, int owningGuild,
                                     long pending = 900, long thisWeek = 5_400, long lastWeek = 12_000)
    {
        var world = new GameWorld();
        // Territory = true is what makes a group contestable; without it the sweep skips it entirely.
        world.MapGroups[Group] = new MapGroupRecord { Index = Group, Name = "Ashfall", Territory = true };

        var terr = world.TerritoryFor(Group);
        terr.ControllingGuild = owningGuild;
        terr.PendingTerritoryIncome = pending;
        terr.IncomeThisWeek = thisWeek;
        terr.PreviousWeekIncome = lastWeek;
        terr.WeeksHeld = 2;

        if (owningGuild > 0)
            world.Guilds[owningGuild] = new GuildRecord { Index = owningGuild, Name = "Owners" };

        var guilds = new GuildSystem(world, new PlayerManager(), new NoOpDispatcher(), null!, null!, null!, null!, null!,
            objectives: null!, NullLogger<GuildSystem>.Instance);

        var views = (List<TerritoryView>)typeof(GuildSystem)
            .GetMethod("TerritoryViews", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(guilds, [viewerGuild])!;

        return views.Single(v => v.Index == Group);
    }

    [Test]
    public void MyOwnTerritory_ShowsBothLiveFigures()
    {
        var row = Row(viewerGuild: 4, owningGuild: 4);

        Assert.Multiple(() =>
        {
            Assert.That(row.OwnedByUs, Is.True);
            Assert.That(row.PendingTerritoryIncome, Is.EqualTo(900));
            Assert.That(row.IncomeThisWeek, Is.EqualTo(5_400));
            Assert.That(row.PreviousWeekIncome, Is.EqualTo(12_000));
        });
    }

    [Test]
    public void SomebodyElsesTerritory_KeepsItsLiveFiguresToItself()
    {
        var row = Row(viewerGuild: 4, owningGuild: 9);

        Assert.Multiple(() =>
        {
            Assert.That(row.OwnedByUs, Is.False, "without this the row cannot tell a withheld figure from a real zero");
            Assert.That(row.PendingTerritoryIncome, Is.Zero, "a rival can see how hard the owner is farming right now");
            Assert.That(row.IncomeThisWeek, Is.Zero);
            Assert.That(row.PreviousWeekIncome, Is.EqualTo(12_000), "last week's figure is what the list is for");
        });
    }

    /// <summary>A player in no guild is not the owner of anything — guild 0 means "no guild", and an
    /// unclaimed territory carries the same 0.</summary>
    [Test]
    public void AGuildlessViewer_DoesNotOwnUnclaimedLand()
    {
        var row = Row(viewerGuild: 0, owningGuild: 0);

        Assert.Multiple(() =>
        {
            Assert.That(row.OwnedByUs, Is.False);
            Assert.That(row.PendingTerritoryIncome, Is.Zero);
            Assert.That(row.IncomeThisWeek, Is.Zero);
        });
    }

    [Test]
    public void AnUnclaimedTerritory_StillReportsWhatItWasWorth()
    {
        var row = Row(viewerGuild: 4, owningGuild: 0, pending: 0, thisWeek: 0, lastWeek: 3_000);

        Assert.Multiple(() =>
        {
            Assert.That(row.OwnedByUs, Is.False);
            Assert.That(row.Owner, Is.Empty);
            Assert.That(row.PreviousWeekIncome, Is.EqualTo(3_000), "what the land was worth to its last owner");
        });
    }

    // Per-file no-op dispatcher, the convention in this suite. Nothing here sends.
    sealed class NoOpDispatcher : Mirage.Server.Core.Net.IPacketDispatcher
    {
        public void SendTo(int index, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToAll(Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToAllBut(int exclude, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToViewport(int speakerIndex, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendChatBubble(int speakerIndex, Mirage.Shared.Protocol.IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToGuild(int guildId, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, Mirage.Server.Core.Net.ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, Mirage.Shared.Protocol.IPacket packet) { }
        public void SendToAllEditors(Mirage.Shared.Protocol.IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
