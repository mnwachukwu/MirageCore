using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The account editor's level/EXP/points coupling. Setting a level here has to produce the
/// character the game itself would have produced at that level — EXP on the level's floor and a point pool
/// moved by <see cref="Constants.PointsPerLevel"/> per level — and has to refuse the one thing a delevel
/// cannot honestly do, which is take back points that were already spent.</summary>
[TestFixture]
public class AccountCharRowTests
{
    // A level-10 Warrior who spent every point granted so far: 20 base + 9 levels x 3 = 47 held.
    private static EditorCharRow Char10() => new()
    {
        Slot = 1,
        Name = "Tavin",
        Class = 1,
        Level = 10,
        Exp = ExpFormulas.ExpFloorForLevel(10) + 250,
        Map = 1,
        Str = 20,
        Def = 15,
        Spd = 6,
        Int = 6,
        Points = 0,
    };

    // The same level-10 budget of 47, with part of it still sitting in the pool.
    private static EditorCharRow Char10With(int unspent) => Char10() with { Str = 20 - unspent, Points = unspent };

    // Neither picker is opened here, so empty sources are the honest stand-in for them.
    private static AccountCharRowViewModel Row(EditorCharRow? row = null) => new(row ?? Char10(), () => [], () => [], () => []);

    [Test]
    public void Loading_LeavesTheRecordAlone()
    {
        var row = Row();
        Assert.Multiple(() =>
        {
            // Mid-level progress survives the load; only an EDIT resets it.
            Assert.That(row.Exp, Is.EqualTo(ExpFormulas.ExpFloorForLevel(10) + 250));
            Assert.That(row.Points, Is.Zero);
            Assert.That(row.PointsHeld, Is.EqualTo(47));
            Assert.That(row.PointBudget, Is.EqualTo(47));
            Assert.That(row.IsOverBudget, Is.False);
        });
    }

    [Test]
    public void LevelUp_SetsExpToTheFloor_AndGrantsPoints()
    {
        var row = Row();
        row.Level = 13;
        Assert.Multiple(() =>
        {
            Assert.That(row.Exp, Is.EqualTo(ExpFormulas.ExpFloorForLevel(13)));
            Assert.That(row.Points, Is.EqualTo(9), "three levels at PointsPerLevel");
            Assert.That(row.IsOverBudget, Is.False);
        });
    }

    [Test]
    public void Delevel_TakesTheGrantBackOutOfUnspentPoints()
    {
        var row = Row(Char10With(12));
        row.Level = 8;
        Assert.Multiple(() =>
        {
            Assert.That(row.Exp, Is.EqualTo(ExpFormulas.ExpFloorForLevel(8)));
            Assert.That(row.Points, Is.EqualTo(6), "two levels reclaimed from the unspent pool");
            Assert.That(row.Str, Is.EqualTo(8), "no stat is touched while the pool can pay");
            Assert.That(row.IsOverBudget, Is.False);
        });
    }

    [Test]
    public void Delevel_PastTheUnspentPool_GoesOverBudgetRatherThanDrainingStats()
    {
        var row = Row();          // 47 points, all spent
        row.Level = 5;
        Assert.Multiple(() =>
        {
            // The death penalty would drain random stats here. The editor will not guess which.
            Assert.That(row.Points, Is.Zero);
            Assert.That(row.Str, Is.EqualTo(20), "no stat is touched");
            Assert.That(row.PointsHeld, Is.EqualTo(47));
            Assert.That(row.PointBudget, Is.EqualTo(32));
            Assert.That(row.IsOverBudget, Is.True);
            Assert.That(row.IsWithinBudget, Is.False);
        });
    }

    /// <summary>A NumericUpDown raises a value per keystroke, so "5" typed over "12" passes through 1 and
    /// 15 on the way. Adjusting the pool by a delta would clamp at zero somewhere in that sequence and
    /// never recover; the result has to depend only on where the level LANDS.</summary>
    [Test]
    public void LevelChange_IsIdempotent_WhateverThePath()
    {
        var direct = Row(Char10With(5));
        direct.Level = 20;

        var typed = Row(Char10With(5));
        typed.Level = 1;
        typed.Level = 2;
        typed.Level = 20;

        Assert.Multiple(() =>
        {
            Assert.That(typed.Points, Is.EqualTo(direct.Points));
            Assert.That(typed.Exp, Is.EqualTo(direct.Exp));
            Assert.That(direct.Points, Is.EqualTo(5 + Constants.PointsPerLevel * 10));
        });
    }

    [Test]
    public void HandEditedPoints_BecomeTheBaselineForTheNextLevelChange()
    {
        var row = Row(Char10With(6));
        row.Points = 3;
        row.Level = 11;
        Assert.That(row.Points, Is.EqualTo(6), "one level on top of what the operator typed, not on top of 6");
    }

    [Test]
    public void RaisingAStatPastTheBudget_FlagsTheRow()
    {
        var row = Row();
        Assert.That(row.IsOverBudget, Is.False);
        row.Str = 21;
        Assert.Multiple(() =>
        {
            Assert.That(row.PointsHeld, Is.EqualTo(48));
            Assert.That(row.IsOverBudget, Is.True);
        });
    }

    [Test]
    public void ToRow_CarriesTheCoupledValues()
    {
        var row = Row();
        row.Level = 12;
        var sent = row.ToRow();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Level, Is.EqualTo(12));
            Assert.That(sent.Exp, Is.EqualTo(ExpFormulas.ExpFloorForLevel(12)));
            Assert.That(sent.Points, Is.EqualTo(6));
            Assert.That(sent.Slot, Is.EqualTo(1));
            Assert.That(sent.Name, Is.EqualTo("Tavin"), "a rename does not go through this form");
        });
    }

    // ── The editor-level consequence: an over-budget row stops the save ────────

    private static AccountEditorViewModel Editor(params EditorCharRow[] chars)
    {
        var vm = new AccountEditorViewModel(new EditorDataService(), new EditorConnection());
        vm.SelectedAccount = new EditorAccountRow { Login = "tav" };
        vm.Apply(new EditorAccountPacket { Login = "tav", Chars = [.. chars] });
        return vm;
    }

    [Test]
    public void CanSave_IsTrue_ForALoadedAccountWithinBudget()
    {
        var vm = Editor(Char10());
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasOverBudgetChar, Is.False);
            Assert.That(vm.CanSave, Is.True);
        });
    }

    [Test]
    public void CanSave_GoesFalse_TheMomentAnyCharacterGoesOverBudget()
    {
        var vm = Editor(Char10(), Char10() with { Slot = 2, Name = "Bree" });
        vm.Chars[1].Level = 4;

        Assert.Multiple(() =>
        {
            Assert.That(vm.Chars[0].IsOverBudget, Is.False, "the untouched character is fine");
            Assert.That(vm.Chars[1].IsOverBudget, Is.True);
            Assert.That(vm.HasOverBudgetChar, Is.True);
            Assert.That(vm.CanSave, Is.False, "one bad row blocks the whole account save");
        });
    }

    [Test]
    public void CanSave_ComesBack_WhenTheRowIsFixed()
    {
        var vm = Editor(Char10());
        vm.Chars[0].Level = 4;
        Assert.That(vm.CanSave, Is.False);

        vm.Chars[0].Level = 10;
        Assert.That(vm.CanSave, Is.True);
    }

    /// <summary>Every server reply rebuilds the rows, and only the rows on screen speak for the account:
    /// a detached row is inert however it is edited.</summary>
    [Test]
    public void ReloadingAnAccount_DropsTheOldRows()
    {
        var vm = Editor(Char10());
        var stale = vm.Chars[0];
        vm.Apply(new EditorAccountPacket { Login = "tav", Chars = [Char10()] });

        stale.Level = 4;
        Assert.Multiple(() =>
        {
            Assert.That(stale.IsOverBudget, Is.True);
            Assert.That(vm.HasOverBudgetChar, Is.False, "the detached row no longer speaks for the account");
            Assert.That(vm.CanSave, Is.True);
        });
    }
}
