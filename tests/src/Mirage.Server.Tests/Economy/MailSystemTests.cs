using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Economy;

/// <summary>Mail attachment claiming on <see cref="MailSystem"/> and the deep-copy invariant on
/// <see cref="MailMessage.Clone"/>. Locks the design's load-bearing hazards: gold (currency) always
/// claims and stacks, an item attachment a full bag can't take is left CLAIMABLE (never silently
/// eaten), a claimed item carries the sender's worn durability, and a mixed message claims what fits
/// while leaving the rest. Persistence is fire-and-forget (a null persistence write faults into the
/// logged catch), so the harness drives Claim with a null-persistence PlayerSaver.</summary>
[TestFixture]
public class MailSystemTests
{
    const int Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10, Armor = 11;

    static (GameWorld world, ItemSystem items, MailSystem mail, ServerPlayer sp) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "tester";
        return (world, items, mail, sp);
    }

    // Variant exposing the PlayerManager so a test can bring a second account (sender + recipient) online.
    static (GameWorld world, PlayerManager pm, ItemSystem items, MailSystem mail) SetupWorld()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);
        return (world, pm, items, mail);
    }

    static ServerPlayer Online(PlayerManager pm, int idx, string login)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = login;
        return sp;
    }

    [Test]
    public void Clone_DeepCopiesAttachments()
    {
        var m = new MailMessage { Id = 1, Attachments = { new MailAttachment { ItemNum = 5, Quantity = 10 } } };

        var copy = m.Clone();
        copy.Attachments[0].Quantity = 999;
        copy.Attachments.Add(new MailAttachment { ItemNum = 6, Quantity = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(m.Attachments, Has.Count.EqualTo(1), "the clone's list is separate from the original's");
            Assert.That(m.Attachments[0].Quantity, Is.EqualTo(10), "mutating the clone leaves the original attachment untouched");
        });
    }

    // ── 30-day expiry ──────────────────────────────────────────────────────────

    [Test]
    public void Deliver_StampsDeleteAt_RetentionAfterMaturity()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, Idx, "tester");
        long deliverAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;

        mail.Deliver("tester", "System", "Hi", "Body", null, deliverAt);

        Assert.That(sp.Mail, Has.Count.EqualTo(1));
        Assert.That(sp.Mail[0].DeleteAt, Is.EqualTo(deliverAt + Constants.MailRetentionSeconds),
            "DeleteAt is the 30-day retention measured from when the message matures");
    }

    [Test]
    public void TickExpiry_SweepsExpiredMail_KeepsFresh()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, Idx, "tester");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        sp.Mail.Add(new MailMessage { Id = 1, DeliverAt = now - 100, DeleteAt = now - 1 });       // past retention
        sp.Mail.Add(new MailMessage { Id = 2, DeliverAt = now - 100, DeleteAt = now + 100_000 }); // fresh
        sp.Mail.Add(new MailMessage { Id = 3, DeliverAt = now - 100, DeleteAt = 0 });              // legacy: never expires

        mail.TickExpiry();

        Assert.Multiple(() =>
        {
            Assert.That(sp.Mail, Has.Count.EqualTo(2), "only the past-retention message is swept");
            Assert.That(sp.Mail.Exists(m => m.Id == 1), Is.False, "the expired message is gone");
            Assert.That(sp.Mail.Exists(m => m.Id == 2), Is.True, "the fresh message survives");
            Assert.That(sp.Mail.Exists(m => m.Id == 3), Is.True, "a legacy DeleteAt=0 message never expires");
        });
    }

    [Test]
    public void Claim_Gold_CreditsInventory_MarksClaimed()
    {
        var (world, _, mail, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        sp.Mail.Add(new MailMessage { Id = 1, Attachments = { new MailAttachment { ItemNum = Gold, Quantity = 500 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Gold), Is.EqualTo(500), "gold lands in the bag");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.True, "the stack is marked claimed");
        });
    }

    [Test]
    public void Claim_Item_FreeSlot_GivesItem_CarriesDurability()
    {
        var (world, _, mail, sp) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability =80;  // max durability
        sp.Mail.Add(new MailMessage { Id = 1, Attachments = { new MailAttachment { ItemNum = Sword, Quantity = 1, Dur = 55 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Inv[1].Num, Is.EqualTo(Sword), "the item lands in the bag");
            Assert.That(sp.Char.Inv[1].Dur, Is.EqualTo(55), "the sender's worn durability is carried, not reset to max");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.True);
        });
    }

    // The headline hazard: a full inventory must NOT silently consume an item attachment.
    [Test]
    public void Claim_Item_FullBag_LeavesAttachmentUnclaimed()
    {
        var (world, _, mail, sp) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        for (int i = 1; i <= Constants.MaxInv; i++) sp.Char.Inv[i].Num = Sword;   // no free slot
        sp.Mail.Add(new MailMessage { Id = 1, Attachments = { new MailAttachment { ItemNum = Armor, Quantity = 1 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Armor), Is.EqualTo(0), "the item was not given");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.False, "so the attachment stays claimable (not eaten)");
        });
    }

    // A mixed message: gold stacks onto an existing pile even with the bag otherwise full; the item that
    // can't fit is left claimable for a later retry once the player frees a slot.
    [Test]
    public void Claim_PartialFit_GoldClaims_ItemStaysUnclaimed()
    {
        var (world, _, mail, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        sp.Char.Inv[1].Num = Gold;
        sp.Char.Inv[1].Quantity = 200;  // an existing gold pile
        for (int i = 2; i <= Constants.MaxInv; i++) sp.Char.Inv[i].Num = Sword;   // every other slot full
        sp.Mail.Add(new MailMessage
        {
            Id = 1,
            Attachments =
            {
                new MailAttachment { ItemNum = Gold, Quantity = 100 },
                new MailAttachment { ItemNum = Armor, Quantity = 1 },
            },
        });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Gold), Is.EqualTo(300), "gold stacked onto the pile");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.True, "the gold stack is claimed");
            Assert.That(sp.Mail[0].Attachments[1].Claimed, Is.False, "the item that couldn't fit stays claimable");
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Armor), Is.EqualTo(0));
        });
    }

    // Gear escrows with Value 0 (durability, not a count), so the claim guard must key on ItemNum, not Value.
    [Test]
    public void Claim_GearValueZero_StillClaims()
    {
        var (world, _, mail, sp) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability =100;
        sp.Mail.Add(new MailMessage { Id = 1, Attachments = { new MailAttachment { ItemNum = Sword, Quantity = 0, Dur = 30 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sp.Char.Inv[1].Num, Is.EqualTo(Sword), "the Value-0 gear stack is still given");
            Assert.That(sp.Char.Inv[1].Dur, Is.EqualTo(30), "with its carried durability");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.True);
        });
    }

    // ── Delivery timing + outbox ──────────────────────────────────────────────────

    // Player-origin mail rides "in transit" for a delay: the recipient's inbox copy AND the sender's outbox
    // receipt are stamped with the same future DeliverAt; the receipt shows the recipient and is pre-claimed.
    [Test]
    public void SendPlayerMail_DelaysBothEnds_AndRecordsOutbox()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Gold].Type = ItemType.Currency;
        var sender = Online(pm, 1, "sender");
        var recipient = Online(pm, 2, "recipient");
        long deliverAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        var attach = new List<MailAttachment> { new() { ItemNum = Gold, Quantity = 500 } };

        mail.SendPlayerMail("sender", "recipient", "hi", "there", attach, deliverAt);

        Assert.Multiple(() =>
        {
            Assert.That(recipient.Mail, Has.Count.EqualTo(1), "recipient got the inbox copy");
            Assert.That(recipient.Mail[0].Sender, Is.EqualTo("sender"));
            Assert.That(recipient.Mail[0].DeliverAt, Is.EqualTo(deliverAt), "recipient copy is stamped in transit");
            Assert.That(recipient.Mail[0].Attachments[0].Claimed, Is.False, "recipient's stack is claimable once matured");

            Assert.That(sender.Outbox, Has.Count.EqualTo(1), "sender got the outbox receipt");
            Assert.That(sender.Outbox[0].Recipient, Is.EqualTo("recipient"), "the receipt shows the To party");
            Assert.That(sender.Outbox[0].DeliverAt, Is.EqualTo(deliverAt), "the receipt mirrors the same delivery time");
            Assert.That(sender.Outbox[0].Attachments[0].Claimed, Is.True, "outbox stacks are a receipt, marked claimed");
            Assert.That(sender.Mail, Is.Empty, "the sender's own inbox is untouched");
        });
    }

    // The load-bearing gate: an in-transit message can't be claimed until it matures.
    [Test]
    public void Claim_InTransit_IsBlocked()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Gold].Type = ItemType.Currency;
        var sp = Online(pm, 1, "tester");
        long future = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        sp.Mail.Add(new MailMessage { Id = 1, DeliverAt = future, Attachments = { new MailAttachment { ItemNum = Gold, Quantity = 500 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Gold), Is.EqualTo(0), "nothing is credited while in transit");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.False, "the stack stays unclaimed");
        });
    }

    [Test]
    public void Claim_AfterMaturity_Succeeds()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Gold].Type = ItemType.Currency;
        var sp = Online(pm, 1, "tester");
        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10;
        sp.Mail.Add(new MailMessage { Id = 1, DeliverAt = past, Attachments = { new MailAttachment { ItemNum = Gold, Quantity = 500 } } });

        mail.Claim(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Gold), Is.EqualTo(500), "a matured message claims normally");
            Assert.That(sp.Mail[0].Attachments[0].Claimed, Is.True);
        });
    }

    // System / notification mail (Deliver with no deliverAt) is instant: DeliverAt == send time, claimable
    // right away, and it writes no outbox receipt.
    [Test]
    public void Deliver_SystemMail_IsInstant_NoOutbox()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Gold].Type = ItemType.Currency;
        var sp = Online(pm, 1, "tester");

        mail.Deliver("tester", "System", "notice", "body", new List<MailAttachment> { new() { ItemNum = Gold, Quantity = 100 } });

        Assert.Multiple(() =>
        {
            Assert.That(sp.Mail, Has.Count.EqualTo(1));
            Assert.That(sp.Mail[0].DeliverAt, Is.EqualTo(sp.Mail[0].TimeUtc), "system mail matures immediately (DeliverAt == send time)");
            Assert.That(sp.Outbox, Is.Empty, "system mail writes no outbox receipt");
        });

        mail.Claim(Idx, 1);
        Assert.That(ItemSystem.CountItem(sp.Char, world.Items, Gold), Is.EqualTo(100), "instant mail is claimable right away");
    }

    // Delete is gated on maturity: an in-transit message can't be deleted until it's delivered.
    [Test]
    public void Delete_InTransit_IsBlocked()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, 1, "tester");
        long future = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        sp.Mail.Add(new MailMessage { Id = 1, DeliverAt = future });

        mail.Delete(Idx, 1, outbox: false);

        Assert.That(sp.Mail, Has.Count.EqualTo(1), "an in-transit message can't be deleted until it matures");
    }

    [Test]
    public void Delete_Delivered_RemovesFromInbox()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, 1, "tester");
        sp.Mail.Add(new MailMessage { Id = 1, DeliverAt = 0 });   // instant/legacy = deletable

        mail.Delete(Idx, 1, outbox: false);

        Assert.That(sp.Mail, Is.Empty, "a delivered message deletes normally");
    }

    // The outbox is a separate copy: deleting it removes only the sender's receipt, not the recipient's inbox.
    [Test]
    public void Delete_Outbox_RemovesOnlySenderCopy()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sender = Online(pm, 1, "sender");
        var recipient = Online(pm, 2, "recipient");
        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10;   // delivered, so deletable
        mail.SendPlayerMail("sender", "recipient", "s", "b", new List<MailAttachment>(), past);
        int outboxId = sender.Outbox[0].Id;

        mail.Delete(1, outboxId, outbox: true);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Outbox, Is.Empty, "the sender's outbox copy is removed");
            Assert.That(recipient.Mail, Has.Count.EqualTo(1), "the recipient's inbox copy is untouched");
        });
    }

    // ── Read state / id assignment / bounded trim ─────────────────────────────────

    [Test]
    public void MarkRead_FlagsTheMessage()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, 1, "tester");
        mail.Deliver("tester", "System", "hi", "", null, 0);
        Assume.That(sp.Mail[0].IsRead, Is.False);

        mail.MarkRead(Idx, sp.Mail[0].Id);

        Assert.That(sp.Mail[0].IsRead, Is.True);
    }

    [Test]
    public void Deliver_AssignsSequentialIds()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, 1, "tester");
        mail.Deliver("tester", "System", "a", "", null, 0);
        mail.Deliver("tester", "System", "b", "", null, 0);
        mail.Deliver("tester", "System", "c", "", null, 0);
        Assert.Multiple(() =>
        {
            Assert.That(sp.Mail[0].Id, Is.EqualTo(1));
            Assert.That(sp.Mail[1].Id, Is.EqualTo(2));
            Assert.That(sp.Mail[2].Id, Is.EqualTo(3));
        });
    }

    // A mailbox is bounded at MaxMailPerAccount; the oldest READ message is evicted first, so unread mail
    // (even older) survives an overflow.
    [Test]
    public void Deliver_BeyondCap_EvictsOldestReadFirst()
    {
        var (_, pm, _, mail) = SetupWorld();
        var sp = Online(pm, 1, "tester");
        for (int i = 0; i < MailSystem.MaxMailPerAccount; i++)
            mail.Deliver("tester", "System", "m", "", null, 0);
        int oldestId = sp.Mail[0].Id;
        int secondOldestId = sp.Mail[1].Id;
        mail.MarkRead(Idx, oldestId);   // the oldest, and the only read message

        mail.Deliver("tester", "System", "overflow", "", null, 0);   // one past the cap

        Assert.Multiple(() =>
        {
            Assert.That(sp.Mail, Has.Count.EqualTo(MailSystem.MaxMailPerAccount), "the mailbox stays bounded at the cap");
            Assert.That(sp.Mail.Exists(m => m.Id == oldestId), Is.False, "the oldest READ message is evicted first");
            Assert.That(sp.Mail.Exists(m => m.Id == secondOldestId), Is.True, "a still-unread older message survives");
        });
    }

    // ── Collect-on-Delivery ───────────────────────────────────────────────────────

    // The per-item marketplace-rate tax, floored: a 100-gold CoD nets 95 with one item, 85 with three.
    [Test]
    public void CodTax_IsMarketRatePerItem_Floored()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MailSystem.CodTax(100, 1), Is.EqualTo(5));
            Assert.That(MailSystem.CodNet(100, 1), Is.EqualTo(95));
            Assert.That(MailSystem.CodTax(100, 3), Is.EqualTo(15));
            Assert.That(MailSystem.CodNet(100, 3), Is.EqualTo(85));
            Assert.That(MailSystem.CodTax(99, 1), Is.EqualTo(4), "the tax floors (99 * 5% = 4.95 -> 4)");
        });
    }

    // The tax's item count excludes gold (the one currency exempt), so attaching gold never inflates its own tax.
    [Test]
    public void CodItemCount_ExcludesGold()
    {
        var attach = new List<MailAttachment>
        {
            new() { ItemNum = Sword, Quantity = 1 },
            new() { ItemNum = Armor, Quantity = 1 },
            new() { ItemNum = Gold, Quantity = 500 },
        };
        Assert.That(MailSystem.CodItemCount(attach), Is.EqualTo(2));
    }

    // A CoD send: the recipient's unclaimed copy carries the price + the short 3-day RETURN clock and stays locked;
    // the sender's outbox receipt carries the price for display but keeps the normal 30-day retention.
    [Test]
    public void SendPlayerMail_Cod_InboxHas3DayReturnClock_OutboxKeepsRetention()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Sword].Type = ItemType.Weapon;
        var sender = Online(pm, 1, "sender");
        var recipient = Online(pm, 2, "recipient");
        long deliverAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        var attach = new List<MailAttachment> { new() { ItemNum = Sword, Quantity = 1 } };

        mail.SendPlayerMail("sender", "recipient", "cod", "pay up", attach, deliverAt, codPrice: 100);

        Assert.Multiple(() =>
        {
            Assert.That(recipient.Mail[0].CodPrice, Is.EqualTo(100), "the recipient copy is a CoD");
            Assert.That(recipient.Mail[0].DeleteAt, Is.EqualTo(deliverAt + Constants.CodLifetimeSeconds),
                "the unpaid recipient copy rides the 3-day return clock");
            Assert.That(recipient.Mail[0].Attachments[0].Claimed, Is.False, "the items stay locked until paid");

            Assert.That(sender.Outbox[0].CodPrice, Is.EqualTo(100), "the outbox receipt carries the price for display");
            Assert.That(sender.Outbox[0].DeleteAt, Is.EqualTo(deliverAt + Constants.MailRetentionSeconds),
                "but the receipt keeps the normal 30-day retention");
        });
    }

    // Paying a CoD (gold already charged by the handler) releases the locked items into the receiver, mails the
    // taxed net to the sender, and converts the message into an ordinary claimed mail on the 30-day retention.
    [Test]
    public void CompleteCod_ReleasesItems_MailsNetToSender_ConvertsToNormal()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability =50;
        var sender = Online(pm, 1, "sender");
        var receiver = Online(pm, 2, "receiver");
        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10;
        receiver.Mail.Add(new MailMessage
        {
            Id = 1, Sender = "sender", DeliverAt = past, DeleteAt = past + Constants.CodLifetimeSeconds,
            CodPrice = 100, Attachments = { new MailAttachment { ItemNum = Sword, Quantity = 1, Dur = 40 } },
        });

        mail.CompleteCod(2, 1);

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Char.Inv[1].Num, Is.EqualTo(Sword), "the item is released into the receiver's bag");
            Assert.That(receiver.Char.Inv[1].Dur, Is.EqualTo(40), "with its carried durability");
            Assert.That(receiver.Mail[0].CodPrice, Is.EqualTo(0), "the message is now a normal (paid) mail");
            Assert.That(receiver.Mail[0].Attachments[0].Claimed, Is.True, "its attachment is claimed");
            Assert.That(receiver.Mail[0].DeleteAt, Is.EqualTo(past + Constants.MailRetentionSeconds), "on the 30-day retention");

            Assert.That(sender.Mail, Has.Count.EqualTo(1), "the sender is mailed the net gold");
            Assert.That(sender.Mail[0].Attachments[0].ItemNum, Is.EqualTo(Gold));
            Assert.That(sender.Mail[0].Attachments[0].Quantity, Is.EqualTo(95), "100 price minus the 5% single-item tax");
        });
    }

    // An unpaid CoD that reaches its return deadline is mailed back to the sender with items intact — not deleted.
    [Test]
    public void TickExpiry_UnpaidCod_ReturnsItemsToSender()
    {
        var (world, pm, _, mail) = SetupWorld();
        world.Items[Sword].Type = ItemType.Weapon;
        var sender = Online(pm, 1, "sender");
        var receiver = Online(pm, 2, "receiver");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        receiver.Mail.Add(new MailMessage
        {
            Id = 1, Sender = "sender", DeliverAt = now - 100, DeleteAt = now - 1,   // matured, past its 3-day return
            CodPrice = 100, Attachments = { new MailAttachment { ItemNum = Sword, Quantity = 1, Dur = 33 } },
        });

        mail.TickExpiry();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Mail, Is.Empty, "the unpaid CoD leaves the receiver's inbox");
            Assert.That(sender.Mail, Has.Count.EqualTo(1), "and is returned to the sender");
            Assert.That(sender.Mail[0].Attachments[0].ItemNum, Is.EqualTo(Sword), "with the item intact");
            Assert.That(sender.Mail[0].Attachments[0].Dur, Is.EqualTo(33), "durability preserved");
            Assert.That(sender.Mail[0].Attachments[0].Claimed, Is.False, "unclaimed so the sender can reclaim it");
            Assert.That(sender.Mail[0].CodPrice, Is.EqualTo(0), "the return is a normal mail, not a CoD");
        });
    }

    // The all-or-nothing room pre-check: two items need two free slots; gold stacks onto an existing pile for free.
    [Test]
    public void CanReceiveAll_ChecksRoomForTheWholeBatch()
    {
        var (world, _, _, sp) = Setup();
        world.Items[Gold].Type = ItemType.Currency;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Armor].Type = ItemType.Armor;
        var twoItems = new List<MailAttachment>
        {
            new() { ItemNum = Sword, Quantity = 1 },
            new() { ItemNum = Armor, Quantity = 1 },
        };

        Assert.That(ItemSystem.CanReceiveAll(sp.Char, world.Items, twoItems), Is.True, "an empty bag has room for two");

        for (int i = 2; i <= Constants.MaxInv; i++) sp.Char.Inv[i].Num = Sword;   // leave only slot 1 free
        Assert.That(ItemSystem.CanReceiveAll(sp.Char, world.Items, twoItems), Is.False, "two items can't fit one slot");

        sp.Char.Inv[1].Num = Gold;
        sp.Char.Inv[1].Quantity = 50;  // slot 1 is now an existing gold pile; bag otherwise full
        var goldOnly = new List<MailAttachment> { new() { ItemNum = Gold, Quantity = 100 } };
        Assert.That(ItemSystem.CanReceiveAll(sp.Char, world.Items, goldOnly), Is.True, "gold stacks onto the pile, needs no slot");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // No-op packet dispatcher (per-file convention). Mail ops only fan out to it (SyncTo).
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
