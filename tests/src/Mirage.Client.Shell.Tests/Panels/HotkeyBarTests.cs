using Microsoft.Xna.Framework;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>The action bar's non-drawing behavior: where its boxes are for a bar of any declared width,
/// and how a bound item NUMBER is resolved to a live inventory slot at the moment of use. That resolution
/// is the whole reason hotkeys store numbers rather than positions, so it is what these pin down.
///
/// <para>🔴 <b>How many slots there are is the GAME's.</b> Nothing here may assume four, and a world that
/// declared none gets no bar at all rather than a row of empty boxes.</para></summary>
[TestFixture]
public class HotkeyBarTests
{
    private const int Slots = 4;

    private static ClientState StateWith(params (int InvSlot, int ItemNum)[] bag)
    {
        var state = new ClientState { HotkeySlots = Slots };
        state.Me.Inv = new PlayerInvSlot[Constants.MaxInv + 1];
        for (int i = 0; i < state.Me.Inv.Length; i++) state.Me.Inv[i] = new PlayerInvSlot();
        state.Hotkeys = new PlayerHotkeysPacket.Slot[Slots + 1];
        foreach (var (slot, num) in bag) state.Me.Inv[slot].Num = num;
        return state;
    }

    private static PlayerHotkeysPacket.Slot Item(int num) =>
        new((byte)HotkeyKind.Record, CoreRecordFamilies.Items, (short)num, "Potion", "", 0);

    private static PlayerHotkeysPacket.Slot Verb(string id) =>
        new((byte)HotkeyKind.Verb, id, 0, "Cast", "spark", 0);

    private static readonly PlayerHotkeysPacket.Slot Nothing =
        new((byte)HotkeyKind.None, "", 0, "", "", 0);

    // ── Layout ───────────────────────────────────────────────────────────────

    [Test]
    public void Slots_AreAdjacentAndNonOverlapping()
    {
        for (int i = 1; i < Slots; i++)
        {
            var a = HotkeyBarPanel.SlotBounds(Slots, i);
            var b = HotkeyBarPanel.SlotBounds(Slots, i + 1);
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
        for (int i = 1; i <= Slots; i++)
        {
            Assert.That(HotkeyBarPanel.Bounds(Slots).Contains(HotkeyBarPanel.SlotBounds(Slots, i)), Is.True,
                        $"slot {i} escapes Bounds");
        }
    }

    /// <summary>🔴 A game that declared no bar gets NO bar: an empty rectangle nothing hovers, not a
    /// zero-width one sitting in the sidebar, and not a row of boxes a player cannot fill.</summary>
    [Test]
    public void NoDeclaredSlots_IsNoBarAtAll()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.Bounds(0), Is.EqualTo(Rectangle.Empty));
            Assert.That(HotkeyBarPanel.SlotAt(0, new Point(400, 540)), Is.EqualTo(0));
        });
    }

    /// <summary>The row stays centered under the sidebar at whatever width the game asked for, so a bar
    /// of one and a bar of twelve both sit where the player expects.</summary>
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(HotkeyBar.Max)]
    public void TheRowIsCenteredAtAnyDeclaredWidth(int slots)
    {
        var bar = HotkeyBarPanel.Bounds(slots);
        var first = HotkeyBarPanel.SlotBounds(slots, 1);
        var last = HotkeyBarPanel.SlotBounds(slots, slots);

        Assert.Multiple(() =>
        {
            Assert.That(first.Left, Is.EqualTo(bar.Left));
            Assert.That(last.Right, Is.EqualTo(bar.Right));
            // Centered on the same column whatever the count, within the rounding of an odd width.
            Assert.That(bar.Center.X, Is.EqualTo(HotkeyBarPanel.Bounds(4).Center.X).Within(1));
        });
    }

    [Test]
    public void SlotAt_RoundTripsEverySlotCenter_AndMissesElsewhere()
    {
        Assert.Multiple(() =>
        {
            for (int i = 1; i <= Slots; i++)
                Assert.That(HotkeyBarPanel.SlotAt(Slots, HotkeyBarPanel.SlotBounds(Slots, i).Center), Is.EqualTo(i));
            // Well clear of the bar in both axes.
            Assert.That(HotkeyBarPanel.SlotAt(Slots, new Point(0, 0)), Is.EqualTo(0));
            Assert.That(HotkeyBarPanel.SlotAt(Slots,
                new Point(HotkeyBarPanel.Bounds(Slots).Right + 40, HotkeyBarPanel.Bounds(Slots).Y)), Is.EqualTo(0));
        });
    }

    /// <summary>A point inside a WIDER bar is not a slot of a narrower one. The width is per world, and a
    /// click resolved against the wrong count would fire a slot the player cannot see.</summary>
    [Test]
    public void SlotAt_ReadsOnlyTheDeclaredSlots()
    {
        var eighth = HotkeyBarPanel.SlotBounds(HotkeyBar.Max, 12).Center;
        Assert.That(HotkeyBarPanel.SlotAt(2, eighth), Is.EqualTo(0));
    }

    // The bar draws above the link strip and must not sit on top of it — the two are stacked chrome, and
    // an overlap would put a hotkey box over the Mail/Options/Help row.
    [Test]
    public void Bar_SitsAboveTheLinkStrip()
        => Assert.That(HotkeyBarPanel.Bounds(Slots).Bottom, Is.LessThan(582), "the link strip starts at y=582");

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
    public void FindInvSlot_RejectsNonPositiveNumbers()
    {
        var state = StateWith((3, 42));
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.FindInvSlot(state, 0), Is.EqualTo(0));
            Assert.That(HotkeyBarPanel.FindInvSlot(state, -1), Is.EqualTo(0));
        });
    }

    // Availability is what grays a slot. An out-of-stock binding stays BOUND — it just can't fire — so the
    // player can see which potion they have run out of instead of the slot silently emptying itself.
    [Test]
    public void IsAvailable_TracksStockWithoutUnbinding()
    {
        var state = StateWith((3, 42));
        var hk = Item(42);
        Assert.That(HotkeyBarPanel.IsAvailable(state, hk), Is.True);

        state.Me.Inv[3].Num = 0;
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.IsAvailable(state, hk), Is.False);
            Assert.That(HotkeyBarPanel.IsBound(hk), Is.True, "running out must not clear the binding");
        });
    }

    /// <summary>🔴 <b>Only an item is grayed here.</b> Whether one of a game's own records can be used is
    /// a rule on the server, and a client guessing at it would gray a slot that works. So a game's slots
    /// are always drawn live, and the server refuses what it must.</summary>
    [Test]
    public void AGamesOwnSlots_AreNeverGrayedByThisClient()
    {
        var state = StateWith();
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.IsAvailable(state, Verb("msr.spell.cast")), Is.True);
            Assert.That(HotkeyBarPanel.IsAvailable(
                state, new PlayerHotkeysPacket.Slot((byte)HotkeyKind.Record, "Spell", 12, "Fireball", "spark", 0)),
                Is.True);
        });
    }

    [Test]
    public void AnUnboundSlotIsNotBound()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.IsBound(Nothing), Is.False);
            Assert.That(HotkeyBarPanel.IsBound(Item(42)), Is.True);
            Assert.That(HotkeyBarPanel.IsBound(Verb("msr.spell.cast")), Is.True);
            Assert.That(HotkeyBarPanel.IsHeldItem(Verb("msr.spell.cast")), Is.False);
            Assert.That(HotkeyBarPanel.IsHeldItem(Item(42)), Is.True);
        });
    }

    /// <summary>A frame drawn between a bar being resized and its packet arriving must not index past
    /// it — the server owns the width, and the two move separately.</summary>
    [Test]
    public void At_IsEmptyPastTheEndOfTheBar()
    {
        var state = StateWith();
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.IsBound(HotkeyBarPanel.At(state.Hotkeys, 99)), Is.False);
            Assert.That(HotkeyBarPanel.IsBound(HotkeyBarPanel.At(null, 1)), Is.False);
        });
    }

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

    /// <summary>A pad has four face buttons and a game may declare more slots. The fifth onward carry no
    /// letter rather than doubling up on one, which would fire two slots from one press.</summary>
    [Test]
    public void GamepadFace_StopsAtTheFourthSlot()
        => Assert.That(HotkeyBarPanel.GamepadFace(5), Is.Empty);

    /// <summary>The digits run 1 through 9 and then 0, which is the row a keyboard has. Past the tenth
    /// there is no key, and those slots are reached by clicking them.</summary>
    [Test]
    public void KeyLabel_RunsTheDigitRowAndThenStops()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyBarPanel.KeyLabel(1), Is.EqualTo("1"));
            Assert.That(HotkeyBarPanel.KeyLabel(9), Is.EqualTo("9"));
            Assert.That(HotkeyBarPanel.KeyLabel(HotkeyBar.Keyed), Is.EqualTo("0"));
            Assert.That(HotkeyBarPanel.KeyLabel(HotkeyBar.Keyed + 1), Is.Empty);
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
    public void EveryPadSlot_HasBothAFaceLetterAndAShape()
    {
        var letters = new HashSet<string>();
        var shapes = new HashSet<GamepadGlyphs.PsFace>();
        for (int i = 1; i <= 4; i++)
        {
            letters.Add(HotkeyBarPanel.GamepadFace(i));
            shapes.Add(HotkeyBarPanel.PlayStationFace(i));
        }
        Assert.Multiple(() =>
        {
            Assert.That(letters, Has.Count.EqualTo(4), "two slots share a face button");
            Assert.That(shapes, Has.Count.EqualTo(4), "two slots share a PlayStation shape");
        });
    }
}
