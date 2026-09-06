using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.RoundTrip;

/// <summary>
/// Locks the ONLINE round-trip of <see cref="NpcRowViewModel.ExtraHp"/> — the flat 1:1 HP bonus that
/// boss/wall NPCs depend on (see <see cref="NpcRecord.ExtraHp"/>).
/// The bug: <see cref="UpdateNpcPacket"/> carried no ExtraHp field, so an NPC opened online seeded
/// ExtraHp = 0, and a later save (EditorSaveNpcPacket.ExtraHp = vm.ExtraHp) wrote that 0 back — silently
/// wiping a boss's real HP wall.  ApplyPacket must now seed ExtraHp from the packet, just as the offline
/// ctor and LoadFromRecord paths already do.
/// </summary>
[TestFixture]
public class NpcExtraHpRoundTripTests
{
    // An online row is built name-only (ExtraHp = 0) and then filled in by the server packet.
    private static NpcRecord Npc(string name = "Wall") => new() { Name = name };

    // Mirrors the wire packet the editor receives for an online NPC load, now carrying ExtraHp.  Light must
    // be set or ApplyPacket throws reading pkt.Light.Rgb (matches NpcPreviewLevelTests.Packet).
    private static UpdateNpcPacket Packet(int extraHp, int def = 0, string name = "Wall") =>
        new() { NpcNum = 1, Name = name, Def = def, ExtraHp = extraHp, Light = LightSpec.Torch };

    [Test]
    public void ApplyPacket_Online_SeedsExtraHpFromPacket()
    {
        var vm = new NpcRowViewModel(1, Npc(), isLoaded: false);
        Assume.That(vm.ExtraHp, Is.EqualTo(0), "precondition: an unloaded online row sits at ExtraHp 0");

        vm.ApplyPacket(Packet(extraHp: 5000));   // a wall NPC with a big flat HP bonus arrives from the server

        Assert.That(vm.ExtraHp, Is.EqualTo(5000),
            "loading an NPC online must carry the server's ExtraHp, not leave it stranded at 0");
    }

    // The reported failure: an online load followed by a save must NOT clobber a non-zero ExtraHp.  The save
    // packet is built as EditorSaveNpcPacket.ExtraHp = vm.ExtraHp, and ToRecord() is the offline persist
    // mirror, so a surviving value on both is what keeps the boss's HP wall intact across an online edit.
    [Test]
    public void ApplyPacket_ThenToRecord_PreservesExtraHp()
    {
        var vm = new NpcRowViewModel(1, Npc(), isLoaded: false);
        vm.ApplyPacket(Packet(extraHp: 5000));
        Assert.That(vm.ToRecord().ExtraHp, Is.EqualTo(5000),
            "a save after an online load must round-trip the loaded ExtraHp");
    }

    // ExtraHp adds 1:1 on top of the stat-derived pool (StatFormulas.GetNpcMaxHp), so an online load must
    // reflect it in the editor's MaxHp readout — with ExtraHp dropped, a boss previewed as a squishy mob.
    [Test]
    public void ApplyPacket_Online_ExtraHpLiftsMaxHpOneForOne()
    {
        var plain = new NpcRowViewModel(1, Npc(), isLoaded: false);
        plain.ApplyPacket(Packet(extraHp: 0, def: 30));

        var boss = new NpcRowViewModel(1, Npc(), isLoaded: false);
        boss.ApplyPacket(Packet(extraHp: 5000, def: 30));

        Assert.That(boss.MaxHp, Is.EqualTo(plain.MaxHp + 5000),
            "ExtraHp adds 1:1 to the NPC HP pool");
    }
}
