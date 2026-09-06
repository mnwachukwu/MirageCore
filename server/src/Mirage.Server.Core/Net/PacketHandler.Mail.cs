using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>The post: reading, deleting and claiming a message, sending one with attachments and postage, and paying a cash-on-delivery parcel.</summary>
public sealed partial class PacketHandler
{
    //  Mail handlers
    // ===========================================================================

    private void HandleMailMarkRead(int index, MailMarkReadPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _mail.MarkRead(index, p.Id);
    }

    private void HandleMailDelete(int index, MailDeletePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _mail.Delete(index, p.Id, p.Outbox);
    }

    private void HandleMailClaim(int index, MailClaimPacket p)
    {
        if (!IsActing(index)) return;
        _mail.Claim(index, p.Id);
    }

    // Compose + send player-to-player mail, addressed by ACCOUNT name (the mailbox is per-account). Items are
    // escrowed off the sender synchronously (anti-dupe) before any async recipient check; an unknown recipient
    // refunds them.
    private void HandleMailSend(int index, MailSendPacket p)
    {
        var sp = _pm[index];
        if (!IsActing(index)) return;

        // Recipients are a comma-separated list. A blank overall field OR any blank token ("a,,b", trailing
        // comma) is rejected; every token is injection-checked; you can never be a recipient (sole or in a list).
        var recipients = p.Recipient.Split(',').Select(t => t.Trim()).ToList();
        if (recipients.Count > Constants.MaxMailRecipients)
        {
            MailMsg(index, ServerStrings.Mail_TooManyRecipients, GameColor.BrightRed, ("Max", Constants.MaxMailRecipients));
            return;
        }
        if (recipients.Count == 0 || recipients.Any(r => r.Length == 0))
        {
            MailMsg(index, ServerStrings.Mail_NoRecipient, GameColor.BrightRed);
            return;
        }
        if (recipients.Any(r => !TextValidation.IsValidText(r)))
        {
            HackingAttempt(index, "Mail Recipient Modification");
            return;
        }
        if (recipients.Any(r => string.Equals(r, sp.Login, StringComparison.OrdinalIgnoreCase)))
        {
            MailMsg(index, ServerStrings.Mail_CannotMailSelf, GameColor.BrightRed);
            return;
        }

        bool multi = recipients.Count > 1;

        // Count staged attachments (resolve each slot to a real item). Attachments are DISALLOWED on a
        // multi-recipient send.
        int attachCount = 0;
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            if (SlotValidation.IsValidInvSlot(spec.InvSlot) && sp.Char.Inv[spec.InvSlot].Num > 0) attachCount++;
        if (multi && attachCount > 0)
        {
            MailMsg(index, ServerStrings.Mail_MultiNoAttachments, GameColor.BrightRed);
            return;
        }

        // Non-mailable backstop for the single-recipient attachment path. Checked BEFORE escrowing.
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
        {
            if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
            int num = sp.Char.Inv[spec.InvSlot].Num;
            if (num > 0 && num <= _world.Limits.Items && _world.Items[num].NonMailable)
            {
                MailMsg(index, ServerStrings.Mail_CannotMailItem, GameColor.BrightRed);
                return;
            }
        }

        // CoD: the receiver pays this to unlock the attachments. A CoD is single-recipient and MUST carry at least
        // one ITEM (non-gold) attachment — a gold-only or text-only CoD is meaningless. Clamp to the market ceiling.
        int codPrice = Math.Clamp(p.CodPrice, 0, Constants.MarketMaxPrice);
        if (codPrice > 0)
        {
            if (multi)
            {
                MailMsg(index, ServerStrings.Mail_CodSingleOnly, GameColor.BrightRed);
                return;
            }
            int itemAttach = 0;
            foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            {
                if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
                int num = sp.Char.Inv[spec.InvSlot].Num;
                if (num > 0 && num != Constants.GoldItemIndex) itemAttach++;
            }
            if (itemAttach == 0)
            {
                MailMsg(index, ServerStrings.Mail_CodNeedsItem, GameColor.BrightRed);
                return;
            }
        }

        // A blank subject becomes "(No Subject)" (the client warns first, but the substitution is authoritative).
        string subject = ClampText(p.Subject, Constants.MailSubjectMaxLength);
        if (string.IsNullOrWhiteSpace(subject)) subject = ServerStrings.Get(ServerStrings.Mail_NoSubjectDefault);
        string body = ClampText(p.Body, Constants.MailBodyMaxLength);

        // Cost (a gold sink): single = base + per-attachment; multi = base per recipient. A long body escalates
        // it (over threshold 1 → x2, threshold 2 → x10). Charged up front; the body clamp above bounds the length.
        int bodyMult = body.Length > Constants.MailVeryLongBodyThreshold ? Constants.MailVeryLongBodyCostMultiplier
                     : body.Length > Constants.MailLongBodyThreshold ? Constants.MailLongBodyCostMultiplier : 1;
        // Postage is two flat parts plus a percent of what the parcel is WORTH — see EconomyFormulas for
        // why the scaling half is keyed on the shipment rather than the sender. A multi-recipient send
        // carries no attachments (rejected above), so it has no value component.
        long attachedValue = 0;
        if (!multi)
        {
            var ch = sp.Char;
            foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            {
                if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
                var slot = ch.Inv[spec.InvSlot];
                if (slot.Num <= 0 || slot.Num > _world.Limits.Items) continue;
                // An EQUIPPED slot is refused by RemoveFromSlot, so it never actually ships — charging a
                // percentage of it would bill for a parcel that does not leave. (The flat per-attachment
                // fee still counts it, as it always has; that is a 50-gold quirk rather than a real one.)
                if (spec.InvSlot == ch.WeaponSlot || spec.InvSlot == ch.ArmorSlot
                    || spec.InvSlot == ch.HelmetSlot || spec.InvSlot == ch.ShieldSlot) continue;
                // Mirrors RemoveFromSlot exactly: currency sends the requested amount, where 0 or an
                // oversized request means the WHOLE stack; everything else always goes as a whole slot.
                var def = _world.Items[slot.Num];
                int quantity = def.Type == ItemType.Currency
                    ? (spec.Quantity <= 0 || spec.Quantity > slot.Quantity ? slot.Quantity : spec.Quantity)
                    : slot.Quantity;
                attachedValue += EconomyFormulas.MailAttachmentValue(slot.Num, quantity, def.Price);
            }
        }
        long cost = (multi ? EconomyFormulas.MailSendCost(0) * recipients.Count
                           : EconomyFormulas.MailSendCost(attachCount, attachedValue)) * bodyMult;
        if (ItemSystem.CountItem(sp.Char, _world.Items, Constants.GoldItemIndex) < cost)
        {
            MailMsg(index, ServerStrings.Mail_CannotAfford, GameColor.BrightRed, ("Cost", cost));
            return;
        }
        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);

        // Player-origin mail rides "in transit" for a random 10-15 min before it matures on both ends.
        long deliverAt = NowUtc
            + _rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);

        if (multi)
        {
            // Text-only fan-out: deliver online recipients now; validate the offline ones TOGETHER so every
            // unreachable account collapses into ONE return-to-sender bounce. One optimistic batch confirmation.
            var offline = new List<string>();
            foreach (var to in recipients)
            {
                if (_pm.FindOnlineByLogin(to) != 0)
                    _mail.SendPlayerMail(sp.Login, to, subject, body, new List<MailAttachment>(), deliverAt);
                else
                    offline.Add(to);
            }
            if (offline.Count > 0)
                _bg.Run(DeliverMultiOffline(sp.Login, offline, subject, body, deliverAt), "MailSend");
            MailMsg(index, ServerStrings.Mail_SentToMany, GameColor.BrightGreen, ("Count", recipients.Count));
            return;
        }

        // Single recipient — escrow the staged attachments off the sender NOW, before any async gap.
        string only = recipients[0];
        var attachments = new List<MailAttachment>();
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
        {
            var (num, qty, dur) = _items.RemoveFromSlot(index, spec.InvSlot, spec.Quantity);
            if (num > 0) attachments.Add(new MailAttachment { ItemNum = num, Quantity = qty, Dur = dur });
        }

        // Online recipient: deliver immediately. Offline: validate the account off-thread, then hop back to
        // deliver it — or, if there's no such account, RETURN the whole mail (attachments included) to the sender.
        if (_pm.FindOnlineByLogin(only) != 0)
        {
            _mail.SendPlayerMail(sp.Login, only, subject, body, attachments, deliverAt, codPrice);
            MailMsg(index, ServerStrings.Mail_Sent, GameColor.BrightGreen);
            return;
        }
        _bg.Run(DeliverOfflineMail(sp.Login, only, subject, body, attachments, deliverAt, codPrice), "MailSend");
    }

    // Pay a Collect-on-Delivery mail: verify the receiver can afford the price AND has room for every item
    // (all-or-nothing — a full bag must never eat a paid item), charge the gold, then release the items + remit
    // the taxed net to the sender via MailSystem.CompleteCod.
    private void HandleMailPayCod(int index, MailPayCodPacket p)
    {
        var sp = _pm[index];
        if (!IsActing(index)) return;
        var m = sp.Mail.FirstOrDefault(x => x.Id == p.Id);
        if (m is null || m.CodPrice <= 0) return;                                   // not a live CoD
        if (NowUtc < m.DeliverAt) return;        // still in transit
        if (ItemSystem.CountItem(sp.Char, _world.Items, Constants.GoldItemIndex) < m.CodPrice)
        {
            MailMsg(index, ServerStrings.Mail_CannotAffordCod, GameColor.BrightRed, ("Cost", m.CodPrice));
            return;
        }
        if (!ItemSystem.CanReceiveAll(sp.Char, _world.Items, m.Attachments))
        {
            MailMsg(index, ServerStrings.Mail_CodNoRoom, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, m.CodPrice);
        _mail.CompleteCod(index, m.Id);
        MailMsg(index, ServerStrings.Mail_CodPaid, GameColor.BrightGreen);
    }

    // Single-recipient offline delivery: deliver if the account exists (confirming to the sender), else bounce
    // the whole mail — attachments included — back to the sender as normal return-to-sender mail.
    private async Task DeliverOfflineMail(string senderLogin, string to, string subject, string body,
        List<MailAttachment> attachments, long deliverAt, int codPrice = 0)
    {
        bool exists = await _persistence.AccountExistsAsync(to);
        _gameLoop.Post(() =>
        {
            if (exists)
            {
                _mail.SendPlayerMail(senderLogin, to, subject, body, attachments, deliverAt, codPrice);
                int si = _pm.FindOnlineByLogin(senderLogin);
                if (si != 0) MailMsg(si, ServerStrings.Mail_Sent, GameColor.BrightGreen);
            }
            else
            {
                ReturnToSender(senderLogin, new List<string> { to }, attachments, deliverAt);
            }
        });
    }

    // Multi-recipient offline delivery: validate the offline recipients together, deliver the ones that exist,
    // and collapse ALL unreachable accounts into a SINGLE return-to-sender bounce (multi is text-only).
    private async Task DeliverMultiOffline(string senderLogin, List<string> offline, string subject, string body, long deliverAt)
    {
        var existsFlags = new bool[offline.Count];
        for (int i = 0; i < offline.Count; i++)
            existsFlags[i] = await _persistence.AccountExistsAsync(offline[i]);
        _gameLoop.Post(() =>
        {
            var unreachable = new List<string>();
            for (int i = 0; i < offline.Count; i++)
            {
                if (existsFlags[i]) _mail.SendPlayerMail(senderLogin, offline[i], subject, body, new List<MailAttachment>(), deliverAt);
                else unreachable.Add(offline[i]);
            }
            if (unreachable.Count > 0) ReturnToSender(senderLogin, unreachable, new List<MailAttachment>(), deliverAt);
        });
    }

    // Bounce undeliverable mail back to the sender as normal "System" mail: the original attachments ride back
    // in it (nothing is lost, even for an offline sender), and the body names the account(s) that don't exist.
    // NOT instant — the bounce is deferred until the original would have been delivered, then rides its OWN
    // delivery time back, so a rejection never beats the original's feasible delivery and probing for account
    // names costs real time (as well as the spent, non-refunded postage). Deliver persists it for an offline sender.
    private void ReturnToSender(string senderLogin, List<string> unreachable, List<MailAttachment> attachments, long originalDeliverAt)
    {
        long deliverAt = originalDeliverAt
            + _rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);
        string body = ServerStrings.Format(ServerStrings.Mail_ReturnedBody, ("Names", string.Join(", ", unreachable)));
        _mail.Deliver(senderLogin, "System", ServerStrings.Get(ServerStrings.Mail_ReturnedSubject), body, attachments, deliverAt);
    }

    private void MailMsg(int index, string key, int color, params (string, object?)[] args)
        => _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, ChatChannel.Notice), args);

    private static string ClampText(string s, int max) => s.Length <= max ? s : s[..max];
}
