using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>The spell-editor row: record round-trip, the dirty lifecycle + wire-load guard (ApplyPacket must
/// not read as an edit), the type-driven flags — only GiveItem shows the item/quantity/INT fields, and the
/// per-cast reagent cost is shown only for the SubHp "weapon" spell — and the save-time normalization.</summary>
[TestFixture]
public class SpellRowViewModelTests
{
    static SpellRecord Fireball() => new()
    {
        Name = "Fireball", AllowedClasses = [2, 6], Type = SpellType.SubHp, VitalAmount = 40,
    };

    [Test]
    public void Ctor_FromRecord_RoundTrips_AndIsClean()
    {
        var vm = new SpellRowViewModel(4, Fireball());
        var r = vm.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Fireball"));
            Assert.That(r.AllowedClasses, Is.EqualTo(new short[] { 2, 6 }));
            Assert.That(r.Type, Is.EqualTo(SpellType.SubHp));
            Assert.That(r.VitalAmount, Is.EqualTo((short)40));
            Assert.That(vm.IsDirty, Is.False);
            Assert.That(vm.IsLoaded, Is.True);
        });
    }

    [Test]
    public void EditingAField_MarksDirty_ClearResets()
    {
        var vm = new SpellRowViewModel(4, Fireball());
        vm.VitalAmount = 60;
        Assert.That(vm.IsDirty, Is.True);
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public void ApplyPacket_SeedsFields_MarksLoaded_ButNotDirty()
    {
        var vm = new SpellRowViewModel(1, new SpellRecord { Name = "" }, isLoaded: false);
        vm.ApplyPacket(new UpdateSpellPacket { Name = "Heal", Type = SpellType.AddHp, VitalAmount = 30 });
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a wire load is not an edit");
            Assert.That(vm.IsLoaded, Is.True);
            Assert.That(vm.ToRecord().Name, Is.EqualTo("Heal"));
            Assert.That(vm.ToRecord().Type, Is.EqualTo(SpellType.AddHp));
            Assert.That(vm.ToRecord().VitalAmount, Is.EqualTo((short)30));
        });
    }

    // The split is total, so the two flags are exact opposites — GiveItem has no magnitude, and nothing
    // else hands over an item.
    [Test]
    public void FieldVisibility_SplitsOnGiveItem()
    {
        Assert.Multiple(() =>
        {
            var give = Row(SpellType.GiveItem);
            Assert.That(give.IsGiveItem, Is.True);
            Assert.That(give.VitalAmountVisible, Is.False, "GiveItem carries no magnitude");

            foreach (var type in new[] { SpellType.SubHp, SpellType.AddHp, SpellType.SubMp, SpellType.AddSp })
            {
                var row = Row(type);
                Assert.That(row.IsGiveItem, Is.False, $"{type} hands over no item");
                Assert.That(row.VitalAmountVisible, Is.True, $"{type} has a magnitude");
            }
        });
    }

    // The reagent-per-cast line is the SubHp spell's real cost; other spell types pay MP only.
    [Test]
    public void ShowReagentCost_OnlyForSubHp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row(SpellType.SubHp).ShowReagentCost, Is.True);
            // A whole count and a percent: reagents are items so the count must be whole, while the odds
            // are stated outright. A low-tier cast usually takes nothing, which is what the percent says.
            var subHp = Row(SpellType.SubHp, vitalAmount: 40);
            Assert.That(subHp.ReagentCost, Is.Not.Empty, "a SubHp spell costs reagents");
            Assert.That(subHp.ReagentChance, Does.Contain("%"), "and states how often it takes them");
            Assert.That(Row(SpellType.AddHp).ShowReagentCost, Is.False);
            Assert.That(Row(SpellType.AddMp).ReagentCost, Is.Empty);
            Assert.That(Row(SpellType.AddMp).ReagentChance, Is.Empty);
        });
    }

    // Retyping away from GiveItem must drop its IntReq: RawSpellRequirement reads IntReq for GiveItem and
    // VitalAmount for everything else, so a leftover would silently re-gate the spell if it ever went back.
    [Test]
    public void ToRecord_ZeroesFieldsTheTypeDoesNotUse()
    {
        var give = new SpellRowViewModel(4, new SpellRecord
        {
            Name = "Conjure", Type = SpellType.GiveItem, ItemNum = 9, ItemQuantity = 3, IntReq = 20,
        });

        var asGiven = give.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(asGiven.ItemNum, Is.EqualTo((short)9));
            Assert.That(asGiven.ItemQuantity, Is.EqualTo((short)3));
            Assert.That(asGiven.IntReq, Is.EqualTo((short)20));
            Assert.That(asGiven.VitalAmount, Is.EqualTo((short)0));
        });

        give.Type = SpellType.AddHp;
        give.VitalAmount = 30;
        var retyped = give.ToRecord();

        Assert.Multiple(() =>
        {
            Assert.That(retyped.VitalAmount, Is.EqualTo((short)30));
            Assert.That(retyped.ItemNum, Is.EqualTo((short)0), "no longer hands over an item");
            Assert.That(retyped.ItemQuantity, Is.EqualTo((short)0));
            Assert.That(retyped.IntReq, Is.EqualTo((short)0), "gates off VitalAmount now, not IntReq");
        });
    }

    [Test]
    public void BuildSavePacket_IsNormalizedLikeToRecord()
    {
        var vm = new SpellRowViewModel(4, new SpellRecord
        {
            Name = "Conjure", Type = SpellType.GiveItem, ItemNum = 9, IntReq = 20,
        });
        vm.Type = SpellType.SubMp;
        vm.VitalAmount = 15;

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.SpellNum, Is.EqualTo(4));
            Assert.That(pkt.VitalAmount, Is.EqualTo((short)15));
            Assert.That(pkt.ItemNum, Is.EqualTo((short)0));
            Assert.That(pkt.IntReq, Is.EqualTo((short)0));
        });
    }

    static SpellRowViewModel Row(SpellType type, short vitalAmount = 10) =>
        new(1, new SpellRecord { Type = type, VitalAmount = vitalAmount });
}
