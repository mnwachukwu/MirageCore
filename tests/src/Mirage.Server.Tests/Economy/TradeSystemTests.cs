using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Mirage.Server.Tests.Economy;

/// <summary>Direct player-to-player trade on <see cref="TradeSystem"/>: the request/accept handshake, the r=5
/// proximity gate, escrow-on-offer with confirm invalidation, the atomic all-or-nothing swap (incl. the
/// no-space refusal), and offer-return on cancel / disconnect. Locks the dupe-critical invariants: items are
/// escrowed off the offerer, a swap either moves everything or nothing, and no teardown loses an item.</summary>
[TestFixture]
public class TradeSystemTests
{
    const int Sword = 10, Shield = 11;

    static (GameWorld world, PlayerManager pm, TradeSystem trade) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var mail = new MailSystem(pm, dispatcher, saver, items);
        var trade = new TradeSystem(world, pm, dispatcher, items, mail, persistence: null!, saver: null!);
        return (world, pm, trade);
    }

    static ServerPlayer AtPos(PlayerManager pm, int idx, string name, int x, int y)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "acc" + idx;
        sp.Char.Name = name;
        sp.Char.Map = 1;
        sp.Char.X = x;
        sp.Char.Y = y;
        return sp;
    }

    // Two adjacent players in an accepted, active trade.
    static (ServerPlayer a, ServerPlayer b) ActiveTrade(PlayerManager pm, TradeSystem trade)
    {
        var a = AtPos(pm, 1, "Alice", 5, 5);
        var b = AtPos(pm, 2, "Bob", 6, 5);
        trade.Request(1, "Bob");
        trade.Respond(2, accept: true);
        return (a, b);
    }

    [Test]
    public void RequestAccept_BothEnterTrade()
    {
        var (_, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        Assert.Multiple(() =>
        {
            Assert.That(a.InTrade, Is.True);
            Assert.That(b.InTrade, Is.True);
            Assert.That(a.TradePartner, Is.EqualTo(2));
            Assert.That(b.TradePartner, Is.EqualTo(1));
        });
    }

    [Test]
    public void Request_OutOfRange_Refused()
    {
        var (_, pm, trade) = Setup();
        var a = AtPos(pm, 1, "Alice", 5, 5);
        _ = AtPos(pm, 2, "Bob", 15, 5);   // 10 tiles away, outside the r=5 circle

        trade.Request(1, "Bob");

        Assert.That(a.TradeStarter, Is.False, "an out-of-range invite is refused");
    }

    [Test]
    public void OfferAdd_EscrowsItem_ClearsBothConfirms()
    {
        var (world, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        a.Char.Inv[3].Num = Sword;
        a.Char.Inv[3].Dur = 40;
        a.TradeConfirmed = b.TradeConfirmed = true;   // pretend both had confirmed

        trade.OfferAdd(1, invSlot: 3, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(a.Char.Inv[3].Num, Is.EqualTo(0), "the item is escrowed off the offerer");
            Assert.That(a.TradeOffer, Has.Count.EqualTo(1));
            Assert.That(a.TradeOffer[0].Num, Is.EqualTo(Sword));
            Assert.That(a.TradeOffer[0].Dur, Is.EqualTo(40), "durability rides along");
            Assert.That(a.TradeConfirmed, Is.False, "changing an offer clears both confirms");
            Assert.That(b.TradeConfirmed, Is.False);
        });
    }

    [Test]
    public void OfferAdd_NonTradeable_Refused()
    {
        // Server backstop for the client's trade filter: a NonTradeable item can't be staged.
        var (world, pm, trade) = Setup();
        var (a, _) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].NonTradeable = true;   // e.g. valor / a soulbound item
        a.Char.Inv[3].Num = Sword;

        trade.OfferAdd(1, invSlot: 3, amount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(a.Char.Inv[3].Num, Is.EqualTo(Sword), "a non-tradeable item is not escrowed");
            Assert.That(a.TradeOffer, Is.Empty, "and never enters the offer");
        });
    }

    [Test]
    public void BothConfirm_SwapsAtomically_AndEnds()
    {
        var (world, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Shield].Type = ItemType.Armor;
        a.Char.Inv[3].Num = Sword;
        b.Char.Inv[3].Num = Shield;
        trade.OfferAdd(1, 3, 0);   // Alice offers her Sword
        trade.OfferAdd(2, 3, 0);   // Bob offers his Shield

        trade.Confirm(1, true);
        trade.Confirm(2, true);    // both confirmed → atomic swap

        Assert.Multiple(() =>
        {
            Assert.That(a.InTrade, Is.False, "the trade ends on completion");
            Assert.That(b.InTrade, Is.False);
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => a.Char.Inv[i].Num == Shield), Is.True, "Alice received Bob's item");
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => b.Char.Inv[i].Num == Sword), Is.True, "Bob received Alice's item");
            Assert.That(a.TradeOffer, Is.Empty);
            Assert.That(b.TradeOffer, Is.Empty);
        });
    }

    [Test]
    public void Cancel_ReturnsBothOffers_AndEnds()
    {
        var (world, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        a.Char.Inv[3].Num = Sword;
        trade.OfferAdd(1, 3, 0);

        trade.Cancel(1);

        Assert.Multiple(() =>
        {
            Assert.That(a.InTrade, Is.False);
            Assert.That(b.InTrade, Is.False);
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => a.Char.Inv[i].Num == Sword), Is.True, "the escrowed item is returned");
            Assert.That(a.TradeOffer, Is.Empty);
        });
    }

    // Atomicity: if either side can't hold the incoming offer, the whole swap is refused and offers survive.
    [Test]
    public void BothConfirm_NoSpace_SwapRefused()
    {
        var (world, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Shield].Type = ItemType.Armor;
        b.Char.Inv[3].Num = Shield;
        trade.OfferAdd(2, 3, 0);                                         // Bob offers his Shield (frees slot 3)
        for (int i = 1; i <= Constants.MaxInv; i++) b.Char.Inv[i].Num = Sword;   // then fill Bob's bag solid
        a.Char.Inv[3].Num = Sword;
        trade.OfferAdd(1, 3, 0);                                         // Alice offers her Sword

        trade.Confirm(1, true);
        trade.Confirm(2, true);    // both confirm, but Bob has no room for Alice's item

        Assert.Multiple(() =>
        {
            Assert.That(a.InTrade, Is.True, "the trade survives a no-space swap");
            Assert.That(a.TradeConfirmed, Is.False, "both confirms are cleared so they can adjust");
            Assert.That(b.TradeConfirmed, Is.False);
            Assert.That(a.TradeOffer, Has.Count.EqualTo(1), "offers are retained");
            Assert.That(b.TradeOffer, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void OnPlayerGone_ReturnsOffers_AndEnds()
    {
        var (world, pm, trade) = Setup();
        var (a, b) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        a.Char.Inv[3].Num = Sword;
        trade.OfferAdd(1, 3, 0);

        trade.OnPlayerGone(1);   // Alice disconnects mid-trade

        Assert.Multiple(() =>
        {
            Assert.That(a.InTrade, Is.False, "the trade ends for both");
            Assert.That(b.InTrade, Is.False);
            Assert.That(Enumerable.Range(1, Constants.MaxInv).Any(i => a.Char.Inv[i].Num == Sword), Is.True, "the escrowed item is returned to the leaver");
        });
    }

    // Crash-safety: an item escrowed into a trade must ride the character's SAVE snapshot (Clone), so a
    // periodic save / shutdown persists it instead of the bag-minus-item alone — otherwise a crash mid-trade
    // wipes it. And the snapshot must be an independent deep copy (the game thread mutates the live escrow
    // while the save runs off-thread).
    [Test]
    public void EscrowedOffer_IsCapturedByCharacterSaveSnapshot()
    {
        var (world, pm, trade) = Setup();
        var (a, _) = ActiveTrade(pm, trade);
        world.Items[Sword].Type = ItemType.Weapon;
        a.Char.Inv[3].Num = Sword;
        a.Char.Inv[3].Dur = 30;
        trade.OfferAdd(1, invSlot: 3, amount: 0);   // escrowed off the bag into Char.TradeOffer

        var snapshot = a.Char.Clone();              // exactly what the background char save writes to disk

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TradeOffer, Has.Count.EqualTo(1), "the escrow rides the character save");
            Assert.That(snapshot.TradeOffer[0].Num, Is.EqualTo(Sword));
            Assert.That(snapshot.TradeOffer[0].Dur, Is.EqualTo(30), "durability persists");
            Assert.That(snapshot.Inv[3].Num, Is.EqualTo(0), "and it's gone from the persisted bag (no dupe)");
        });

        a.TradeOffer.Clear();   // mutate the live escrow after the snapshot
        Assert.That(snapshot.TradeOffer, Has.Count.EqualTo(1), "the snapshot is an independent deep copy");
    }

    // On login, escrow persisted by a crash / shutdown that skipped the leave-path unwind is returned to the
    // bag — a restart can't wipe items that were mid-trade. A live trade never resumes across a restart.
    [Test]
    public void RecoverEscrowOnLogin_ReturnsPersistedEscrow_ToBag()
    {
        var (world, pm, trade) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        var a = AtPos(pm, 1, "Alice", 5, 5);
        a.Char.TradeOffer.Add(new PlayerInvSlot { Num = Sword, Quantity = 1, Dur = 25 });   // as loaded from disk post-crash

        trade.RecoverEscrowOnLogin(1);

        int slot = Enumerable.Range(1, Constants.MaxInv).FirstOrDefault(i => a.Char.Inv[i].Num == Sword);
        Assert.Multiple(() =>
        {
            Assert.That(a.Char.TradeOffer, Is.Empty, "the escrow is drained on login");
            Assert.That(slot, Is.GreaterThan(0), "the escrowed item is back in the bag");
            Assert.That(a.Char.Inv[slot].Dur, Is.EqualTo(25), "durability preserved");
        });
    }

    // FULL crash-tear recovery (the two-phase-commit journal): a journal survives on disk with one side
    // already applied (escrow empty) and the other NOT (escrow intact) — the classic cross-file dupe+loss
    // window. RecoverJournalsAsync must finish only the unapplied side, touching neither the applied side nor
    // dropping/duping any item. Uses a real temp-dir JsonPersistenceService end to end.
    [Test]
    public async Task RecoverJournals_FinishesTornSwap_NoDupeNoLoss()
    {
        const int Sword = 10, Shield = 11;
        string dir = Path.Combine(Path.GetTempPath(), "mirage-tradejournal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var persistence = new JsonPersistenceService(dir, dir, NullLogger<JsonPersistenceService>.Instance, new NoOpChatLog());
            var world = new GameWorld();
            world.Items[Sword].Type = ItemType.Weapon;
            world.Items[Shield].Type = ItemType.Armor;
            var pm = new PlayerManager();
            var dispatcher = new NoOpDispatcher();
            var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
            var saver = new PlayerSaver(persistence, NullLogger<PlayerSaver>.Instance);
            var mail = new MailSystem(pm, dispatcher, saver, items);
            var trade = new TradeSystem(world, pm, dispatcher, items, mail, persistence, saver);

            // A staged the Sword, B staged the Shield; the swap gives A the Shield and B the Sword.
            // Torn crash: A's post-swap write LANDED (bag has Shield, escrow empty); B's did NOT (escrow still
            // holds the Shield, no Sword) — pre-swap on disk.
            var accA = new AccountRecord { Login = "alice" };
            accA.Chars[1].Name = "Alice";
            accA.Chars[1].Inv[1].Num = Shield;
            accA.Chars[1].Inv[1].Quantity = 1;  // already received
            await persistence.SaveAccountAsync(accA);

            var accB = new AccountRecord { Login = "bob" };
            accB.Chars[1].Name = "Bob";
            accB.Chars[1].TradeOffer.Add(new PlayerInvSlot { Num = Shield, Quantity = 1 });   // still escrowed
            await persistence.SaveAccountAsync(accB);

            persistence.SaveTradeJournal(new TradeJournal
            {
                Id = 1,
                ALogin = "alice", AChar = 1, AReceives = new() { new PlayerInvSlot { Num = Shield, Quantity = 1 } },
                BLogin = "bob", BChar = 1, BReceives = new() { new PlayerInvSlot { Num = Sword, Quantity = 1 } },
            });

            await trade.RecoverJournalsAsync();

            var a = (await persistence.LoadAccountAsync("alice"))!.Chars[1];
            var b = (await persistence.LoadAccountAsync("bob"))!.Chars[1];
            Assert.Multiple(() =>
            {
                // A was skipped (escrow empty ⇒ already applied): still exactly one Shield, never re-gains the Sword.
                Assert.That(CountItem(a, Shield), Is.EqualTo(1), "A keeps its received Shield (no dupe)");
                Assert.That(CountItem(a, Sword), Is.EqualTo(0), "A never re-gains the Sword it gave away");
                Assert.That(a.TradeOffer, Is.Empty);
                // B was completed: receives the Sword, escrow cleared, and its staged Shield is NOT returned to it.
                Assert.That(CountItem(b, Sword), Is.EqualTo(1), "B receives the Sword (no loss)");
                Assert.That(CountItem(b, Shield), Is.EqualTo(0), "B's staged Shield went to A, not back to B");
                Assert.That(b.TradeOffer, Is.Empty, "B's escrow is cleared");
            });

            Assert.That(await persistence.LoadAllTradeJournalsAsync(), Is.Empty, "the journal is deleted after recovery");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    static int CountItem(PlayerRecord p, int itemNum)
    {
        int n = 0;
        for (int i = 1; i <= Constants.MaxInv; i++) if (p.Inv[i].Num == itemNum) n++;
        return n;
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class NoOpChatLog : IChatLog { public void Write(string message, string chatType) { } }

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
