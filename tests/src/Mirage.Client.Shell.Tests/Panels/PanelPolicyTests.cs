using Mirage.Client.Shell.Panels;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// The per-panel policy table: five facts about every panel, held as data rather than as switches and
/// boolean chains inside GameplayScreen. The registry keeps only what needs a live panel and a frame,
/// so the policy itself is reachable without a graphics device — which is what makes it assertable.
///
/// <para>Two of these tests lock behavior that is arguably WRONG but deliberately preserved. That is
/// the point: an unlocked quirk gets "fixed" by accident during unrelated work and nobody notices the
/// behavior change. A locked one fails the build, and whoever changes it does so on purpose.</para>
/// </summary>
[TestFixture]
public class PanelPolicyTests
{
    static PanelPolicy P(int slot) => PanelPolicies.BySlot[slot];

    [Test]
    public void EverySlot_HasAPolicy()
    {
        Assert.That(PanelPolicies.BySlot, Has.Length.EqualTo(PanelSlots.Count));
        // A default-constructed row means a slot was added to PanelSlots but not to the table: it would
        // silently be non-blocking, non-persisting and invisible to Escape.
        Assert.Multiple(() =>
        {
            for (int slot = 0; slot < PanelSlots.Count; slot++)
            {
                Assert.That(P(slot), Is.Not.EqualTo(default(PanelPolicy)),
                            $"slot {slot} has no policy row — every slot in PanelSlots needs one");
            }
        });
    }

    // ── Escape participation ───────────────────────────────────────────────────

    // Every player-opened panel must count for Escape, so Escape closes the topmost one rather than
    // raising the quit dialog. A single panel missing from the table is enough to break that: with only
    // that panel open, Escape offers to quit the game. Asserting over EVERY slot rather than a sample is
    // what stops a newly added panel slipping through.
    [Test]
    public void EveryPlayerOpenedPanel_CountsForEscape()
    {
        // Trade is the sole legitimate exclusion: Escape CANCELS the trade (a server round-trip) rather
        // than closing the window, and that is handled ahead of the generic escape path.
        Assert.Multiple(() =>
        {
            for (int slot = 0; slot < PanelSlots.Count; slot++)
            {
                if (slot == PanelSlots.Trade) continue;
                Assert.That(P(slot).CountsAsOpenForEscape, Is.True,
                            $"slot {slot} is open-able but Escape ignores it, so Escape would offer to "
                            + "quit the game instead of closing it");
            }
        });
    }

    // Named directly as well as covered by the sweep above: Controls and Help are the same kind of
    // read-only reference window, so any divergence between them under Escape is an oversight rather
    // than a decision, and pairing them in one assertion is what makes that visible.
    [Test]
    public void Controls_CountsForEscape_LikeItsSiblingHelp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(P(PanelSlots.Controls).CountsAsOpenForEscape, Is.True,
                        "regression guard: Escape must close the Controls panel, not offer to quit");
            Assert.That(P(PanelSlots.Help).CountsAsOpenForEscape, Is.True,
                        "Help and Controls are the same kind of panel and must behave alike");
        });
    }

    [Test]
    public void Trade_IsExcludedFromEscape_BecauseEscapeCancelsTheTrade()
    {
        Assert.That(P(PanelSlots.Trade).CountsAsOpenForEscape, Is.False,
                    "Escape on an open trade cancels it server-side; it never reaches the generic path");
    }

    // ── Quirk #2: Market and Trade survive a screen teardown ───────────────────

    // CORRECT behavior, and deliberate: both are live server-tracked sessions.
    // Closing them client-side on OnExit would leave the two ends disagreeing about whether the window
    // is up. Every other panel does close.
    [Test]
    public void MarketAndTrade_DoNotCloseOnLeave_BecauseTheServerTracksThem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(P(PanelSlots.Market).ClosesOnLeave, Is.False, "market is a server-tracked session");
            Assert.That(P(PanelSlots.Trade).ClosesOnLeave, Is.False, "trade is a server-tracked session");
        });
    }

    [Test]
    public void EveryOtherPanel_ClosesOnLeave()
    {
        int[] exempt = [PanelSlots.Market, PanelSlots.Trade];
        Assert.Multiple(() =>
        {
            for (int slot = 0; slot < PanelSlots.Count; slot++)
            {
                if (exempt.Contains(slot)) continue;
                Assert.That(P(slot).ClosesOnLeave, Is.True,
                            $"slot {slot} must close when the screen tears down");
            }
        });
    }

    // ── The movement-locking set ───────────────────────────────────────────────

    // The documented membership: the shop/bank/inn/market/trade/mail/training counters PLUS the quest
    // log/dialog and conversation panels. Pinned as an exact set, so adding a panel that should lock
    // movement (or removing one that should not) is a visible decision.
    [Test]
    public void MovementBlockingSet_IsExactlyTheDocumentedMembership()
    {
        int[] expected =
        [
            PanelSlots.Training, PanelSlots.Shop, PanelSlots.Bank, PanelSlots.Inn, PanelSlots.Mail,
            PanelSlots.Market, PanelSlots.Trade, PanelSlots.QuestLog, PanelSlots.QuestDialog,
            PanelSlots.Conversation,
        ];

        var actual = Enumerable.Range(0, PanelSlots.Count).Where(s => P(s).BlocksMovement).ToArray();
        Assert.That(actual, Is.EquivalentTo(expected),
                    "the movement-locking set changed — confirm that is intended");
    }

    // Browsing your bag or your stats must NOT freeze you in place; that is the whole distinction
    // between a floating panel and a counter you are standing at.
    [Test]
    public void InformationalPanels_DoNotLockMovement()
    {
        Assert.Multiple(() =>
        {
            // Moderation is here for the same reason as the rest: a Creator reading a list of who is
            // punished should still be able to walk away from whatever is happening around them.
            foreach (int slot in new[] { PanelSlots.Inventory, PanelSlots.Spells, PanelSlots.Stats,
                                         PanelSlots.Help, PanelSlots.Controls, PanelSlots.Social,
                                         PanelSlots.Options, PanelSlots.Moderation })
            {
                Assert.That(P(slot).BlocksMovement, Is.False, $"slot {slot} must not freeze the player");
            }
        });
    }

    // ── Persistence keys ──────────────────────────────────────────────────────

    // Two panels sharing a key would silently overwrite each other's saved position.
    [Test]
    public void ConfigKeys_AreUnique()
    {
        var keys = Enumerable.Range(0, PanelSlots.Count)
            .Select(s => P(s).ConfigKey).Where(k => k is not null).ToList();

        Assert.That(keys, Is.Unique, "two panels sharing a config key would clobber each other's position");
    }

    // The server-driven dialogs appear where the game puts them, so they have no saved position. That
    // is a decision, not an omission — pin it.
    [Test]
    public void ServerPlacedDialogs_HaveNoPersistedPosition()
    {
        Assert.Multiple(() =>
        {
            foreach (int slot in new[] { PanelSlots.QuestLog, PanelSlots.QuestDialog,
                                         PanelSlots.Conversation, PanelSlots.Options })
            {
                Assert.That(P(slot).ConfigKey, Is.Null,
                            $"slot {slot} positions itself, so it must not persist a position");
            }
        });
    }

    // ── Player-toggleable set ─────────────────────────────────────────────────

    // The server-driven panels have no toggle entry point: a keybind must not be able to conjure a shop
    // or a trade window the server does not know about. The quest LOG is the near-miss here — it is
    // player-opened (J) and was once missing from the toggle dispatch, which made the key silently do
    // nothing.
    [Test]
    public void ServerDrivenPanels_AreNotPlayerToggleable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(P(PanelSlots.Shop).PlayerToggleable, Is.False, "a keybind must not open a shop");
            Assert.That(P(PanelSlots.Trade).PlayerToggleable, Is.False, "a keybind must not open a trade");
            Assert.That(P(PanelSlots.QuestDialog).PlayerToggleable, Is.False);
            Assert.That(P(PanelSlots.Conversation).PlayerToggleable, Is.False);

            Assert.That(P(PanelSlots.QuestLog).PlayerToggleable, Is.True,
                        "the quest LOG is player-opened (J) — it was once missing from the toggle "
                        + "dispatch, which made the key appear to do nothing");
        });
    }

    // ── The query helpers ─────────────────────────────────────────────────────

    [Test]
    public void AnyBlocksMovement_TrueOnlyForABlockingOpenPanel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PanelPolicies.AnyBlocksMovement(_ => false), Is.False, "nothing open");
            Assert.That(PanelPolicies.AnyBlocksMovement(s => s == PanelSlots.Inventory), Is.False,
                        "inventory open does not lock movement");
            Assert.That(PanelPolicies.AnyBlocksMovement(s => s == PanelSlots.Bank), Is.True,
                        "the bank counter does");
            Assert.That(PanelPolicies.AnyBlocksMovement(_ => true), Is.True, "everything open");
        });
    }

    // Driven through the query the escape handler actually calls, so this covers the wiring and not
    // just the table.
    [Test]
    public void AnyOpenForEscape_TrueForAnyEscapeClosablePanel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PanelPolicies.AnyOpenForEscape(_ => false), Is.False,
                        "nothing open — Escape raises the quit dialog");
            Assert.That(PanelPolicies.AnyOpenForEscape(s => s == PanelSlots.Inventory), Is.True);
            Assert.That(PanelPolicies.AnyOpenForEscape(s => s == PanelSlots.Controls), Is.True,
                        "the fixed case: Controls alone must make Escape close it, not offer to quit");
            Assert.That(PanelPolicies.AnyOpenForEscape(s => s == PanelSlots.Trade), Is.False,
                        "trade alone is handled by the cancel path ahead of this query");
        });
    }

    // ── Slot numbering ────────────────────────────────────────────────────────

    // The registry is an array indexed by these, and the z-order list holds them, so they must be a
    // dense 0..Count-1 range with no gaps or duplicates.
    [Test]
    public void SlotNumbers_AreDenseAndUnique()
    {
        int[] slots =
        [
            PanelSlots.Inventory, PanelSlots.Spells, PanelSlots.Training, PanelSlots.Shop,
            PanelSlots.Options, PanelSlots.Stats, PanelSlots.Help, PanelSlots.Controls,
            PanelSlots.Bank, PanelSlots.Inn, PanelSlots.Mail, PanelSlots.Social,
            PanelSlots.Market, PanelSlots.Trade, PanelSlots.QuestLog, PanelSlots.QuestDialog,
            PanelSlots.Conversation, PanelSlots.Moderation,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(slots, Is.Unique, "two panels sharing a slot would overwrite each other in the registry");
            Assert.That(slots, Has.Length.EqualTo(PanelSlots.Count), "Count must match the slot list");
            Assert.That(slots.OrderBy(s => s), Is.EqualTo(Enumerable.Range(0, PanelSlots.Count)),
                        "slots must be a dense 0..Count-1 range — the registry is a flat array");
        });
    }
}
