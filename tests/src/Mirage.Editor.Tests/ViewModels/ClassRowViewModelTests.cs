using Mirage.Editor.ViewModels;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The class-editor row: record round-trip, the dirty lifecycle + wire-load guard, and the fresh-Lv.1
/// stat previews wiring — each preview must key off the RIGHT stat (HP off DEF, MP off INT, SP off SPD, phys
/// damage off STR, magic off INT). A mis-wired preview (e.g. HP reading STR) is the failure this guards.</summary>
[TestFixture]
public class ClassRowViewModelTests
{
    static ClassRecord Warrior() => new()
    {
        Name = "Warrior", SpriteMale = 5, SpriteFemale = 15, Str = 20, Def = 18, Spd = 10, Int = 4,
    };

    static ClassRowViewModel Cls(int str, int def, int spd, int @int) =>
        new(1, new ClassRecord { Str = str, Def = def, Spd = spd, Int = @int });

    [Test]
    public void Ctor_FromRecord_RoundTrips_AndIsClean()
    {
        var vm = new ClassRowViewModel(2, Warrior());
        var r = vm.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Warrior"));
            Assert.That(r.SpriteMale, Is.EqualTo(5));
            Assert.That(r.SpriteFemale, Is.EqualTo(15));
            Assert.That(r.Str, Is.EqualTo(20));
            Assert.That(r.Def, Is.EqualTo(18));
            Assert.That(r.Spd, Is.EqualTo(10));
            Assert.That(r.Int, Is.EqualTo(4));
            Assert.That(vm.IsDirty, Is.False);
        });
    }

    [Test]
    public void EditingAField_MarksDirty_ClearResets()
    {
        var vm = new ClassRowViewModel(2, Warrior());
        vm.Str = 25;
        Assert.That(vm.IsDirty, Is.True);
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public void ApplyPacket_SeedsFields_ButNotDirty()
    {
        var vm = new ClassRowViewModel(1, new ClassRecord(), isLoaded: false);
        vm.ApplyPacket(new UpdateClassPacket { Name = "Mage", Str = 4, Def = 6, Spd = 8, Int = 20 });
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a wire load is not an edit");
            Assert.That(vm.IsLoaded, Is.True);
            Assert.That(vm.ToRecord().Int, Is.EqualTo(20));
        });
    }

    // Each Lv.1 pool preview rises with its OWN stat...
    [Test]
    public void LevelOnePools_KeyOffTheirOwnStat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cls(10, 40, 10, 10).LevelOneMaxHp, Is.GreaterThan(Cls(10, 20, 10, 10).LevelOneMaxHp), "HP off DEF");
            Assert.That(Cls(10, 10, 10, 40).LevelOneMaxMp, Is.GreaterThan(Cls(10, 10, 10, 20).LevelOneMaxMp), "MP off INT");
            Assert.That(Cls(10, 10, 40, 10).LevelOneMaxSp, Is.GreaterThan(Cls(10, 10, 20, 10).LevelOneMaxSp), "SP off SPD");
            Assert.That(Cls(40, 10, 10, 10).LevelOnePhysDamage, Is.GreaterThan(Cls(10, 10, 10, 10).LevelOnePhysDamage), "phys off STR");
            Assert.That(Cls(10, 10, 10, 40).LevelOneMagicDamage, Is.GreaterThan(Cls(10, 10, 10, 10).LevelOneMagicDamage), "magic off INT");
        });
    }

    // ...and NOT off an unrelated stat: the HP preview must ignore STR (the classic mis-wiring).
    [Test]
    public void LevelOneMaxHp_IgnoresStr()
        => Assert.That(Cls(50, 20, 10, 10).LevelOneMaxHp, Is.EqualTo(Cls(5, 20, 10, 10).LevelOneMaxHp),
            "HP is a function of DEF only; changing STR must not move it");
}
