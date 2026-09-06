using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Combat;

/// <summary>Pure spellbook slot helpers on <see cref="SpellSystem"/>: 1-based first-empty-slot search
/// (0 = book full) and membership lookup. These gate learning a scroll (ItemSystem.UseItem) and casting.</summary>
[TestFixture]
public class SpellHelperTests
{
    [Test]
    public void FindOpenSpellSlot_EmptyBook_ReturnsOne()
        => Assert.That(SpellSystem.FindOpenSpellSlot(new PlayerRecord()), Is.EqualTo(1));

    [Test]
    public void FindOpenSpellSlot_ReturnsFirstGap()
    {
        var p = new PlayerRecord();
        p.Spell[1] = 7;
        p.Spell[2] = 9;  // 1 and 2 known
        Assert.That(SpellSystem.FindOpenSpellSlot(p), Is.EqualTo(3));
    }

    [Test]
    public void FindOpenSpellSlot_FullBook_ReturnsZero()
    {
        var p = new PlayerRecord();
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++) p.Spell[i] = i;
        Assert.That(SpellSystem.FindOpenSpellSlot(p), Is.EqualTo(0));
    }

    [Test]
    public void HasSpell_PresentAndAbsent()
    {
        var p = new PlayerRecord();
        p.Spell[5] = 42;
        Assert.Multiple(() =>
        {
            Assert.That(SpellSystem.HasSpell(p, 42), Is.True);
            Assert.That(SpellSystem.HasSpell(p, 43), Is.False);
            Assert.That(SpellSystem.HasSpell(p, 0), Is.True, "slot value 0 = empty; HasSpell(0) matches an empty slot");
        });
    }
}
