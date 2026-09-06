using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Per-account mailbox delivery + management. Runs on the game thread. The mailbox mirror lives on
/// <see cref="ServerPlayer.Mail"/> (hydrated at login); the authoritative copy persists on
/// <see cref="AccountRecord.Mail"/>. Delivery works whether the recipient is online (update the mirror
/// + push to the client) or offline (persisted for their next login). The guild layer is the first
/// sender; deliberately shaped to grow into a full player-to-player mail system.
/// </summary>
public sealed class MailSystem : GameSystem
{
    private readonly PlayerManager _pm;
    private readonly PlayerSaver _saver;
    private readonly ItemSystem _items;

    // Bounds a mailbox so it can't grow without limit; the oldest read message is dropped past this.
    public const int MaxMailPerAccount = 50;

    // Watermark for the maturity sweep: only mail crossing DeliverAt within (prev, now] triggers a re-sync.
    // Seeded to construction time so already-delivered mail doesn't re-sync everyone on the first sweep.
    // Seeded in the constructor rather than inline, because the clock is now an injected member and a
    // field initializer runs before the base constructor has set it.
    private long _lastSweepUtc;

    public MailSystem(PlayerManager pm, IPacketDispatcher dispatcher, PlayerSaver saver, ItemSystem items,
                      IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, clock: clock, rng: rng)
    {
        _pm = pm;
        _saver = saver;
        _items = items;
        _lastSweepUtc = NowUtc;
    }

    /// <summary>Deliver a message to an account by login — online (mirror + push) or offline (persisted
    /// for next login). <paramref name="deliverAt"/> is the UTC-second the message matures; 0 = instant
    /// (system mail), claimable immediately. <paramref name="codPrice"/> > 0 makes it a Collect-on-Delivery
    /// message whose attachments stay locked until paid (and which returns to sender in 3 days if unpaid).</summary>
    public void Deliver(string login, string sender, string subject, string body,
        List<MailAttachment>? attachments = null, long deliverAt = 0, int codPrice = 0)
    {
        long nowUtc = NowUtc;
        int idx = _pm.FindOnlineByLogin(login);
        if (idx != 0)
        {
            var sp = _pm[idx];
            sp.Mail.Add(NewMessage(sp.Mail, sender, "", subject, body, nowUtc, deliverAt, attachments, markClaimed: false, codPrice));
            Trim(sp.Mail);
            PersistMail(login, sp.Mail);
            SyncTo(idx);
        }
        else
        {
            _saver.MutateAccountInBackground(login, a =>
            {
                a.Mail.Add(NewMessage(a.Mail, sender, "", subject, body, nowUtc, deliverAt, attachments, markClaimed: false, codPrice));
                Trim(a.Mail);
            });
        }
    }

    /// <summary>Send player-composed mail: deliver it to the recipient's inbox AND record a receipt in the
    /// sender's outbox, both stamped with the same <paramref name="deliverAt"/> so the "in transit →
    /// delivered" state shows on both ends. Player-origin only (the marketplace reuses this); system mail
    /// goes through <see cref="Deliver"/> and has no outbox.</summary>
    public void SendPlayerMail(string senderLogin, string recipientLogin, string subject, string body,
        List<MailAttachment> attachments, long deliverAt, int codPrice = 0)
    {
        Deliver(recipientLogin, senderLogin, subject, body, attachments, deliverAt, codPrice);
        AddToOutbox(senderLogin, recipientLogin, subject, body, attachments, deliverAt, codPrice);
    }

    // Record the sender's copy of a sent message in their outbox (online mirror + push, or offline persist).
    // Attachments are marked Claimed (the sender already parted with them) so the copy is purely a receipt. A CoD's
    // price rides along for display, but the receipt keeps the normal 30-day retention (only the recipient's
    // unclaimed copy carries the 3-day return clock — see NewMessage).
    private void AddToOutbox(string senderLogin, string recipientLogin, string subject, string body,
        List<MailAttachment> attachments, long deliverAt, int codPrice = 0)
    {
        long nowUtc = NowUtc;
        int idx = _pm.FindOnlineByLogin(senderLogin);
        if (idx != 0)
        {
            var sp = _pm[idx];
            sp.Outbox.Add(NewMessage(sp.Outbox, senderLogin, recipientLogin, subject, body, nowUtc, deliverAt, attachments, markClaimed: true, codPrice));
            Trim(sp.Outbox);
            PersistOutbox(senderLogin, sp.Outbox);
            SyncTo(idx);
        }
        else
        {
            _saver.MutateAccountInBackground(senderLogin, a =>
            {
                a.Outbox.Add(NewMessage(a.Outbox, senderLogin, recipientLogin, subject, body, nowUtc, deliverAt, attachments, markClaimed: true, codPrice));
                Trim(a.Outbox);
            });
        }
    }

    // Build a message, deep-copying the caller's attachment list so it can't be mutated after delivery.
    // deliverAt 0 collapses to nowUtc (instant); markClaimed stamps the copy's stacks claimed (outbox receipt).
    private static MailMessage NewMessage(List<MailMessage> mail, string sender, string recipient, string subject,
        string body, long nowUtc, long deliverAt, List<MailAttachment>? attachments, bool markClaimed, int codPrice = 0)
    {
        var list = attachments is null ? new List<MailAttachment>() : attachments.Select(a => a.Clone()).ToList();
        if (markClaimed) foreach (var a in list) a.Claimed = true;
        long matures = deliverAt == 0 ? nowUtc : deliverAt;
        // An UNCLAIMED CoD (the live recipient copy) rides the short 3-day RETURN clock; everything else — ordinary
        // mail and the sender's claimed CoD receipt — uses the 30-day retention.
        long lifetime = codPrice > 0 && !markClaimed ? Constants.CodLifetimeSeconds : Constants.MailRetentionSeconds;
        return new()
        {
            Id = NextId(mail),
            Sender = sender,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            TimeUtc = nowUtc,
            DeliverAt = matures,
            DeleteAt = matures + lifetime,
            CodPrice = codPrice,
            Attachments = list,
        };
    }

    /// <summary>Collect a message's attachments into the (online) recipient's inventory. Each stack is claimed
    /// independently: currency always fits; an item stack that a full bag can't take is left unclaimed for a
    /// later retry. No-op when nothing unclaimed remains.</summary>
    public void Claim(int index, int mailId)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var m = sp.Mail.FirstOrDefault(x => x.Id == mailId);
        if (m is null) return;
        if (NowUtc < m.DeliverAt) return;   // still in transit — not claimable yet
        bool changed = false;
        foreach (var a in m.Attachments)
        {
            if (a.Claimed || a.ItemNum <= 0) continue;   // Value 0 is a valid gear stack (durability, not count)
            if (_items.TryGiveItem(index, a.ItemNum, a.Quantity, a.Dur))
            {
                a.Claimed = true;
                changed = true;
            }
        }
        if (!changed) return;
        PersistMail(sp.Login, sp.Mail);
        SyncTo(index);
    }

    /// <summary>Finish a CoD purchase for the (online) receiver at <paramref name="index"/>: the caller has ALREADY
    /// verified affordability + inventory room and CHARGED the CoD price. Releases the message's locked attachments
    /// into the receiver's bag (all fit — pre-checked via <see cref="ItemSystem.CanReceiveAll"/>), mails the
    /// tax-adjusted NET gold to the original sender as normal delayed mail, and converts the message into an ordinary
    /// claimed mail (unlocked, deletable, 30-day expiry). No-op if <paramref name="mailId"/> isn't a live CoD.</summary>
    public void CompleteCod(int index, int mailId)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var m = sp.Mail.FirstOrDefault(x => x.Id == mailId);
        if (m is null || m.CodPrice <= 0) return;

        foreach (var a in m.Attachments)
        {
            if (a.Claimed || a.ItemNum <= 0) continue;
            if (_items.TryGiveItem(index, a.ItemNum, a.Quantity, a.Dur)) a.Claimed = true;
        }

        // The taxed net goes to the original sender (m.Sender) as a regular delayed P2P-style mail.
        int net = CodNet(m.CodPrice, CodItemCount(m.Attachments));
        if (net > 0)
        {
            long deliverAt = NowUtc
                + Rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);
            var goldBack = new List<MailAttachment> { new() { ItemNum = Constants.GoldItemIndex, Quantity = net } };
            Deliver(m.Sender, ServerStrings.Get(ServerStrings.Mail_SystemSender),
                ServerStrings.Get(ServerStrings.Mail_CodPaidSubject),
                ServerStrings.Format(ServerStrings.Mail_CodPaidBody, ("Name", sp.Login), ("Gold", net)),
                goldBack, deliverAt);
        }

        // Convert to a normal claimed mail: unlocked, deletable, and on the standard 30-day retention henceforth.
        m.CodPrice = 0;
        m.DeleteAt = m.DeliverAt + Constants.MailRetentionSeconds;
        PersistMail(sp.Login, sp.Mail);
        SyncTo(index);
    }

    /// <summary>Item attachments on a CoD — every stack except gold (the one currency exempt from its own tax).</summary>
    public static int CodItemCount(IReadOnlyList<MailAttachment> attachments)
    {
        int n = 0;
        foreach (var a in attachments)
            if (a.ItemNum > 0 && a.ItemNum != Constants.GoldItemIndex) n++;
        return n;
    }

    /// <summary>The CoD tax withheld from the price: the marketplace rate applied PER ITEM attached (floor), mirroring
    /// <see cref="MarketSystem.SaleTax"/>. Public so the compose UI's net preview agrees with the server.</summary>
    public static int CodTax(int price, int itemCount) =>
        (int)((long)price * Constants.MarketSaleTaxPercent * itemCount / 100);

    /// <summary>Gold the sender nets from a paid CoD after the per-item tax.</summary>
    public static int CodNet(int price, int itemCount) => price - CodTax(price, itemCount);

    /// <summary>Push a player's mailbox — inbox + outbox + the server clock (for in-transit rendering) — to
    /// their client (call on entering the world and after a change).</summary>
    public void SyncTo(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        _dispatcher.SendTo(index, new MailboxPacket
        {
            Mail = sp.Mail.Select(m => m.Clone()).ToList(),
            Outbox = sp.Outbox.Select(m => m.Clone()).ToList(),
            NowUtc = NowUtc,
        });
    }

    public void MarkRead(int index, int mailId)
    {
        var sp = _pm[index];
        var m = sp.Mail.FirstOrDefault(x => x.Id == mailId);
        if (m is null || m.IsRead) return;
        m.IsRead = true;
        PersistMail(sp.Login, sp.Mail);
        SyncTo(index);
    }

    /// <summary>Delete a message from the inbox, or from the sender's outbox when <paramref name="outbox"/>
    /// is set (removes only that copy). An in-transit message can't be deleted until it matures.</summary>
    public void Delete(int index, int mailId, bool outbox)
    {
        var sp = _pm[index];
        var list = outbox ? sp.Outbox : sp.Mail;
        var m = list.FirstOrDefault(x => x.Id == mailId);
        if (m is null) return;
        if (NowUtc < m.DeliverAt) return;   // still in transit — not deletable yet
        list.Remove(m);
        if (outbox) PersistOutbox(sp.Login, sp.Outbox);
        else PersistMail(sp.Login, sp.Mail);
        SyncTo(index);
    }

    // Persist a mailbox list to the account through the per-login write chain, from a detached snapshot.
    private void PersistMail(string login, List<MailMessage> mail)
    {
        var snapshot = mail.Select(m => m.Clone()).ToList();
        _saver.MutateAccountInBackground(login, a => a.Mail = snapshot);
    }

    private void PersistOutbox(string login, List<MailMessage> outbox)
    {
        var snapshot = outbox.Select(m => m.Clone()).ToList();
        _saver.MutateAccountInBackground(login, a => a.Outbox = snapshot);
    }

    private static int NextId(List<MailMessage> mail) => mail.Count == 0 ? 1 : mail.Max(m => m.Id) + 1;

    // Keep the mailbox bounded: drop the oldest READ message first, else the oldest of all.
    private static void Trim(List<MailMessage> mail)
    {
        while (mail.Count > MaxMailPerAccount)
        {
            int drop = mail.FindIndex(m => m.IsRead);
            mail.RemoveAt(drop >= 0 ? drop : 0);
        }
    }

    /// <summary>Periodic game-thread sweep: re-sync any online owner whose in-transit mail (inbox or outbox)
    /// matured since the last sweep, so their grayed "in transit" rows flip to delivered promptly. Cheap —
    /// scans only online players' bounded mailboxes, and only when the clock advanced a whole second.</summary>
    public void TickMaturity()
    {
        long nowUtc = NowUtc;
        long prev = _lastSweepUtc;
        if (nowUtc <= prev) return;
        _lastSweepUtc = nowUtc;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (Matured(sp.Mail, prev, nowUtc) || Matured(sp.Outbox, prev, nowUtc))
                SyncTo(i);
        }
    }

    // True if any message's DeliverAt falls in the half-open window (prev, now] — i.e. it matured this sweep.
    private static bool Matured(List<MailMessage> mail, long prev, long now)
    {
        foreach (var m in mail)
            if (m.DeliverAt > prev && m.DeliverAt <= now) return true;
        return false;
    }

    /// <summary>Periodic game-thread sweep (SaveTick cadence, mirrors <see cref="MarketSystem.TickExpiry"/>):
    /// expire mail past its <see cref="MailMessage.DeleteAt"/> from every ONLINE inbox + outbox, then persist +
    /// re-sync the affected owners. An unpaid CoD in the inbox is RETURNED to its sender (items intact) instead of
    /// deleted; ordinary mail and outbox receipts are dropped. Offline accounts are swept on their owner's next
    /// login (they come online and the next sweep catches them).</summary>
    public void TickExpiry()
    {
        long nowUtc = NowUtc;
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            bool changed = SweepInbox(sp.Login, sp.Mail, nowUtc);   // unpaid CoDs return to sender; the rest drop
            changed |= DropExpired(sp.Outbox, nowUtc);              // outbox copies (incl. CoD receipts) just drop
            if (!changed) continue;
            PersistMail(sp.Login, sp.Mail);
            PersistOutbox(sp.Login, sp.Outbox);
            SyncTo(i);
        }
    }

    // Sweep an inbox: a matured UNPAID CoD (CodPrice > 0) is RETURNED to its sender with items intact; any other
    // matured message (past DeleteAt) is simply dropped. Returns true if the inbox changed.
    private bool SweepInbox(string ownerLogin, List<MailMessage> mail, long nowUtc)
    {
        bool changed = false;
        for (int k = mail.Count - 1; k >= 0; k--)
        {
            var m = mail[k];
            if (m.DeleteAt <= 0 || nowUtc < m.DeleteAt) continue;
            if (m.CodPrice > 0) ReturnCod(ownerLogin, m);
            mail.RemoveAt(k);
            changed = true;
        }
        return changed;
    }

    // Mail an expired, unpaid CoD's intact item attachments back to the original sender (m.Sender) as normal delayed
    // mail, telling them the recipient (ownerLogin) never paid. The returned stacks are marked unclaimed to reclaim.
    private void ReturnCod(string recipientLogin, MailMessage m)
    {
        var items = m.Attachments.Where(a => a.ItemNum > 0).Select(a => a.Clone()).ToList();
        foreach (var a in items) a.Claimed = false;
        long deliverAt = NowUtc
            + Rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);
        Deliver(m.Sender, ServerStrings.Get(ServerStrings.Mail_SystemSender),
            ServerStrings.Get(ServerStrings.Mail_CodReturnedSubject),
            ServerStrings.Format(ServerStrings.Mail_CodReturnedBody, ("Name", recipientLogin)),
            items, deliverAt);
    }

    // Remove every message past its DeleteAt (0 = legacy / never expires); returns true if any were removed.
    private static bool DropExpired(List<MailMessage> mail, long nowUtc)
    {
        int before = mail.Count;
        mail.RemoveAll(m => m.DeleteAt > 0 && nowUtc >= m.DeleteAt);
        return mail.Count != before;
    }
}
