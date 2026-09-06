using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.Net;

/// <summary>The client caches spell definitions from the wire and reads them back for tooltips, the shop
/// confirmation and the spell book. Every field the packet carries has to survive that copy.
///
/// <para>A dropped field is silent: the record keeps its default, and a reader gated on
/// <c>LevelReq > 0</c> just renders nothing. Both handlers are covered because the bulk send at join
/// and the per-spell push after an editor save build the record independently.</para></summary>
[TestFixture]
public class SpellDefWireTests
{
    private static readonly MethodInfo HandleSend = typeof(ClientPacketHandler)
        .GetMethod("HandleSendSpells", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo HandleUpdate = typeof(ClientPacketHandler)
        .GetMethod("HandleUpdateSpell", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Neither handler touches sender or mapCache, per the client-test convention.
    private static void Apply(ClientState state, MethodInfo handler, object packet)
        => handler.Invoke(new ClientPacketHandler(state, null!, null!), [packet]);

    // Distinct values so a field copied from the wrong source shows up as a wrong number, not a match.
    private const short Vital = 41;
    private const short ItemNum = 42;
    private const short ItemQty = 43;
    private const short IntReq = 44;
    private const short LevelReq = 45;

    private static void AssertCarriesEveryField(SpellRecord? got)
    {
        Assert.That(got, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(got!.Name, Is.EqualTo("Firebolt"));
            Assert.That(got.Type, Is.EqualTo(SpellType.SubHp));
            Assert.That(got.AllowedClasses, Is.EqualTo(new List<short> { 2, 5 }));
            Assert.That(got.VitalAmount, Is.EqualTo(Vital));
            Assert.That(got.ItemNum, Is.EqualTo(ItemNum));
            Assert.That(got.ItemQuantity, Is.EqualTo(ItemQty));
            Assert.That(got.IntReq, Is.EqualTo(IntReq));
            Assert.That(got.LevelReq, Is.EqualTo(LevelReq), "LevelReq gates the tooltip and the shop confirm");
        });
    }

    [Test]
    public void SendSpells_CarriesEveryWireField()
    {
        var state = new ClientState();
        var packet = new SendSpellsPacket
        {
            Spells =
            [
                new SendSpellsPacket.SpellData(7, "Firebolt", [2, 5], SpellType.SubHp,
                    Vital, ItemNum, ItemQty, IntReq, LevelReq),
            ],
        };

        Apply(state, HandleSend, packet);

        AssertCarriesEveryField(state.SpellDefs[7]);
    }

    [Test]
    public void UpdateSpell_CarriesEveryWireField()
    {
        var state = new ClientState();
        var packet = new UpdateSpellPacket
        {
            SpellNum = 7,
            Name = "Firebolt",
            AllowedClasses = [2, 5],
            Type = SpellType.SubHp,
            VitalAmount = Vital,
            ItemNum = ItemNum,
            ItemQuantity = ItemQty,
            IntReq = IntReq,
            LevelReq = LevelReq,
        };

        Apply(state, HandleUpdate, packet);

        AssertCarriesEveryField(state.SpellDefs[7]);
    }

    /// <summary>Every property on the wire record has a same-named property on <see cref="SpellRecord"/>.
    /// Adding a field to the packet without a matching assignment in the handlers is the failure the two
    /// tests above catch only for the fields they already name; this catches the field nobody named.</summary>
    [Test]
    public void EveryWireField_HasAMatchingRecordProperty()
    {
        string[] wireOnly = ["Num"];   // the slot key, not part of the record

        var missing = typeof(SendSpellsPacket.SpellData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Except(wireOnly)
            .Where(name => typeof(SpellRecord).GetProperty(name) is null)
            .ToList();

        Assert.That(missing, Is.Empty,
            $"SpellRecord has no property for wire field(s): {string.Join(", ", missing)}");
    }
}
