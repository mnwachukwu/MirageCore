using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>The action bar's storage rules. Most of this guards the load path: a character file predates
/// the bar, or was written when <see cref="Constants.MaxHotkeys"/> was a different width, and every read
/// site indexes 1..MaxHotkeys without a bounds check on the strength of <see cref="PlayerHotkey.Normalize"/>
/// having run first.</summary>
[TestFixture]
public class PlayerHotkeyTests
{
    [Test]
    public void NewBar_IsOneBasedAndEmpty()
    {
        var bar = PlayerHotkey.NewBar();
        Assert.Multiple(() =>
        {
            // Length MaxHotkeys + 1 with index 0 unused, matching Inv and Spell.
            Assert.That(bar, Has.Length.EqualTo(Constants.MaxHotkeys + 1));
            for (int i = 1; i <= Constants.MaxHotkeys; i++)
                Assert.That(bar[i].IsBound, Is.False, $"slot {i} should start unbound");
        });
    }

    // A save written before the bar existed deserializes the property as null. Without this the very first
    // login on an existing character would throw on the join-time send.
    [Test]
    public void Normalize_Null_GivesAFullEmptyBar()
    {
        var bar = PlayerHotkey.Normalize(null);
        Assert.That(bar, Has.Length.EqualTo(Constants.MaxHotkeys + 1));
        Assert.That(bar.Any(h => h.IsBound), Is.False);
    }

    [Test]
    public void Normalize_ShorterBar_KeepsWhatItHadAndPadsTheRest()
    {
        // As if MaxHotkeys had been 2 when this character was last saved.
        var saved = new PlayerHotkey[3];
        saved[1] = new PlayerHotkey(HotkeyKind.Item, 7);
        saved[2] = new PlayerHotkey(HotkeyKind.Spell, 9);

        var bar = PlayerHotkey.Normalize(saved);

        Assert.Multiple(() =>
        {
            Assert.That(bar, Has.Length.EqualTo(Constants.MaxHotkeys + 1));
            Assert.That(bar[1], Is.EqualTo(new PlayerHotkey(HotkeyKind.Item, 7)));
            Assert.That(bar[2], Is.EqualTo(new PlayerHotkey(HotkeyKind.Spell, 9)));
            for (int i = 3; i <= Constants.MaxHotkeys; i++)
                Assert.That(bar[i].IsBound, Is.False);
        });
    }

    [Test]
    public void Normalize_LongerBar_TruncatesRatherThanThrowing()
    {
        var saved = new PlayerHotkey[Constants.MaxHotkeys + 5];
        for (int i = 1; i < saved.Length; i++) saved[i] = new PlayerHotkey(HotkeyKind.Item, (short)i);

        var bar = PlayerHotkey.Normalize(saved);

        Assert.That(bar, Has.Length.EqualTo(Constants.MaxHotkeys + 1));
        Assert.That(bar[Constants.MaxHotkeys].Num, Is.EqualTo(Constants.MaxHotkeys));
    }

    // A Kind with no Num is a half-written record, not a binding — it must not survive a load and render
    // as a bound-but-broken icon.
    [Test]
    public void Normalize_DropsAKindWithNoNumber()
    {
        var saved = PlayerHotkey.NewBar();
        saved[1] = new PlayerHotkey(HotkeyKind.Item, 0);
        saved[2] = new PlayerHotkey(HotkeyKind.Spell, 0);

        var bar = PlayerHotkey.Normalize(saved);

        Assert.That(bar[1].IsBound, Is.False);
        Assert.That(bar[2].IsBound, Is.False);
    }

    [Test]
    public void IsBound_NeedsBothAKindAndANumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayerHotkey.Empty.IsBound, Is.False);
            Assert.That(new PlayerHotkey(HotkeyKind.None, 5).IsBound, Is.False, "a number alone is not a binding");
            Assert.That(new PlayerHotkey(HotkeyKind.Item, 0).IsBound, Is.False, "a kind alone is not a binding");
            Assert.That(new PlayerHotkey(HotkeyKind.Item, 5).IsBound, Is.True);
            Assert.That(new PlayerHotkey(HotkeyKind.Spell, 5).IsBound, Is.True);
        });
    }

    // The bar stores numbers, never bag/book positions — the whole reason it survives a reordering
    // inventory. Nothing enforces that at the type level, so this pins the intent: index 0 stays unused so
    // a slot number and an array index are the same thing at every call site.
    [Test]
    public void Normalize_LeavesIndexZeroUnused()
    {
        var saved = PlayerHotkey.NewBar();
        saved[0] = new PlayerHotkey(HotkeyKind.Item, 3);
        Assert.That(PlayerHotkey.Normalize(saved)[0].IsBound, Is.False);
    }
}
