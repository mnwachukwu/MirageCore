using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Typed changes in the account form live until Save sends them, and nothing else in the section throws
/// them away.
///
/// <para>Two things read an account back while the form is open, and neither is a save. A targeted
/// operation — give an item, teach a spell, set a quest, rename — lands immediately and has to be re-read,
/// because the bag on screen must be the bag that exists. And the browser list refreshes on every keystroke
/// in the search box, which clears the list and takes the ListBox's selection with it.</para>
///
/// <para>🔴 Both used to reach the whole form. The split that makes them safe is ownership: the SERVER owns
/// the vault, the online flag, the guild line and each character's name, bag, book and log, and re-reading
/// those is always right. The OPERATOR owns access and every character's level, EXP, position and stats —
/// the fields <c>ToRow</c> sends — and those are theirs until Save.</para>
/// </summary>
[TestFixture]
public class AccountEditsSurviveTests
{
    private const string Login = "matt";

    private static EditorCharRow Tavin() => new()
    {
        Slot = 1, Name = "Tavin", Class = 1, Level = 10, Exp = 500, Map = 3, X = 4, Y = 5,
        Str = 20, Def = 15, Spd = 6, Int = 6, Points = 0,
        Inv = [new EditorInvSlot { Slot = 1, Num = 7, Quantity = 1 }],
        Spells = [new EditorSpellSlot { Slot = 1, Num = 2 }],
        Quests = [new EditorQuestRow { QuestNum = 4, Status = QuestStatus.InProgress }],
    };

    private static EditorAccountPacket Account(params EditorCharRow[] chars) => new()
    {
        Login = Login,
        Access = AdminLevel.Creator,
        Guild = 2,
        Chars = [.. chars.Length > 0 ? chars : [Tavin()]],
        Bank = [new EditorInvSlot { Slot = 1, Num = 9, Quantity = 3 }],
    };

    private static AccountEditorViewModel Open()
    {
        var vm = new AccountEditorViewModel(new EditorDataService(), new EditorConnection());
        vm.Apply(Account());
        return vm;
    }

    /// <summary>Everything an operator can type and has not sent yet: two levels granted and spent on
    /// strength, a move, and a demotion. Kept inside the stat budget, since a form over it refuses to save
    /// for its own reasons and would say nothing about what survives a re-read.</summary>
    private static void TypeSomeChanges(AccountEditorViewModel vm)
    {
        vm.Access = AdminLevel.Mapper;
        var c = vm.Chars[0];
        c.Level = 12;      // grants PointsPerLevel x 2 into the pool
        c.Map = 99;
        c.Str = 26;        // ...spent here
        c.Points = 0;
    }

    private static void AssertStillTyped(AccountEditorViewModel vm)
    {
        Assert.Multiple(() =>
        {
            Assert.That(vm.Access, Is.EqualTo(AdminLevel.Mapper), "access");
            Assert.That(vm.Chars[0].Level, Is.EqualTo(12), "level");
            Assert.That(vm.Chars[0].Map, Is.EqualTo(99), "map");
            Assert.That(vm.Chars[0].Str, Is.EqualTo(26), "strength");
            Assert.That(vm.Chars[0].Points, Is.Zero, "unspent points");
        });
    }

    // ── A targeted operation's re-read ───────────────────────────────────────

    [Test]
    public void ATargetedOpsReread_LeavesTypedChangesAlone()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.AdoptServerOwned(Account());

        AssertStillTyped(vm);
    }

    [Test]
    public void ATargetedOpsReread_TakesTheServersBagBookAndLog()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        var afterGiving = Tavin() with
        {
            Inv = [new EditorInvSlot { Slot = 1, Num = 7, Quantity = 1 },
                   new EditorInvSlot { Slot = 2, Num = 42, Quantity = 1 }],
            Spells = [],
            Quests = [],
        };
        vm.AdoptServerOwned(Account(afterGiving));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Chars[0].Inv, Has.Count.EqualTo(2), "the item just handed over is not on screen");
            Assert.That(vm.Chars[0].Spells, Is.Empty);
            Assert.That(vm.Chars[0].Quests, Is.Empty);
        });
        AssertStillTyped(vm);
    }

    [Test]
    public void ATargetedOpsReread_TakesTheServersVault()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        var record = Account();
        record.Bank.Add(new EditorInvSlot { Slot = 2, Num = 11, Quantity = 1 });
        vm.AdoptServerOwned(record);

        Assert.That(vm.Bank, Has.Count.EqualTo(2));
        AssertStillTyped(vm);
    }

    /// <summary>A rename is a targeted op too, so the accepted name has to arrive — and the rename box has
    /// to follow it, or the button stays lit offering the rename that already happened.</summary>
    [Test]
    public void ARenamesReread_TakesTheNewNameAndSettlesTheBox()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.AdoptServerOwned(Account(Tavin() with { Name = "Tavina" }));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Chars[0].Name, Is.EqualTo("Tavina"));
            Assert.That(vm.Chars[0].RenameTo, Is.EqualTo("Tavina"));
            Assert.That(vm.Chars[0].CanRename, Is.False, "the box still offers the rename that just landed");
        });
        AssertStillTyped(vm);
    }

    /// <summary>A character appearing or disappearing means the form is describing an account that moved
    /// under it. Nothing on screen can be trusted, so the whole record is taken.</summary>
    [Test]
    public void WhenTheCharacterSetChanges_TheWholeRecordIsTaken()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.AdoptServerOwned(Account(Tavin(), Tavin() with { Slot = 2, Name = "Bryn" }));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Chars, Has.Count.EqualTo(2));
            Assert.That(vm.Access, Is.EqualTo(AdminLevel.Creator), "a changed roster is a full re-read");
            Assert.That(vm.Chars[0].Level, Is.EqualTo(10));
        });
    }

    // ── The browser list refreshing underneath ───────────────────────────────

    [Test]
    public void TheListSelectionGoingAway_LeavesTheFormOpen()
    {
        var vm = Open();
        vm.SelectedAccount = new EditorAccountRow { Login = Login };
        TypeSomeChanges(vm);

        vm.SelectedAccount = null;      // what clearing Accounts does to the bound selection

        Assert.Multiple(() =>
        {
            Assert.That(vm.Login, Is.EqualTo(Login));
            Assert.That(vm.Chars, Is.Not.Empty);
            Assert.That(vm.HasSelection, Is.True, "the form is open, so it is not 'no account selected'");
            Assert.That(vm.CanSave, Is.True, "Save went gray with unsaved changes on screen");
        });
        AssertStillTyped(vm);
    }

    /// <summary>The same account arriving back on a refreshed page is the one already open, so it is not
    /// re-read — doing so would discard the very changes the refresh must not touch.</summary>
    [Test]
    public void ReselectingTheOpenAccount_DoesNotRereadIt()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.SelectedAccount = new EditorAccountRow { Login = Login };

        AssertStillTyped(vm);
    }

    [Test]
    public void GoingOffline_ClosesTheFormOutright()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.LoadOffline();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Login, Is.Empty);
            Assert.That(vm.Chars, Is.Empty);
            Assert.That(vm.Bank, Is.Empty);
            Assert.That(vm.HasSelection, Is.False);
        });
    }

    /// <summary>The two paths that re-read while the form is open both have to ask for the merge. Read from
    /// source: the request itself needs a live connection, so nothing here can drive one.</summary>
    [Test]
    public void BothRereadingPaths_AskToKeepEdits()
    {
        string dir = typeof(AccountEditsSurviveTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        string source = File.ReadAllText(Path.Combine(dir, "ViewModels", "AccountEditorViewModel.cs"));
        string code = string.Join("\n", source.Split('\n')
            .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));

        var reloads = Regex.Matches(code, @"LoadAccountAsync\(Login[^)]*\)").Select(m => m.Value).ToList();

        Assert.That(reloads, Has.Count.EqualTo(3),
            "the re-reading call sites moved: " + string.Join(" | ", reloads));
        Assert.That(reloads.Count(r => r.Contains("keepEdits: true")), Is.EqualTo(2),
            "a targeted operation and a rename both re-read while the form may hold unsaved changes, so both "
            + "ask to keep them; only the explicit Reload takes the whole record: " + string.Join(" | ", reloads));
    }

    /// <summary>Save's reply is the server's own re-read of what it just accepted, so it replaces the form
    /// outright — that is what makes a clamped level visible instead of leaving the screen asserting a
    /// number that did not land.</summary>
    [Test]
    public void AFullApply_StillReplacesEverything()
    {
        var vm = Open();
        TypeSomeChanges(vm);

        vm.Apply(Account(Tavin() with { Level = 11 }));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Access, Is.EqualTo(AdminLevel.Creator));
            Assert.That(vm.Chars[0].Level, Is.EqualTo(11));
        });
    }
}
