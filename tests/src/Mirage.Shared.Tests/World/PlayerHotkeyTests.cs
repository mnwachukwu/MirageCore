using Mirage.Shared.Extensibility;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>The action bar's storage rules. Most of this guards the load path: a character file predates
/// the bar, or was written under a world that sized it differently, and every read site indexes the
/// declared slots without a bounds check on the strength of <see cref="PlayerHotkey.Normalize"/> having
/// run first.
///
/// <para>🔴 <b>How wide the bar is belongs to the GAME.</b> A character can move between worlds that
/// declared different widths, or one that declared none, so no width is a constant here.</para></summary>
[TestFixture]
public class PlayerHotkeyTests
{
    private const int Slots = 4;

    private static PlayerHotkey Item(int num) =>
        new(HotkeyKind.Record, CoreRecordFamilies.Items, (short)num);

    [Test]
    public void NewBar_IsOneBasedAndEmpty()
    {
        var bar = PlayerHotkey.NewBar(Slots);
        Assert.Multiple(() =>
        {
            // Length slots + 1 with index 0 unused, matching Inv and Spell.
            Assert.That(bar, Has.Length.EqualTo(Slots + 1));
            for (int i = 1; i <= Slots; i++)
                Assert.That(bar[i].IsBound, Is.False, $"slot {i} should start unbound");
        });
    }

    /// <summary>A world that declared no bar. Index 0 is still there, so the load path and every read
    /// site behave the same rather than branching on "is there a bar".</summary>
    [Test]
    public void NewBar_OfNoSlots_IsStillIndexable()
        => Assert.That(PlayerHotkey.NewBar(0), Has.Length.EqualTo(1));

    // A save written before the bar existed deserializes the property as null. Without this the very first
    // login on an existing character would throw on the join-time send.
    [Test]
    public void Normalize_Null_GivesAFullEmptyBar()
    {
        var bar = PlayerHotkey.Normalize(null, Slots);
        Assert.That(bar, Has.Length.EqualTo(Slots + 1));
        Assert.That(bar.Any(h => h.IsBound), Is.False);
    }

    [Test]
    public void Normalize_ShorterBar_KeepsWhatItHadAndPadsTheRest()
    {
        // As if the game had declared two slots when this character was last saved.
        var saved = new PlayerHotkey[3];
        saved[1] = Item(7);
        saved[2] = Item(9);

        var bar = PlayerHotkey.Normalize(saved, Slots);

        Assert.Multiple(() =>
        {
            Assert.That(bar, Has.Length.EqualTo(Slots + 1));
            Assert.That(bar[1], Is.EqualTo(Item(7)));
            Assert.That(bar[2], Is.EqualTo(Item(9)));
            for (int i = 3; i <= Slots; i++)
                Assert.That(bar[i].IsBound, Is.False);
        });
    }

    [Test]
    public void Normalize_LongerBar_TruncatesRatherThanThrowing()
    {
        var saved = new PlayerHotkey[Slots + 5];
        for (int i = 1; i < saved.Length; i++) saved[i] = Item(i);

        var bar = PlayerHotkey.Normalize(saved, Slots);

        Assert.That(bar, Has.Length.EqualTo(Slots + 1));
        Assert.That(bar[Slots].Num, Is.EqualTo(Slots));
    }

    /// <summary>A character carrying a bar into a world with no bar. Everything is dropped rather than
    /// kept invisibly, because there is nowhere for it to be and nothing to fire it.</summary>
    [Test]
    public void Normalize_IntoAWorldWithNoBar_KeepsNothing()
    {
        var saved = PlayerHotkey.NewBar(Slots);
        saved[1] = Item(7);

        Assert.That(PlayerHotkey.Normalize(saved, 0), Has.Length.EqualTo(1));
    }

    // A Kind with no declaration named is a half-written record, not a binding — it must not survive a
    // load and render as a bound-but-broken icon.
    [Test]
    public void Normalize_DropsAKindNamingNothing()
    {
        var saved = PlayerHotkey.NewBar(Slots);
        saved[1] = new PlayerHotkey(HotkeyKind.Record, "", 5);
        saved[2] = new PlayerHotkey(HotkeyKind.Verb, "", 0);

        var bar = PlayerHotkey.Normalize(saved, Slots);

        Assert.That(bar[1].IsBound, Is.False);
        Assert.That(bar[2].IsBound, Is.False);
    }

    [Test]
    public void IsBound_NeedsBothAKindAndADeclaration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayerHotkey.Empty.IsBound, Is.False);
            Assert.That(new PlayerHotkey(HotkeyKind.None, CoreRecordFamilies.Items, 5).IsBound, Is.False,
                        "a number alone is not a binding");
            Assert.That(new PlayerHotkey(HotkeyKind.Record, "", 5).IsBound, Is.False,
                        "a kind alone is not a binding");
            Assert.That(Item(5).IsBound, Is.True);
            // A verb needs no number: it is the verb itself that was bound, and a subject is optional.
            Assert.That(new PlayerHotkey(HotkeyKind.Verb, "msr.spell.cast", 0).IsBound, Is.True);
        });
    }

    // The bar stores numbers, never bag/book positions — the whole reason it survives a reordering
    // inventory. Nothing enforces that at the type level, so this pins the intent: index 0 stays unused so
    // a slot number and an array index are the same thing at every call site.
    [Test]
    public void Normalize_LeavesIndexZeroUnused()
    {
        var saved = PlayerHotkey.NewBar(Slots);
        saved[0] = Item(3);
        Assert.That(PlayerHotkey.Normalize(saved, Slots)[0].IsBound, Is.False);
    }
}
