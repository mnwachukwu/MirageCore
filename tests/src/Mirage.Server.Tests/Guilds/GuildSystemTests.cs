using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Guilds;

/// <summary>The manual late tax payment (<see cref="GuildSystem.PayTaxLate"/>): it takes exactly one week's tax
/// to restore suspended perks, and stamps the same per-date guard the 00:00 settlement uses so a forced
/// re-settlement of that date (the creator's <c>/guildreset</c>) cannot charge the week twice.</summary>
[TestFixture]
public class GuildSystemTests
{
    const int Idx = 1, GuildId = 1;
    const string Login = "founder";

    // A perks-suspended guild founded on TODAY's weekday — the collision case, where a late payment and a forced
    // settlement of the same date both target the same week's tax — whose sole member (slot 1) is its Leader.
    static (GuildSystem guilds, GuildRecord guild) Setup(int level, long vault)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var guild = new GuildRecord
        {
            Index = GuildId,
            Name = "Testers",
            Level = level,
            VaultGold = vault,
            PerksActive = false,
            FoundingWeekday = DateOnly.FromDateTime(DateTime.Now).DayOfWeek,
        };
        guild.Members.Add(new GuildMember { Login = Login, Rank = GuildRank.Leader });
        world.Guilds[GuildId] = guild;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = Login;
        sp.Guild = GuildId;
        sp.GuildRank = GuildRank.Leader;

        // Positional (persistence, bg, saver, items, mail, objectives null): the payment path only reads the world,
        // the player manager and the dispatcher — its guild write is chained onto a background task that swallows
        // the null persistence, and it never touches the account saver, items, mail or the objective kernel.
        var guilds = new GuildSystem(world, pm, new NoOpDispatcher(), null!, null!, null!, null!, null!,
            objectives: null!, NullLogger<GuildSystem>.Instance);
        return (guilds, guild);
    }

    [Test]
    public void PayTaxLate_RestoresPerks_AndStampsTheSettlementDate()
    {
        // The vault is seeded in weeks of tax, not in literal gold: the guild gold family is rescaled as a
        // unit, and this test is about the perk restore and the date stamp.
        long tax = 2 * Constants.GuildTaxPerLevel;
        var (guilds, guild) = Setup(level: 2, vault: tax * 2);

        guilds.PayTaxLate(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(guild.PerksActive, Is.True, "one week's tax restores the suspended perks");
            Assert.That(guild.VaultGold, Is.EqualTo(tax), "exactly one week, no back taxes");
            Assert.That(guild.LastTaxPaidDate, Is.EqualTo(DateOnly.FromDateTime(DateTime.Now)),
                "the manual payment stamps the settlement's per-date guard (server-local date)");
        });
    }

    [Test]
    public void PayTaxLate_ThenForcedSettlementOfTheSameDate_DoesNotChargeTheWeekTwice()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        long tax = 2 * Constants.GuildTaxPerLevel;
        var (guilds, guild) = Setup(level: 2, vault: tax * 2);

        guilds.PayTaxLate(Idx);
        // Today IS the founding weekday, so /guildreset → RunManualSettlement settles this very date.
        var result = GuildScheduleSystem.SettleGuild(guild, today, nowUtc: 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Tax, Is.EqualTo(TaxOutcome.None), "the date is already stamped as paid");
            Assert.That(guild.VaultGold, Is.EqualTo(tax), "the week's tax was taken once, by the manual payment");
        });
    }

    [Test]
    public void PayTaxLate_Unaffordable_TakesNothing_AndLeavesTheDateFreeToRetry()
    {
        var (guilds, guild) = Setup(level: 2, vault: 100);   // owes 2000

        guilds.PayTaxLate(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(guild.VaultGold, Is.EqualTo(100), "whole-or-nothing: nothing deducted");
            Assert.That(guild.PerksActive, Is.False, "perks stay suspended");
            Assert.That(guild.LastTaxPaidDate, Is.EqualTo(default(DateOnly)),
                "unstamped, so the settlement still retries this week's tax");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // No-op packet dispatcher (per-file convention; the guild paths only fan out to it, never read from it).
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
