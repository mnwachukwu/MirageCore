using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.RoundTrip;

/// <summary>
/// Locks the ONLINE round-trip of <see cref="NpcRowViewModel.Size"/> - the NPC footprint size class
/// (1 = 32x32, 2 = 64x64, 3 = 96x96).  Same failure mode the ExtraHp test guards: if
/// <see cref="UpdateNpcPacket"/> didn't carry Size (or ApplyPacket didn't seed it), an NPC opened online
/// would seed Size = 0 and a later save (EditorSaveNpcPacket.Size = vm.Size) would write that back,
/// silently shrinking a big NPC to 1x1.  ApplyPacket must seed Size from the packet, just as the offline
/// ctor and LoadFromRecord paths already do.  Also pins the EffectiveSize sentinel handling.
/// </summary>
[TestFixture]
public class NpcSizeRoundTripTests
{
    // An online row is built name-only and then filled in by the server packet.
    private static NpcRecord Npc(string name = "Giant") => new() { Name = name };

    // Mirrors the wire packet the editor receives for an online NPC load, now carrying Size.  Light must
    // be set or ApplyPacket throws reading pkt.Light.Rgb (matches NpcExtraHpRoundTripTests.Packet).
    private static UpdateNpcPacket Packet(int size, string name = "Giant") =>
        new() { NpcNum = 1, Name = name, Size = size, Light = LightSpec.Torch };

    [Test]
    public void ApplyPacket_Online_SeedsSizeFromPacket()
    {
        var vm = new NpcRowViewModel(1, Npc(), isLoaded: false);
        Assume.That(vm.Size, Is.EqualTo(1), "precondition: an unloaded online row defaults to the 1x1 size");

        vm.ApplyPacket(Packet(size: 3));   // a 3x3 boss arrives from the server

        Assert.That(vm.Size, Is.EqualTo(3),
            "loading an NPC online must carry the server's Size, not leave it stranded at the default");
    }

    // The reported failure shape: an online load followed by a save must NOT clobber a non-default Size.
    [Test]
    public void ApplyPacket_ThenToRecord_PreservesSize()
    {
        var vm = new NpcRowViewModel(1, Npc(), isLoaded: false);
        vm.ApplyPacket(Packet(size: 2));
        Assert.That(vm.ToRecord().Size, Is.EqualTo(2),
            "a save after an online load must round-trip the loaded Size");
    }

    [Test]
    public void EffectiveSize_NormalizesLegacyZeroToOne()
    {
        Assert.That(new NpcRecord { Size = 0 }.EffectiveSize, Is.EqualTo(1),
            "a legacy/blank record (Size 0 = 'not defined') behaves as the 1x1 default");
    }

    [Test]
    public void EffectiveSize_ClampsAboveMax()
    {
        Assert.That(new NpcRecord { Size = 99 }.EffectiveSize, Is.EqualTo(Constants.MaxNpcSize),
            "an out-of-range Size clamps to the largest footprint class");
    }
}
