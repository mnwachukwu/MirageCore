using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Progression;

/// <summary>
/// Whether a character may learn a spell — the one answer the scroll and the editor's account browser both
/// take.
///
/// <para>Matt's rule, and the reason it is shared: the editor hands over THINGS, and what can be done with
/// them is the game's decision. The editor does not choose what a character wears either — removing a worn
/// piece merely takes it off. A spell meant to arrive early arrives as a scroll, exactly as gear does.</para>
/// </summary>
[TestFixture]
public class SpellLearnGateTests
{
    const int Fireball = 5;

    static (PlayerRecord P, SpellRecord Spell, ClassRecord Cls) Setup()
    {
        var p = new PlayerRecord { Class = 2, Level = 20, Int = 60 };
        var spell = new SpellRecord { Name = "Fireball", LevelReq = 10 };
        var cls = new ClassRecord { Name = "Wizard", Int = 8 };
        return (p, spell, cls);
    }

    static SpellSystem.LearnResult Check(PlayerRecord p, SpellRecord spell, ClassRecord cls) =>
        SpellSystem.CanLearn(p, Fireball, spell, cls);

    [Test]
    public void ACharacterWhoMeetsEverything_MayLearnIt()
    {
        var (p, spell, cls) = Setup();
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.Ok));
    }

    [Test]
    public void TheWrongClass_IsRefused()
    {
        var (p, spell, cls) = Setup();
        spell.AllowedClasses = [7, 8];
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.WrongClass));
    }

    [Test]
    public void AnUnrestrictedSpell_IsOpenToEveryClass()
    {
        var (p, spell, cls) = Setup();
        spell.AllowedClasses = null;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.Ok));
    }

    [Test]
    public void TooLowALevel_IsRefused()
    {
        var (p, spell, cls) = Setup();
        p.Level = 9;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.LevelTooLow));
    }

    [Test]
    public void ExactlyTheLevelRequired_IsEnough()
    {
        var (p, spell, cls) = Setup();
        p.Level = spell.LevelReq;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.Ok));
    }

    [Test]
    public void TooLittleInt_IsRefused()
    {
        var (p, spell, cls) = Setup();
        p.Int = 0;
        Assume.That(CombatFormulas.GetSpellIntRequirement(spell, cls.Int), Is.GreaterThan(0));
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.IntTooLow));
    }

    /// <summary>The INT requirement is discounted by the class's own affinity, so the same spell asks less of
    /// a class built for it. The gate has to read the class, not just the spell.</summary>
    [Test]
    public void TheClassAffinityDiscountsTheIntRequirement()
    {
        var (_, spell, _) = Setup();
        int plain = CombatFormulas.GetSpellIntRequirement(spell, 0);
        int gifted = CombatFormulas.GetSpellIntRequirement(spell, 20);
        Assert.That(gifted, Is.LessThanOrEqualTo(plain));
    }

    [Test]
    public void ASpellAlreadyKnown_IsRefused()
    {
        var (p, spell, cls) = Setup();
        p.Spell[3] = Fireball;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.AlreadyKnown));
    }

    [Test]
    public void AFullBook_IsRefused()
    {
        var (p, spell, cls) = Setup();
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++) p.Spell[i] = 90 + i;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.BookFull));
    }

    /// <summary>A full book that already holds the spell needs no slot, so naming the book full would name
    /// the wrong reason.</summary>
    [Test]
    public void AFullBookThatAlreadyHoldsIt_ReportsKnownRatherThanFull()
    {
        var (p, spell, cls) = Setup();
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++) p.Spell[i] = 90 + i;
        p.Spell[2] = Fireball;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.AlreadyKnown));
    }

    /// <summary>Class before level before INT: the refusal an operator sees should be the one that is
    /// hardest to do anything about.</summary>
    [Test]
    public void TheEarliestFailure_IsTheOneReported()
    {
        var (p, spell, cls) = Setup();
        spell.AllowedClasses = [7];
        p.Level = 1;
        p.Int = 0;
        Assert.That(Check(p, spell, cls), Is.EqualTo(SpellSystem.LearnResult.WrongClass));
    }
}
