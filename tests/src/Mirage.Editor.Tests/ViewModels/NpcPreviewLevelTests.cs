using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// Locks the NPC editor's "experience at level" preview spinner (<see cref="NpcRowViewModel.PreviewLevel"/>):
///   1. On EVERY load path (offline ctor, offline reload, ONLINE packet) the spinner must open on the mob's own
///      player-equivalent level — not the placeholder level 1.  The online packet path (ApplyPacket) is the one
///      that kept getting missed, leaving the spinner stranded at 1 (the reported bug).
///   2. While parked on the mob's own level the spinner auto-follows stat edits that change that level; once
///      scrubbed to a custom level it detaches and stays put.
/// Levels come from <see cref="StatFormulas.NpcLevel"/> = Max(1, (statSum - 20)/3 + 1), so +3 stat points = +1
/// level; concrete numbers below are annotated with that arithmetic.
/// </summary>
[TestFixture]
public class NpcPreviewLevelTests
{
    private static NpcRecord Npc(int str = 0, int def = 0, int @int = 0, int spd = 0, string name = "Mob") =>
        new() { Name = name, Str = str, Def = def, Int = @int, Spd = spd };

    // Mirrors the wire packet the editor receives for an online NPC load.
    private static UpdateNpcPacket Packet(int str = 0, int def = 0, int @int = 0, int spd = 0, string name = "Mob") =>
        new() { NpcNum = 1, Name = name, Str = str, Def = def, Int = @int, Spd = spd, Light = LightSpec.Torch };

    // ── Every load path opens the spinner on the mob's own level ───────────────

    [Test]
    public void Ctor_Offline_OpensSpinnerOnOwnLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // sum 32 → level 5
        Assert.That(vm.PreviewLevel, Is.EqualTo(StatFormulas.NpcLevel(32, 0, 0, 0)));
        Assert.That(vm.PreviewLevel, Is.EqualTo(5), "(32-20)/3 + 1 = 5");
    }

    // The reported bug: an online row is built name-only (all-zero stats → placeholder level 1), then filled in
    // by the packet.  Before the fix, ApplyPacket never touched PreviewLevel, so the spinner stayed at 1.
    [Test]
    public void ApplyPacket_Online_OpensSpinnerOnOwnLevel_NotOne()
    {
        var vm = new NpcRowViewModel(1, Npc(name: "Goblin"), isLoaded: false);
        Assert.That(vm.PreviewLevel, Is.EqualTo(1),
            "precondition: an unloaded online row sits at the placeholder level 1");

        vm.ApplyPacket(Packet(str: 32));   // a level-5 mob arrives from the server

        Assert.That(vm.PreviewLevel, Is.EqualTo(StatFormulas.NpcLevel(32, 0, 0, 0)));
        Assert.That(vm.PreviewLevel, Is.EqualTo(5),
            "loading an NPC online must open the spinner on the mob's level, not the placeholder 1");
    }

    [Test]
    public void LoadFromRecord_Offline_ReSeedsSpinnerToOwnLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // level 5
        vm.LoadFromRecord(Npc(str: 35));                 // reload as a level-6 mob (e.g. a Discard)
        Assert.That(vm.PreviewLevel, Is.EqualTo(6), "(35-20)/3 + 1 = 6");
    }

    // ── Auto-follow while parked on the mob's own level ────────────────────────

    [TestCase("Str")]
    [TestCase("Def")]
    [TestCase("Int")]
    [TestCase("Spd")]
    public void Spinner_FollowsMob_WhenAnyStatLiftsLevel(string stat)
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // level 5; spinner parked on 5
        Assume.That(vm.PreviewLevel, Is.EqualTo(5));

        // +3 points = exactly +1 level, whichever stat carries it (proves all four stat handlers follow).
        switch (stat)
        {
            case "Str":
                vm.Str += 3;
                break;
            case "Def":
                vm.Def += 3;
                break;
            case "Int":
                vm.Int += 3;
                break;
            case "Spd":
                vm.Spd += 3;
                break;
        }

        Assert.That(vm.PreviewLevel, Is.EqualTo(6),
            $"a spinner parked on the mob's level should ride up when {stat} lifts that level");
    }

    [Test]
    public void Spinner_FollowsMob_Down()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 35));   // level 6; parked on 6
        vm.Str = 32;                                     // drops to level 5
        Assert.That(vm.PreviewLevel, Is.EqualTo(5));
    }

    [Test]
    public void Spinner_DoesNotMove_WhenStatEditKeepsSameLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // level 5 (sum 32)
        vm.Str = 33;                                     // sum 33 → still level 5
        Assert.That(vm.PreviewLevel, Is.EqualTo(5),
            "an edit that doesn't change the mob's level must leave the spinner alone");
    }

    // ── A custom (scrubbed) preview level detaches from the mob ────────────────

    [Test]
    public void Spinner_StaysCustom_WhenScrubbedOffOwnLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // level 5
        vm.PreviewLevel = 20;                            // designer picks a custom preview band
        vm.Str = 35;                                     // mob rises to level 6...
        Assert.That(vm.PreviewLevel, Is.EqualTo(20),     // ...but the custom preview must not be dragged
            "a custom preview level must not follow stat edits");
    }

    [Test]
    public void Spinner_ReAttaches_WhenScrubbedBackToOwnLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(str: 32));   // level 5
        vm.PreviewLevel = 20;                            // detach to a custom level
        vm.Str = 35;                                     // level 6; spinner stays 20
        Assume.That(vm.PreviewLevel, Is.EqualTo(20));

        vm.PreviewLevel = 6;                             // scrub back onto the mob's CURRENT level (6)
        vm.Str = 38;                                     // sum 38 → level 7
        Assert.That(vm.PreviewLevel, Is.EqualTo(7),
            "returning the spinner to the mob's level re-enables auto-follow");
    }

    // Guards the auto-follow ANCHOR after an online load: ApplyPacket must re-seed _ownLevel to the loaded level,
    // not leave it at the placeholder 1 — otherwise a later stat edit wouldn't recognize the spinner as "parked"
    // and would refuse to follow.  (Setting PreviewLevel alone, without the anchor, would pass the test above but
    // fail this one.)
    [Test]
    public void ApplyPacket_AnchorsFollow_AtLoadedLevel()
    {
        var vm = new NpcRowViewModel(1, Npc(name: "Goblin"), isLoaded: false);  // placeholder level 1
        vm.ApplyPacket(Packet(str: 32));                                        // loads level 5
        Assume.That(vm.PreviewLevel, Is.EqualTo(5));

        vm.Str = 35;                                                            // level 6
        Assert.That(vm.PreviewLevel, Is.EqualTo(6),
            "after an online load a stat edit must ride from the LOADED level, proving the anchor was re-seeded");
    }
}
