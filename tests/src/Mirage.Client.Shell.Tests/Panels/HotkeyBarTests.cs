using Microsoft.Xna.Framework;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>The action bar's non-drawing behavior: where its four boxes are, and how a bound item or
/// spell NUMBER is resolved to a live inventory/spellbook slot at the moment of use. That resolution is
/// the whole reason hotkeys store numbers rather than positions, so it is what these pin down.</summary>
[TestFixture]
public class HotkeyBarTests
{
    private static ClientState StateWith(params (int InvSlot, int ItemNum)[] bag)
    {
        var state = new ClientState();
        state.Me.Inv = new PlayerInvSlot[Constants.MaxInv + 1];
        for (int i = 0; i < state.Me.Inv.Length; i++) state.Me.Inv[i] = new PlayerInvSlot();
        state.Me.Spell = new int[Constants.MaxPlayerSpells + 1];
        state.Me.Hotkeys = PlayerHotkey.NewBar();
        foreach (var (slot, num) in bag) state.Me.Inv[slot].Num = num;
        return state;
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    [Test]
    public void Slots_AreAdjacentAndNonOverlapping()
    {
        for (int i = 1; i < Constants.MaxHotkeys; i++)
        {
            var a = HotkeyBarPanel.SlotBounds(i);
            var b = HotkeyBarPanel.SlotBounds(i + 1);
            Assert.Multiple(() =>
            {
                Assert.That(a.Intersects(b), Is.False, $"slots {i} and {i + 1} overlap");
                Assert.That(b.X, Is.GreaterThan(a.Right), "slots should run left to right with a gap");
                Assert.That(b.Y, Is.EqualTo(a.Y), "the row should be flat");
            });
        }
    }

    [Test]
    public void Bounds_ContainsEverySlot()
    {
        for (int i = 1; i <= Constants.MaxHotkeys; i++)
            Assert.That(HotkeyBarPanel.Bounds.Contains(HotkeyBarPanel.SlotBounds(i)), Is.True, $"slot {i} escapes Bounds");
    }

    [Test]
    public void SlotAt_RoundTripsEverySlotCenter_AndMissesElsewhere()
    {
        Assert.Multiple(() =>
        {
            for (int i = 1; i <= Constants.MaxHotkeys; i++)
                Assert.That(HotkeyBarPanel.SlotAt(HotkeyBarPanel.SlotBounds(i).Center), Is.EqualTo(i));
            // Well clear of the bar in both axes.
            Assert.That(HotkeyBarPanel.SlotAt(new Point(0, 0)), Is.EqualTo(0));
            Assert.That(HotkeyBarPanel.SlotAt(new Point(HotkeyBarPanel.Bounds.Right + 40, HotkeyBarPanel.Bounds.Y)), Is.EqualTo(0));
        });
    }

    // The bar draws above the link strip and must not sit on top of it — the two are stacked chrome, and
    // an overlap would put a hotkey box over the Mail/Options/Help row.
    [Test]
    public void Bar_SitsAboveTheLinkStrip()
        => Assert.That(HotkeyBarPanel.Bounds.Bottom, Is.LessThan(582), "the link strip starts at y=582");

    // ── Resolution ───────────────────────────────────────────────────────────

    [Test]
    public void FindInvSlot_ReturnsTheFirstMatchingSlot()
    {
        var state = StateWith((3, 42), (7, 42));
        Assert.That(HotkeyBarPanel.FindInvSlot(state, 42), Is.EqualTo(3), "the lowest slot wins, as the old potion scan did");
    }

    [Test]
    public void FindInvSlot_ZeroWhenTheBagHasNone()
        => Assert.That(HotkeyBarPanel.FindInvSlot(StateWith((3, 42)), 99), Is.EqualTo(0));

    // The point of binding by number: the bag reorders under the player constantly, and the same binding
    // has to keep finding the item wherever it lands.
    [Test]
    public void FindInvSlot_FollowsTheItemWhenTheBagReorders()
    {
        var state = StateWith((3, 42));
        Assert.That(HotkeyBarPanel.FindInvSlot(state, 42), Is.EqualTo(3));

        state.Me.Inv[3].Num = 0;      // drank/dropped it
        state.Me.Inv[9].Num = 42;     // picked another up, elsewhere
        Assert.That(HotkeyBarPanel.FindInvSlot(state, 42), Is.EqualTo(9));
    }

    [Test]
    public void FindSpellSlot_FindsAKnownSpellAndMissesAnUnknownOne()
    {
        var state = StateWith();
        state.Me.Spell[4] = 17;
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.FindSpellSlot(state, 17), Is.EqualTo(4));
            Assert.That(HotkeyBarPanel.FindSpellSlot(state, 18), Is.EqualTo(0));
        });
    }

    [Test]
    public void FindSlots_RejectNonPositiveNumbers()
    {
        var state = StateWith((3, 42));
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.FindInvSlot(state, 0), Is.EqualTo(0));
            Assert.That(HotkeyBarPanel.FindInvSlot(state, -1), Is.EqualTo(0));
            Assert.That(HotkeyBarPanel.FindSpellSlot(state, 0), Is.EqualTo(0));
        });
    }

    // Availability is what grays a slot. An out-of-stock binding stays BOUND — it just can't fire — so the
    // player can see which potion they have run out of instead of the slot silently emptying itself.
    [Test]
    public void IsAvailable_TracksStockWithoutUnbinding()
    {
        var state = StateWith((3, 42));
        var hk = new PlayerHotkey(HotkeyKind.Item, 42);
        Assert.That(HotkeyBarPanel.IsAvailable(state, hk), Is.True);

        state.Me.Inv[3].Num = 0;
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.IsAvailable(state, hk), Is.False);
            Assert.That(hk.IsBound, Is.True, "running out must not clear the binding");
        });
    }

    [Test]
    public void IsAvailable_IsFalseForAnEmptySlot()
        => Assert.That(HotkeyBarPanel.IsAvailable(StateWith(), PlayerHotkey.Empty), Is.False);

    // ── Button mapping ───────────────────────────────────────────────────────

    // The order is not arbitrary: it preserves the old potion layout (X=HP, Y=MP, B=SP) so existing muscle
    // memory keeps working, with slot 4 taking A.
    [Test]
    public void GamepadFace_PreservesTheOldPotionLayout()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.GamepadFace(1), Is.EqualTo("X"));
            Assert.That(HotkeyBarPanel.GamepadFace(2), Is.EqualTo("Y"));
            Assert.That(HotkeyBarPanel.GamepadFace(3), Is.EqualTo("B"));
            Assert.That(HotkeyBarPanel.GamepadFace(4), Is.EqualTo("A"));
        });
    }

    // Same four physical positions, Sony's names. If these ever drift apart the pad would show a player
    // the wrong button, which is worse than showing none.
    [Test]
    public void PlayStationFace_MirrorsTheXboxPositions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.PlayStationFace(1), Is.EqualTo(GamepadGlyphs.PsFace.Square));   // X
            Assert.That(HotkeyBarPanel.PlayStationFace(2), Is.EqualTo(GamepadGlyphs.PsFace.Triangle)); // Y
            Assert.That(HotkeyBarPanel.PlayStationFace(3), Is.EqualTo(GamepadGlyphs.PsFace.Circle));   // B
            Assert.That(HotkeyBarPanel.PlayStationFace(4), Is.EqualTo(GamepadGlyphs.PsFace.Cross));    // A
        });
    }

    [Test]
    public void EverySlot_HasBothAFaceLetterAndAShape()
    {
        var letters = new HashSet<string>();
        var shapes = new HashSet<GamepadGlyphs.PsFace>();
        for (int i = 1; i <= Constants.MaxHotkeys; i++)
        {
            letters.Add(HotkeyBarPanel.GamepadFace(i));
            shapes.Add(HotkeyBarPanel.PlayStationFace(i));
        }
        Assert.Multiple(() =>
        {
            Assert.That(letters, Has.Count.EqualTo(Constants.MaxHotkeys), "two slots share a face button");
            Assert.That(shapes, Has.Count.EqualTo(Constants.MaxHotkeys), "two slots share a PlayStation shape");
        });
    }
}
