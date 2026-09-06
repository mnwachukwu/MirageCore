using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.RoundTrip;

/// <summary>
/// Guards the push-changes prompt (the unsaved-edits dialog raised on connect / reconnect / disconnect /
/// close) against silently pushing a THINNER record than an ordinary save would.
/// <para>A second, inline copy of the packet-building is the hazard: any field it misses is one the
/// per-type editor writes and the packet carries correctly, so the loss is invisible until an author
/// reopens the record. The dialog therefore sends exactly <c>row.BuildSavePacket()</c>, and pinning that
/// one projection pins the push too. Each fixture below fills EVERY field with a value distinguishable
/// from the default, so a field left unassigned in a future edit of the mapping fails here rather than
/// reaching a server.</para>
/// </summary>
[TestFixture]
public class PushChangesPacketCoverageTests
{
    // A light that shares no component with the LightSpec default (all-zero) or with Torch, so a dropped
    // Light field can't accidentally match.
    private static readonly LightSpec Lantern = new(0x3366CC, 7.5f, FlickerStyle.Pulse, 0.5f);

    // Every field non-default, and every numeric one distinct, so a mapping that assigns the wrong source
    // property is caught alongside one that assigns nothing at all.
    private static NpcRecord FullNpc() => new()
    {
        Name = "Cave Troll", AttackSay = "Rrraagh!", Sprite = 42, SpriteSheet = 2, Size = 3, SpawnSecs = 90,
        Behavior = NpcBehavior.AttackOnSight, Group = 7, Range = 5,
        Drops = [new NpcDrop { ItemNum = 12, Quantity = 250, Chance = 35 },
                 new NpcDrop { ItemNum = 7, Chance = 3 }],
        Str = 61, Def = 62, Spd = 63, Int = 64,
        ExtraHp = 1500, IsBoss = true, EmitsLight = true, Light = Lantern,
    };

    [Test]
    public void Npc_PushedPacket_CarriesEveryField()
    {
        var vm = new NpcRowViewModel(5, FullNpc());

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.NpcNum, Is.EqualTo(5));
            Assert.That(pkt.Name, Is.EqualTo("Cave Troll"));
            Assert.That(pkt.AttackSay, Is.EqualTo("Rrraagh!"));
            Assert.That(pkt.Sprite, Is.EqualTo(42));
            Assert.That(pkt.SpawnSecs, Is.EqualTo(90));
            Assert.That(pkt.Behavior, Is.EqualTo(NpcBehavior.AttackOnSight));
            Assert.That(pkt.Range, Is.EqualTo(5));
            // The whole drop TABLE has to survive the projection, not just its first line — a push that
            // silently kept one drop would be the same class of bug as the footprint reset below.
            Assert.That(pkt.Drops, Is.Not.Null);
            Assert.That(pkt.Drops!, Has.Count.EqualTo(2));
            Assert.That(pkt.Drops![0].ItemNum, Is.EqualTo(12));
            Assert.That(pkt.Drops![0].Quantity, Is.EqualTo((short)250));
            Assert.That(pkt.Drops![0].Chance, Is.EqualTo((short)35));
            Assert.That(pkt.Drops![1].ItemNum, Is.EqualTo(7));
            Assert.That(pkt.Drops![1].Chance, Is.EqualTo((short)3));
            Assert.That(pkt.Str, Is.EqualTo(61));
            Assert.That(pkt.Def, Is.EqualTo(62));
            Assert.That(pkt.Spd, Is.EqualTo(63));
            Assert.That(pkt.Int, Is.EqualTo(64));

            // The six a thinner projection drops: pushing a dirty NPC would reset its footprint to 1x1,
            // clear its comrade group, drop its boss HP padding and boss flag, and switch its light
            // emitter off.
            Assert.That(pkt.Size, Is.EqualTo(3), "a pushed NPC must keep its footprint size");
            Assert.That(pkt.Group, Is.EqualTo(7), "a pushed NPC must keep its comrade group");
            Assert.That(pkt.ExtraHp, Is.EqualTo(1500), "a pushed NPC must keep its extra HP");
            Assert.That(pkt.IsBoss, Is.True, "a pushed NPC must keep its boss flag");
            Assert.That(pkt.EmitsLight, Is.True, "a pushed NPC must keep emitting light");
            Assert.That(pkt.Light, Is.EqualTo(Lantern), "a pushed NPC must keep its light attributes");
            Assert.That(pkt.SpriteSheet, Is.EqualTo(2), "a pushed NPC must keep the sheet its sprite is on");
        });
    }

    [Test]
    public void Item_PushedPacket_CarriesEveryField()
    {
        var vm = new ItemRowViewModel(4, new ItemRecord
        {
            Name = "Bound Blade", Pic = 21, ItemSheet = 3, Type = ItemType.Weapon, Durability = 120, Power = 14, AllowedClasses = [3, 1],
            NonTradeable = true, NonListable = true, NonMailable = true, DestroyOnDrop = true,
        });

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.ItemNum, Is.EqualTo(4));
            Assert.That(pkt.Name, Is.EqualTo("Bound Blade"));
            Assert.That(pkt.Pic, Is.EqualTo((short)21));
            Assert.That(pkt.ItemSheet, Is.EqualTo((short)3), "a pushed item must keep the sheet its picture is on");
            Assert.That(pkt.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(pkt.Durability, Is.EqualTo((short)120));
            Assert.That(pkt.Power, Is.EqualTo((short)14));
            // Sorted by the save-path normalize, not left in the order it was authored.
            Assert.That(pkt.AllowedClasses, Is.EqualTo(new short[] { 1, 3 }));

            // The four a thinner projection leaves false: pushing a dirty item would make a bound,
            // unlistable, unmailable, destroy-on-drop weapon freely tradeable again.
            Assert.That(pkt.NonTradeable, Is.True, "a pushed item must keep its no-trade restriction");
            Assert.That(pkt.NonListable, Is.True, "a pushed item must keep its no-list restriction");
            Assert.That(pkt.NonMailable, Is.True, "a pushed item must keep its no-mail restriction");
            Assert.That(pkt.DestroyOnDrop, Is.True, "a pushed item must keep its destroy-on-drop flag");
        });
    }

    [Test]
    public void Class_PushedPacket_CarriesEveryField()
    {
        var vm = new ClassRowViewModel(2, new ClassRecord
        {
            Name = "Ranger", SpriteMale = 17, SpriteFemale = 27,
            SpriteSheetMale = 4, SpriteSheetFemale = 6,
            Str = 8, Def = 6, Spd = 9, Int = 4,
        });

        var pkt = vm.BuildSavePacket();

        Assert.Multiple(() =>
        {
            Assert.That(pkt.ClassNum, Is.EqualTo(2));
            Assert.That(pkt.Name, Is.EqualTo("Ranger"));
            Assert.That(pkt.SpriteMale, Is.EqualTo(17));
            Assert.That(pkt.SpriteFemale, Is.EqualTo(27));
            Assert.That(pkt.SpriteSheetMale, Is.EqualTo(4), "a pushed class must keep the sheet its male sprite is on");
            Assert.That(pkt.SpriteSheetFemale, Is.EqualTo(6), "and the separate sheet its female sprite is on");
            Assert.That(pkt.Str, Is.EqualTo(8));
            Assert.That(pkt.Def, Is.EqualTo(6));
            Assert.That(pkt.Spd, Is.EqualTo(9));
            Assert.That(pkt.Int, Is.EqualTo(4));
        });
    }

    // ── The dialog itself: every listed row type must reach the send path ──────
    //
    // A row type with no online arm is skipped in silence — the loop falls through, nothing is sent, and
    // the dialog reports success, which is exactly how a dirty record goes missing. These tests detect
    // "was it sent at all?" without a server: the dialog's connection is not connected, so SendSaveAsync
    // throws, the catch sets the status line, and ProceedConfirmed does NOT fire. So a row that reaches the
    // send path leaves ProceedConfirmed unraised, while a skipped row sails through and raises it.
    private static bool Pushed(object dirtyRow)
    {
        var conn = new EditorConnection();          // never connected: SendSaveAsync throws
        var data = new EditorDataService();
        // isConnecting/isClosing false = the online push branch (the disconnect wording).
        var dialog = new PushChangesDialogViewModel([dirtyRow], conn, data);
        bool proceeded = false;
        dialog.ProceedConfirmed += () => proceeded = true;

        dialog.SaveAndProceedCommand.Execute(null);

        return !proceeded;   // the send was attempted (and failed) rather than skipped
    }

    [Test]
    public void Push_SendsDirtyClass()
    {
        var row = new ClassRowViewModel(2, new ClassRecord { Name = "Ranger" });
        row.Str = 11;
        Assume.That(row.IsDirty, "precondition: the row is dirty, so the dialog would list it");

        Assert.That(Pushed(row), Is.True,
            "a dirty class must be pushed like every other record type, not silently skipped");
    }

    // Control for the mechanism above: a type that always had an online arm must read the same way, so a
    // passing Push_SendsDirtyClass can't be an artifact of how "was it sent" is detected.
    [Test]
    public void Push_SendsDirtyNpc()
    {
        var row = new NpcRowViewModel(5, FullNpc());
        row.Str = 71;
        Assume.That(row.IsDirty, "precondition: the row is dirty, so the dialog would list it");

        Assert.That(Pushed(row), Is.True);
    }

    // Negative control: a row type the switch does not handle falls straight through and the dialog reports
    // success. This is the shape of the bug, and it proves the two tests above are not vacuous — if a
    // skipped row also read as "pushed", they would pass no matter what the switch covered.
    [Test]
    public void Push_SkipsUnhandledRowType()
    {
        Assert.That(Pushed(new object()), Is.False,
            "an unhandled row type is silently skipped — the failure mode the class arm was missing");
    }
}
