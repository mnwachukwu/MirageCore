using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Direct player-to-player trade: a live two-party session with staged offers escrowed off each player, a
/// confirm handshake, and an ATOMIC all-or-nothing swap. Runs on the game thread (no locks). Both players must
/// be within casting range (r=5, world-space); the client trade window locks movement, and any separation
/// (death / warp / disconnect) or invite timeout cancels the trade and returns both offers. No mail, instant
/// — except that a returned item a full bag can't take is mailed back rather than lost. Mirrors the party
/// invite flow (request → notify → accept), so the same anti-spam / symmetric-accept rules apply.
/// </summary>
public sealed class TradeSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly MailSystem _mail;
    private readonly IPersistenceService _persistence;   // null in unit tests → plain in-memory swap, no journal
    private readonly PlayerSaver _saver;
    private int _nextJournalId = 1;   // seeded past any survivor by RecoverJournalsAsync at boot

    private static string TradeSender => ServerStrings.Get(ServerStrings.Trade_Sender);

    public TradeSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items,
        MailSystem mail, IPersistenceService persistence, PlayerSaver saver)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _mail = mail;
        _persistence = persistence;
        _saver = saver;
    }

    // ── Request / accept ────────────────────────────────────────────────────────

    public void Request(int index, string targetName)
    {
        var me = _pm[index];
        if (!me.IsPlaying) return;
        int target = _pm.FindPlayerByName(targetName);
        if (target == 0 || !_pm[target].IsPlaying)
        {
            SendMsg(index, ServerStrings.Trade_TargetNotOnline, GameColor.White);
            return;
        }
        if (target == index)
        {
            SendMsg(index, ServerStrings.Trade_CannotTradeSelf, GameColor.BrightRed);
            return;
        }
        // Neither side of a trade may be a corpse. The asker's own death is refused by the handler with a
        // message; this is the other direction, which nothing else covers.
        if (_pm[target].Char.Dead)
        {
            SendMsg(index, ServerStrings.Trade_TargetIsDead, GameColor.BrightRed);
            return;
        }
        if (me.InTrade || _pm[target].InTrade)
        {
            SendMsg(index, ServerStrings.Trade_AlreadyTrading, GameColor.Pink);
            return;
        }
        if (!InRange(index, target))
        {
            SendMsg(index, ServerStrings.Trade_OutOfRange, GameColor.BrightRed);
            return;
        }

        // Symmetric accept: the target already has a request out to me → accept theirs instead of overwriting.
        if (_pm[target].TradeStarter && _pm[target].TradePartner == index)
        {
            Accept(index);
            return;
        }

        // Clear a prior dangling invite from me before starting a fresh one.
        if (me.TradeStarter && me.TradePartner > 0 && _pm[me.TradePartner].TradePartner == index && !_pm[me.TradePartner].InTrade)
            _pm[me.TradePartner].TradePartner = 0;

        me.TradeStarter = true;
        me.TradePartner = target;
        _pm[target].TradePartner = index;
        me.TradeInviteExpiresAt = Environment.TickCount64 + Constants.TradeInviteTimeoutMs;

        string fromName = me.Char.TrimmedName;
        _dispatcher.SendTo(target, new TradeInviteNotifyPacket { FromName = fromName });
        SendMsg(index, ServerStrings.Trade_RequestSent, GameColor.Pink, ("Name", targetName.TrimEnd()));
        SendMsg(target, ServerStrings.Trade_RequestReceived, GameColor.Pink, ("Name", fromName));
    }

    public void Respond(int index, bool accept)
    {
        if (accept) Accept(index);
        else DeclinePending(index);
    }

    private void Accept(int index)
    {
        var me = _pm[index];
        if (!me.IsPlaying) return;
        int starter = me.TradePartner;
        if (starter == 0 || !_pm[starter].IsPlaying || !_pm[starter].TradeStarter || _pm[starter].TradePartner != index)
        {
            SendMsg(index, ServerStrings.Trade_NoRequest, GameColor.Pink);
            return;
        }
        if (me.InTrade || _pm[starter].InTrade) return;
        if (!InRange(index, starter))
        {
            SendMsg(index, ServerStrings.Trade_OutOfRange, GameColor.BrightRed);
            ClearInvite(starter, index);
            return;
        }

        me.InTrade = _pm[starter].InTrade = true;
        _pm[starter].TradeStarter = false;
        _pm[starter].TradeInviteExpiresAt = 0;
        me.TradePartner = starter;
        me.TradeConfirmed = _pm[starter].TradeConfirmed = false;
        me.TradeOffer.Clear();
        _pm[starter].TradeOffer.Clear();
        SyncBoth(index, starter);
    }

    // Decline (or cancel) a pending, not-yet-accepted request from either side.
    private void DeclinePending(int index)
    {
        var me = _pm[index];
        int other = me.TradePartner;
        if (other > 0 && _pm[other].IsPlaying && _pm[other].TradeStarter && _pm[other].TradePartner == index)
        {
            SendMsg(other, ServerStrings.Trade_Declined, GameColor.Pink, ("Name", me.Char.TrimmedName));
            ClearInvite(other, index);
        }
        else if (me.TradeStarter)
        {
            ClearInvite(index, me.TradePartner);
        }
    }

    private void ClearInvite(int starter, int invitee)
    {
        if (starter > 0)
        {
            _pm[starter].TradeStarter = false;
            _pm[starter].TradePartner = 0;
            _pm[starter].TradeInviteExpiresAt = 0;
        }
        if (invitee > 0 && !_pm[invitee].InTrade && _pm[invitee].TradePartner == starter) _pm[invitee].TradePartner = 0;
    }

    // ── Offer ─────────────────────────────────────────────────────────────────────

    public void OfferAdd(int index, int invSlot, int amount)
    {
        var me = _pm[index];
        if (!ActiveTrade(index, out int partner)) return;
        if (me.TradeOffer.Count >= Constants.MaxTradeOfferItems)
        {
            SendMsg(index, ServerStrings.Trade_OfferFull, GameColor.BrightRed);
            return;
        }
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;
        var slot = me.Char.Inv[invSlot];
        if (slot.Num <= 0 || slot.Num > _world.Limits.Items) return;
        // Server backstop for the client's non-tradeable filter — a hacked client can't stage a blocked item.
        if (_world.Items[slot.Num].NonTradeable)
        {
            SendMsg(index, ServerStrings.Trade_CannotOfferItem, GameColor.BrightRed);
            return;
        }

        var (num, val, dur) = _items.RemoveFromSlot(index, invSlot, amount);
        if (num <= 0) return;
        me.TradeOffer.Add(new PlayerInvSlot { Num = num, Quantity = val, Dur = dur });
        _pm.MarkDirty(index);   // escrowed off the player — persist so a disconnect can't dupe
        Unconfirm(index, partner);
        SyncBoth(index, partner);
    }

    public void OfferRemove(int index, int offerIndex)
    {
        var me = _pm[index];
        if (!ActiveTrade(index, out int partner)) return;
        if (offerIndex < 0 || offerIndex >= me.TradeOffer.Count) return;
        var it = me.TradeOffer[offerIndex];
        me.TradeOffer.RemoveAt(offerIndex);
        GiveOrMail(index, it);   // back to the bag (escrowing it freed a slot), or mail if somehow full
        _pm.MarkDirty(index);
        Unconfirm(index, partner);
        SyncBoth(index, partner);
    }

    // ── Confirm / swap ──────────────────────────────────────────────────────────

    public void Confirm(int index, bool confirmed)
    {
        var me = _pm[index];
        if (!ActiveTrade(index, out int partner)) return;
        me.TradeConfirmed = confirmed;
        if (me.TradeConfirmed && _pm[partner].TradeConfirmed)
        {
            Swap(index, partner);
            return;
        }
        SyncBoth(index, partner);
    }

    private void Swap(int a, int b)
    {
        var pa = _pm[a];
        var pb = _pm[b];
        // Pre-check both bags can take the other's offer — no partial swaps.
        if (!CanReceive(pa.Char, pb.TradeOffer) || !CanReceive(pb.Char, pa.TradeOffer))
        {
            pa.TradeConfirmed = pb.TradeConfirmed = false;
            SendMsg(a, ServerStrings.Trade_NoSpace, GameColor.BrightRed);
            SendMsg(b, ServerStrings.Trade_NoSpace, GameColor.BrightRed);
            SyncBoth(a, b);
            return;
        }

        // Durable commit so the swap is atomic across the two SEPARATE per-login account files. Ordered,
        // fsync'd steps precede any bag change; a crash after any of them is reconciled by boot recovery
        // (RecoverJournalsAsync), and a failure at any step aborts the swap cleanly (offers survive). Unit
        // tests run with null persistence and take the plain in-memory path below (no journal).
        //   PHASE 0 — persist BOTH participants' PRE-swap state (escrow still staged) durably. This pins the
        //     recovery baseline: on disk, "escrow empty" now unambiguously means "this side's swap was
        //     applied", never "the offer wasn't flushed yet" — which matters because the game loop drains all
        //     queued packets before the dirty-flush, so a scripted pair could land both offers AND both
        //     confirms in ONE tick, leaving the escrow un-persisted at swap time.
        //   PHASE 1 — write+fsync a journal of who-receives-what: the atomic commit point for the pair.
        // (Blocking I/O on the game thread, but only on a completed trade — a rare, human-paced event.)
        int journalId = 0;
        if (_persistence is not null)
        {
            journalId = _nextJournalId++;
            var journal = new TradeJournal
            {
                Id = journalId,
                ALogin = pa.Login, AChar = pa.CharNum, AReceives = pb.TradeOffer.Select(Clone).ToList(),
                BLogin = pb.Login, BChar = pb.CharNum, BReceives = pa.TradeOffer.Select(Clone).ToList(),
            };
            try
            {
                _saver.SaveCharTracked(pa.Login, pa.CharNum, pa.Char.Clone(), pa.CloneBank()).GetAwaiter().GetResult();
                _saver.SaveCharTracked(pb.Login, pb.CharNum, pb.Char.Clone(), pb.CloneBank()).GetAwaiter().GetResult();
                _persistence.SaveTradeJournal(journal);
            }
            catch (Exception)
            {
                pa.TradeConfirmed = pb.TradeConfirmed = false;
                SendMsg(a, ServerStrings.Trade_Failed, GameColor.BrightRed);
                SendMsg(b, ServerStrings.Trade_Failed, GameColor.BrightRed);
                SyncBoth(a, b);
                return;
            }
        }

        // PHASE 2: apply. Per side, granting the receives AND clearing the escrow land in ONE account file, so
        // each side's half is atomic; the phase-1 journal makes the PAIR atomic.
        foreach (var it in pb.TradeOffer) _items.TryGiveItem(a, it.Num, it.Quantity, it.Dur);
        foreach (var it in pa.TradeOffer) _items.TryGiveItem(b, it.Num, it.Quantity, it.Dur);
        pa.TradeOffer.Clear();
        pb.TradeOffer.Clear();

        if (_persistence is not null)
        {
            // Save both participants NOW (tracked), and drop the journal only once BOTH are durably on disk.
            var ta = _saver.SaveCharTracked(pa.Login, pa.CharNum, pa.Char.Clone(), pa.CloneBank());
            var tb = _saver.SaveCharTracked(pb.Login, pb.CharNum, pb.Char.Clone(), pb.CloneBank());
            _ = DeleteJournalAfter(ta, tb, journalId);
        }
        else
        {
            _pm.MarkDirty(a);
            _pm.MarkDirty(b);
        }

        EndTrade(a, b, ServerStrings.Trade_Complete);
    }

    // Delete the write-ahead journal once BOTH participants are durably saved — until then it must survive so a
    // crash can be recovered. A failed delete is harmless: boot recovery re-runs idempotently and re-deletes.
    private async Task DeleteJournalAfter(Task saveA, Task saveB, int journalId)
    {
        try { await Task.WhenAll(saveA, saveB).ConfigureAwait(false); }
        catch { /* per-login save failures are already logged; the journal stays for recovery */ return; }
        try { await _persistence.DeleteTradeJournalAsync(journalId).ConfigureAwait(false); }
        catch { /* leftover journal is harmless — recovery replays it idempotently */ }
    }

    /// <summary>Boot recovery: replay any trade journal a crash left behind so an interrupted swap completes
    /// atomically instead of tearing. Runs once at world load, before anyone can log in, operating directly on
    /// account files. Idempotent — a side whose escrow is already empty had its half applied and is skipped.
    /// Also seeds the journal-id counter past any survivor so a new trade can't reuse a live id.</summary>
    public async Task RecoverJournalsAsync()
    {
        if (_persistence is null) return;
        var journals = await _persistence.LoadAllTradeJournalsAsync().ConfigureAwait(false);
        if (journals.Count == 0) return;
        _nextJournalId = journals.Max(j => j.Id) + 1;
        foreach (var j in journals)
        {
            await ReconcileSideAsync(j.ALogin, j.AChar, j.AReceives).ConfigureAwait(false);
            await ReconcileSideAsync(j.BLogin, j.BChar, j.BReceives).ConfigureAwait(false);
            await _persistence.DeleteTradeJournalAsync(j.Id).ConfigureAwait(false);
        }
    }

    // Complete one side of a journaled swap on its OFFLINE account file. Escrow non-empty ⇒ the swap wasn't
    // applied for this side, so grant its receives and clear the escrow (its staged items go to the partner via
    // the partner's branch / the partner's already-applied bag). Escrow empty ⇒ already applied ⇒ leave it.
    private async Task ReconcileSideAsync(string login, int charNum, List<PlayerInvSlot> receives)
    {
        if (string.IsNullOrEmpty(login) || charNum < 1 || charNum > Constants.MaxChars) return;
        var account = await _persistence.LoadAccountAsync(login).ConfigureAwait(false);
        if (account is null) return;
        var rec = account.Chars[charNum];
        if (rec is null || rec.TradeOffer.Count == 0) return;
        foreach (var it in receives)
            ItemSystem.TryGiveItemOffline(rec, _world.Items, it.Num, it.Quantity, it.Dur);
        rec.TradeOffer.Clear();
        await _persistence.SaveAccountAsync(account).ConfigureAwait(false);
    }

    // ── Cancel / teardown ───────────────────────────────────────────────────────

    public void Cancel(int index)
    {
        if (!ActiveTrade(index, out int partner))
        {
            DeclinePending(index);
            return;
        }
        ReturnOffer(index);
        ReturnOffer(partner);
        EndTrade(index, partner, ServerStrings.Trade_Canceled);
    }

    /// <summary>Cancel a player's trade / invite on disconnect or logout, returning both offers. Call from the
    /// leave path BEFORE the final character save so returned items persist.</summary>
    public void OnPlayerGone(int index)
    {
        var me = _pm[index];
        if (me.InTrade && me.TradePartner > 0)
        {
            int partner = me.TradePartner;
            ReturnOffer(index);
            ReturnOffer(partner);
            EndTrade(index, partner, ServerStrings.Trade_Canceled);
        }
        else if (me.TradeStarter)
        {
            ClearInvite(index, me.TradePartner);
        }
        else if (me.TradePartner > 0)
        {
            ClearInvite(me.TradePartner, index);
        }
    }

    /// <summary>Called from the login path (JoinGame): return any items still escrowed on the character record
    /// from a trade that a crash or shutdown interrupted before the leave-path could unwind it. A live trade
    /// never resumes across a restart, so the items simply come back — to the bag, or mail if it's full.
    /// A no-op in the common case (the escrow is empty), and idempotent.</summary>
    public void RecoverEscrowOnLogin(int index)
    {
        var offer = _pm[index].Char.TradeOffer;
        if (offer.Count == 0) return;
        // Detach the list first so a give that re-syncs inventory can't observe (or double-return) the escrow.
        var recovered = offer.ToList();
        offer.Clear();
        foreach (var it in recovered) GiveOrMail(index, it);
        _pm.MarkDirty(index);
    }

    /// <summary>Periodic game-thread sweep: cancel any trade whose parties drifted out of range or became
    /// invalid, and expire stale pending invites.</summary>
    public void Tick()
    {
        long now = Environment.TickCount64;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (sp.InTrade && sp.TradePartner > 0)
            {
                int partner = sp.TradePartner;
                if (!sp.IsPlaying || !_pm[partner].IsPlaying || _pm[partner].TradePartner != i || !_pm[partner].InTrade || !InRange(i, partner))
                {
                    ReturnOffer(i);
                    if (_pm[partner].InTrade && _pm[partner].TradePartner == i) ReturnOffer(partner);
                    EndTrade(i, partner, ServerStrings.Trade_Canceled);
                }
            }
            else if (sp.TradeStarter && sp.TradeInviteExpiresAt != 0 && now >= sp.TradeInviteExpiresAt)
            {
                ClearInvite(i, sp.TradePartner);
            }
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private bool ActiveTrade(int index, out int partner)
    {
        partner = 0;
        var me = _pm[index];
        if (!me.IsPlaying || !me.InTrade || me.TradePartner <= 0) return false;
        partner = me.TradePartner;
        if (!_pm[partner].IsPlaying || _pm[partner].TradePartner != index || !_pm[partner].InTrade)
        {
            OnPlayerGone(index);
            return false;
        }
        return true;
    }

    private void Unconfirm(int a, int b)
    {
        _pm[a].TradeConfirmed = false;
        _pm[b].TradeConfirmed = false;
    }

    private void ReturnOffer(int index)
    {
        var me = _pm[index];
        foreach (var it in me.TradeOffer) GiveOrMail(index, it);
        me.TradeOffer.Clear();
        if (me.IsPlaying) _pm.MarkDirty(index);
    }

    // Return one escrowed item to a player's bag, or mail it if the bag is full (never lose it).
    private void GiveOrMail(int index, PlayerInvSlot it)
    {
        if (_items.TryGiveItem(index, it.Num, it.Quantity, it.Dur)) return;
        _mail.Deliver(_pm[index].Login, TradeSender, ServerStrings.Get(ServerStrings.Trade_ReturnedSubject),
            ServerStrings.Get(ServerStrings.Trade_ReturnedBody),
            new List<MailAttachment> { new() { ItemNum = it.Num, Quantity = it.Quantity, Dur = it.Dur } });
    }

    private void EndTrade(int a, int b, string closeKey)
    {
        foreach (int i in new[] { a, b })
        {
            var sp = _pm[i];
            sp.InTrade = sp.TradeStarter = sp.TradeConfirmed = false;
            sp.TradePartner = 0;
            sp.TradeInviteExpiresAt = 0;
            sp.TradeOffer.Clear();
            if (sp.IsPlaying)
            {
                SendMsg(i, closeKey, GameColor.BrightGreen);
                _dispatcher.SendTo(i, new TradeWindowPacket { Open = false });
            }
        }
    }

    private void SyncBoth(int a, int b)
    {
        SyncTo(a, b);
        SyncTo(b, a);
    }

    private void SyncTo(int index, int partner)
    {
        var me = _pm[index];
        var them = _pm[partner];
        if (!me.IsPlaying) return;
        _dispatcher.SendTo(index, new TradeWindowPacket
        {
            PartnerName = them.Char.TrimmedName,
            MyOffer = me.TradeOffer.Select(Clone).ToList(),
            TheirOffer = them.TradeOffer.Select(Clone).ToList(),
            MyConfirmed = me.TradeConfirmed,
            TheirConfirmed = them.TradeConfirmed,
            Open = true,
        });
    }

    private static PlayerInvSlot Clone(PlayerInvSlot s) => new() { Num = s.Num, Quantity = s.Quantity, Dur = s.Dur };

    // Two players are in trade range iff the partner sits inside this player's r=5 spell circle (world-space).
    private bool InRange(int a, int b)
    {
        var pa = _pm[a].Char;
        var pb = _pm[b].Char;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, pa.Map);
        var (aWX, aWY) = grid.CenterToWorld(pa.X, pa.Y);
        var bw = grid.ToWorldRelative(pb.Map, pb.X, pb.Y);
        return bw is not null && WorldCoordHelper.IsInSpellRange(aWX, aWY, bw.Value.worldX, bw.Value.worldY);
    }

    // Whether every item in 'incoming' fits the receiver's bag: currency stacks onto an existing pile; else a
    // free slot is needed. Simulated over a slot-occupancy copy so multiple incoming items claim distinct slots.
    private bool CanReceive(PlayerRecord receiver, List<PlayerInvSlot> incoming)
    {
        var num = new int[Constants.MaxInv + 1];
        for (int i = 1; i <= Constants.MaxInv; i++) num[i] = receiver.Inv[i].Num;
        foreach (var it in incoming)
        {
            bool isCurrency = it.Num > 0 && it.Num < _world.Items.Length && _world.Items[it.Num].Type == ItemType.Currency;
            if (isCurrency)
            {
                bool has = false;
                for (int i = 1; i <= Constants.MaxInv; i++) if (num[i] == it.Num) { has = true; break; }
                if (has) continue;
            }
            int free = 0;
            for (int i = 1; i <= Constants.MaxInv; i++) if (num[i] == 0) { free = i; break; }
            if (free == 0) return false;
            num[free] = it.Num;
        }
        return true;
    }
}
