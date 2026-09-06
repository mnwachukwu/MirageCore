using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>Chat and bubbles, and the per-session surfaces the server pushes wholesale: bank,
/// shop, party, mail and the social panel.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Chat ──────────────────────────────────────────────────────────────────

    private void HandleChatMsg(ChatMsgPacket p) => ChatMessage?.Invoke(p);

    // ── Chat bubbles ──────────────────────────────────────────────────────────

    private void HandleChatBubble(ChatBubblePacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.PlayerIndex)) return;
        var player = _state.Players[p.PlayerIndex];
        if (string.IsNullOrEmpty(player.Name)) return;  // speaker not visible to us

        string raw = (p.Msg ?? "").Trim();
        if (raw.Length == 0) return;
        long now = Environment.TickCount64;

        // Demote current head (if any) into the drifter list before installing the new head.
        Logic.ChatBubbleManager.DemoteHeadToDrifter(player, now);

        int wordCount = CountWords(raw);
        long visibleMs = Math.Clamp(ChatBubbleStyle.BaseMs + ChatBubbleStyle.PerWordMs * wordCount,
            ChatBubbleStyle.MinMs, ChatBubbleStyle.MaxMs);
        player.ChatBubbleText = raw;
        player.ChatBubbleColor = p.Kind switch
        {
            1 => GameColor.Yellow,
            2 => GameColor.Pink,
            _ => GameColor.Say,
        };
        player.ChatBubbleEndMs = now + visibleMs;
    }

    private void HandleNpcChatBubble(NpcChatBubblePacket p)
    {
        // Native: addressed by (MapNum, NpcSlot).  Traversal guest: NpcSlot==0 and identity flows
        // through (SpawnMap, SpawnSlot) into the TraversalNpcs dict.  ClientTraversalNpc inherits
        // from ClientMapNpc so the bubble-state fields below work on either kind.
        ClientMapNpc? npc = null;
        if (p.NpcSlot >= 1)
        {
            var mapNpcs = _state.NpcsForMap(p.MapNum);
            if (mapNpcs is null || p.NpcSlot >= mapNpcs.Length) return;
            var candidate = mapNpcs[p.NpcSlot];
            if (candidate.Num <= 0) return;
            npc = candidate;
        }
        else if (p.SpawnSlot >= 1
                 && _state.TraversalNpcs.TryGetValue((p.SpawnMap, p.SpawnSlot), out var guest))
        {
            npc = guest;
        }
        if (npc is null) return;

        string raw = (p.Msg ?? "").Trim();
        if (raw.Length == 0) return;
        long now = Environment.TickCount64;

        Logic.ChatBubbleManager.DemoteHeadToDrifter(npc, now);

        int wordCount = CountWords(raw);
        long visibleMs = Math.Clamp(ChatBubbleStyle.BaseMs + ChatBubbleStyle.PerWordMs * wordCount,
            ChatBubbleStyle.MinMs, ChatBubbleStyle.MaxMs);
        npc.ChatBubbleText = raw;
        npc.ChatBubbleColor = p.Kind == 1 ? GameColor.BrightGreen : GameColor.BrightRed;
        npc.ChatBubbleEndMs = now + visibleMs;
    }

    private static int CountWords(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i])) { inWord = false; }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }
        return count;
    }

    // ── Bank ─────────────────────────────────────────────────────────────────

    private void HandleSendBank(SendBankPacket p)
    {
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
        {
            _state.Bank[i].Num = 0;
            _state.Bank[i].Quantity = 0;
            _state.Bank[i].Dur = 0;
        }
        foreach (var s in p.Slots)
        {
            if (!SlotValidation.IsValidBankSlot(s.Slot)) continue;
            _state.Bank[s.Slot].Num = s.Num;
            _state.Bank[s.Slot].Quantity = s.Quantity;
            _state.Bank[s.Slot].Dur = s.Dur;
        }
        _state.BankOpen = true;
    }

    private void HandleBankSlotUpdate(BankSlotUpdatePacket p)
    {
        if (!SlotValidation.IsValidBankSlot(p.Slot)) return;
        _state.Bank[p.Slot].Num = p.Num;
        _state.Bank[p.Slot].Quantity = p.Quantity;
        _state.Bank[p.Slot].Dur = p.Dur;
    }

    // ── Shop ─────────────────────────────────────────────────────────────────

    private void HandleShopContents(ShopContentsPacket p)
    {
        _state.ActiveShopNum = p.ShopNum;
        _state.ActiveBarters = p.Barters;
        _state.ActiveSales = p.Sales;
        ShopOpened?.Invoke(p.ShopNum);
    }

    // ── Party ─────────────────────────────────────────────────────────────────

    private void HandlePartyRequest(PartyRequestNotifyPacket p)
        => PartyRequest?.Invoke(p.FromName, p.FromIndex);

    private void HandleGuildOffer(GuildOfferNotifyPacket p)
        => GuildOffer?.Invoke(p);

    // ── Mail ──────────────────────────────────────────────────────────────────
    // The server pushes the whole mailbox (on entering the world and after every mark-read/delete),
    // so replace it wholesale. The Mail panel + sidebar link read ClientState directly — no event.
    private void HandleMailbox(MailboxPacket p) => _state.SetMail(p.Mail, p.Outbox, p.NowUtc);

    // The server pushes the whole marketplace when a player opens it from an inn and after any change they
    // made; replace it wholesale. Open=true also flags the panel to open (polled by GameplayScreen).
    private void HandleMarketList(MarketListPacket p) => _state.SetMarket(p.Listings, p.MySales, p.MeLogin, p.Open, p.NowUtc);

    // ── Social panel ──────────────────────────────────────────────────────────
    // Both carry a whole list and replace it; the Social panel reads ClientState directly.

    private void HandleGuildInfo(GuildInfoPacket p) => _state.SetGuildInfo(p);

    private void HandleSocialList(SocialListPacket p) => _state.SetSocialLists(p.Friends, p.Ignore);

    private void HandleModerationList(ModerationListPacket p) =>
        _state.SetModeration(p.Bans, p.Penalties, p.HardwareBans, p.HardwareBanMode, p.AccountsScanned);

    private void HandleGuildBrowse(GuildBrowsePacket p) => _state.SetGuildBrowse(p.Guilds);

    // Live war-attrition push (per death): update the matching war row's meters in place + record the
    // trend so the War panel's bar + direction arrow animate without a full GuildInfo resync.
    private void HandleGuildWarAttrition(GuildWarAttritionPacket p)
        => _state.ApplyWarAttrition(p.OpponentIndex, p.OurAttrition, p.TheirAttrition);

    private void HandlePartyVitals(PartyVitalsPacket p)
    {
        var party = _state.Party;
        if (string.IsNullOrEmpty(p.Name))
        {
            party.Clear();
            return;
        }

        // Snap on death so the bar lerps from full on respawn rather than rising from 0.
        if (p.Hp == 0 && party.Hp > 0) party.SnapVitals = true;

        party.Index = p.Index;
        party.Name = p.Name;
        party.Level = p.Level;
        party.Hp = p.Hp;
        party.MaxHp = p.MaxHp;
        party.Mp = p.Mp;
        party.MaxMp = p.MaxMp;
        party.Sp = p.Sp;
        party.MaxSp = p.MaxSp;
        party.MapNum = p.MapNum;
        party.X = p.X;
        party.Y = p.Y;
        party.ShowAsPk = p.ShowAsPk;
        party.Access = p.Access;
        party.LastCombatTickMs = p.MsSinceCombat == int.MaxValue
            ? 0
            : Environment.TickCount64 - p.MsSinceCombat;
    }
}
