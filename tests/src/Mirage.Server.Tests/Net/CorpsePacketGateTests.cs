using Mirage.Server.Core.Net;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Tests.Net;

/// <summary>
/// The corpse gate: which packets a dead player may still have delivered to a handler.
///
/// <para>It is an ALLOW-list, so the failure it prevents is a packet added later that nobody remembers to
/// guard. The failure it can CAUSE is the opposite — an omission that breaks play while dead — so the
/// must-be-allowed half below matters at least as much as the must-be-denied half.</para>
/// </summary>
[TestFixture]
public class CorpsePacketGateTests
{
    static readonly MethodInfo Gate = typeof(PacketHandler).GetMethod(
        "AllowedWhileDead", BindingFlags.NonPublic | BindingFlags.Static)!;

    static bool Allowed<T>() where T : IPacket, new() => (bool)Gate.Invoke(null, [new T()])!;

    // ── Refusing these would break being dead ────────────────────────────────
    [Test] public void Respawn_IsAllowed() => Assert.That(Allowed<RespawnRequestPacket>(), Is.True);
    [Test] public void Say_IsAllowed() => Assert.That(Allowed<SayMsgPacket>(), Is.True);
    [Test] public void Emote_IsAllowed() => Assert.That(Allowed<EmoteMsgPacket>(), Is.True);
    [Test] public void Tell_IsAllowed() => Assert.That(Allowed<PlayerMsgPacket>(), Is.True);
    [Test] public void GuildChat_IsAllowed() => Assert.That(Allowed<GuildChatPacket>(), Is.True);
    [Test] public void MapData_IsAllowed() => Assert.That(Allowed<MapDataClientPacket>(), Is.True);
    [Test] public void NeedMap_IsAllowed() => Assert.That(Allowed<NeedMapPacket>(), Is.True);
    [Test] public void NeedNeighborMap_IsAllowed() => Assert.That(Allowed<NeedNeighborMapPacket>(), Is.True);
    [Test] public void RegionSync_IsAllowed() => Assert.That(Allowed<RequestRegionSyncPacket>(), Is.True);
    [Test] public void LogoutToCharSelect_IsAllowed() => Assert.That(Allowed<LogoutToCharSelectPacket>(), Is.True);
    [Test] public void WhoIsOnline_IsAllowed() => Assert.That(Allowed<WhoIsOnlinePacket>(), Is.True);
    [Test] public void GetStats_IsAllowed() => Assert.That(Allowed<GetStatsPacket>(), Is.True);
    [Test] public void TradeCancel_IsAllowed() => Assert.That(Allowed<TradeCancelPacket>(), Is.True);
    [Test] public void MarketClose_IsAllowed() => Assert.That(Allowed<MarketClosePacket>(), Is.True);

    /// <summary>Delivered so the handler can refuse them OUT LOUD. Denying them here would put those
    /// commands back to failing silently, which is the thing the messages exist to prevent.</summary>
    [Test] public void Home_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<HomeRequestPacket>(), Is.True);
    [Test] public void TradeInvite_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<TradeInvitePacket>(), Is.True);
    [Test] public void JoinParty_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<JoinPartyPacket>(), Is.True);
    [Test] public void LeaveParty_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<LeavePartyPacket>(), Is.True);
    [Test] public void WarpTo_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<WarpToPacket>(), Is.True);
    [Test] public void WarpMeTo_IsDelivered_SoItCanRefuseAloud() => Assert.That(Allowed<WarpMeToPacket>(), Is.True);

    // ── The gate's whole purpose ─────────────────────────────────────────────
    [Test] public void NpcInteract_IsDenied() => Assert.That(Allowed<NpcInteractPacket>(), Is.False);
    [Test] public void ConfirmSetSpawn_IsDenied() => Assert.That(Allowed<ConfirmSetSpawnPacket>(), Is.False);
    [Test] public void ShopBuy_IsDenied() => Assert.That(Allowed<ShopBuyPacket>(), Is.False);
    [Test] public void ShopSell_IsDenied() => Assert.That(Allowed<ShopSellPacket>(), Is.False);
    [Test] public void BankWithdraw_IsDenied() => Assert.That(Allowed<BankWithdrawPacket>(), Is.False);
    [Test] public void MarketBuy_IsDenied() => Assert.That(Allowed<MarketBuyPacket>(), Is.False);
    [Test] public void MarketCreate_IsDenied() => Assert.That(Allowed<MarketCreatePacket>(), Is.False);
    [Test] public void MailClaim_IsDenied() => Assert.That(Allowed<MailClaimPacket>(), Is.False);
    [Test] public void MailSend_IsDenied() => Assert.That(Allowed<MailSendPacket>(), Is.False);
    [Test] public void QuestTurnIn_IsDenied() => Assert.That(Allowed<QuestTurnInPacket>(), Is.False);
    [Test] public void QuestAccept_IsDenied() => Assert.That(Allowed<QuestAcceptPacket>(), Is.False);
    [Test] public void GuildDonate_IsDenied() => Assert.That(Allowed<GuildDonatePacket>(), Is.False);
    [Test] public void GuildWarDeclare_IsDenied() => Assert.That(Allowed<GuildWarDeclarePacket>(), Is.False);
    [Test] public void TerritoryChallenge_IsDenied() => Assert.That(Allowed<GuildTerritoryChallengePacket>(), Is.False);
    [Test] public void TradeConfirm_IsDenied() => Assert.That(Allowed<TradeConfirmPacket>(), Is.False);
    [Test] public void UseItem_IsDenied() => Assert.That(Allowed<UseItemPacket>(), Is.False);
    [Test] public void MapGetItem_IsDenied() => Assert.That(Allowed<MapGetItemPacket>(), Is.False);
    [Test] public void TrainStats_IsDenied() => Assert.That(Allowed<TrainStatsPacket>(), Is.False);
    [Test] public void PlayerMove_IsDenied() => Assert.That(Allowed<PlayerMovePacket>(), Is.False);

    /// <summary>The gate must have an opinion about every packet the server can receive, and it must not
    /// wave through most of them — a list that says yes to everything is not a gate.</summary>
    [Test]
    public void TheGate_ClassifiesEveryPacketType_AndDeniesTheBulkOfThem()
    {
        var types = typeof(IPacket).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPacket).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        Assert.That(types, Has.Count.GreaterThan(100), "reflection found almost no packets — wrong assembly?");

        var allowed = new List<string>();
        foreach (var t in types)
        {
            var instance = (IPacket)Activator.CreateInstance(t)!;
            if ((bool)Gate.Invoke(null, [instance])!) allowed.Add(t.Name);
        }

        Assert.That(allowed, Is.Not.Empty, "the gate denies everything — a corpse could not even respawn");
        Assert.That(allowed.Count, Is.LessThan(types.Count / 2),
            $"the gate allows {allowed.Count} of {types.Count} packet types, which is not a gate. "
            + "Allowed: " + string.Join(", ", allowed.OrderBy(x => x)));
    }
}
