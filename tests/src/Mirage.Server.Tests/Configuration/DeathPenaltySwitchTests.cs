using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Configuration;

/// <summary>
/// The three death-penalty switches, pinned one at a time, all off, and all on — that last being the
/// important case, since every existing server runs it.
///
/// <para>These call the penalty helpers rather than killing anybody: the switches are gated inside the
/// helpers, so the gate is what needs pinning, and a real kill would mean standing up the respawn state
/// machine and the dropped-item save queue to assert on three booleans.</para>
///
/// <para>NOT covered, because the switches must not reach them: the sub-level-10 spare and the
/// drop-before-degrade ordering. Both live at the call sites, untouched.</para>
/// </summary>
[TestFixture]
public class DeathPenaltySwitchTests
{
    const int Map = 1, Idx = 1;
    const int WeaponNum = 10, ArmorNum = 11, HelmetNum = 12, ShieldNum = 13;
    const int TrinketNum = 20, SpellNum = 5;
    const short MaxDur = 100;
    const int NormalWearPercent = 10;   // what a non-PK death passes to DegradeEquipped
    const int BagSlot = 5, BagSlots = 4;   // slots 5..8 — unequipped, droppable
    const int ReagentSlot = 9;

    static readonly ServerConfig AllOff = new()
    {
        DeathPenalty = new DeathPenaltyConfig { DurabilityLoss = false, ItemDrop = false, ExpLoss = false },
    };

    static ServerConfig Without(bool durability = true, bool drop = true, bool exp = true) => new()
    {
        DeathPenalty = new DeathPenaltyConfig { DurabilityLoss = durability, ItemDrop = drop, ExpLoss = exp },
    };

    // ── All on: the stock game, which must be exactly what it was ─────────────

    [Test]
    public void EverySwitchOn_IsTheStockGame()
    {
        // The regression guard for the whole change. Any of these three going quiet means a default
        // server stopped charging for death, which is the one outcome nobody would report as a bug.
        var (combat, p) = Setup(ServerConfig.Default);
        long expBefore = p.Exp;

        combat.DegradeEquipped(Idx, NormalWearPercent);
        combat.DropNonEquippedInventory(Idx);
        long lost = combat.ApplyExpLoss(Idx, 1_000);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.LessThan(MaxDur), "worn gear still takes death damage");
            Assert.That(OccupiedBagSlots(p), Is.Zero, "the bag still empties on a PK death");
            Assert.That(lost, Is.EqualTo(1_000), "EXP loss still returns what it took");
            Assert.That(p.Exp, Is.EqualTo(expBefore - 1_000));
        });
    }

    // ── One switch at a time ──────────────────────────────────────────────────

    [Test]
    public void DurabilityOff_SparesGear_AndLeavesTheOtherTwoAlone()
    {
        var (combat, p) = Setup(Without(durability: false));
        long expBefore = p.Exp;

        combat.DegradeEquipped(Idx, NormalWearPercent);
        combat.DropNonEquippedInventory(Idx);
        long lost = combat.ApplyExpLoss(Idx, 1_000);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.EqualTo(MaxDur), "the weapon is untouched");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(MaxDur), "so is the armor");
            Assert.That(p.Inv[3].Dur, Is.EqualTo(MaxDur), "so is the helmet");
            Assert.That(p.Inv[4].Dur, Is.EqualTo(MaxDur), "so is the shield");
            Assert.That(OccupiedBagSlots(p), Is.Zero, "one switch off does not turn the others off");
            Assert.That(p.Exp, Is.EqualTo(expBefore - lost).And.LessThan(expBefore));
        });
    }

    [Test]
    public void ItemDropOff_KeepsTheBag_AndLeavesTheOtherTwoAlone()
    {
        var (combat, p) = Setup(Without(drop: false));
        long expBefore = p.Exp;

        combat.DropNonEquippedInventory(Idx);       // PK: the whole bag, no roll
        combat.DropRandomNonEquippedInventory(Idx); // normal: a roll per slot
        combat.DropRandomEquipped(Idx);             // PK: a roll per worn piece
        combat.DegradeEquipped(Idx, NormalWearPercent);
        combat.ApplyExpLoss(Idx, 1_000);

        Assert.Multiple(() =>
        {
            Assert.That(OccupiedBagSlots(p), Is.EqualTo(BagSlots), "nothing left the bag by any of the three routes");
            Assert.That(p.Inv[1].Num, Is.EqualTo(WeaponNum), "and nothing was stripped off the body");
            Assert.That(p.Inv[1].Dur, Is.LessThan(MaxDur), "gear still wears");
            Assert.That(p.Exp, Is.LessThan(expBefore), "EXP is still lost");
        });
    }

    [Test]
    public void ExpLossOff_KeepsEveryPoint_AndLeavesTheOtherTwoAlone()
    {
        var (combat, p) = Setup(Without(exp: false));
        long expBefore = p.Exp;

        long lost = combat.ApplyExpLoss(Idx, 1_000);
        combat.DegradeEquipped(Idx, NormalWearPercent);
        combat.DropNonEquippedInventory(Idx);

        Assert.Multiple(() =>
        {
            Assert.That(lost, Is.Zero,
                "0 is what the PvP paths read as 'nothing to transfer' — the killer must earn nothing either");
            Assert.That(p.Exp, Is.EqualTo(expBefore));
            Assert.That(p.Level, Is.EqualTo(50), "and no delevel, so no stat drain");
            Assert.That(p.Inv[1].Dur, Is.LessThan(MaxDur), "gear still wears");
            Assert.That(OccupiedBagSlots(p), Is.Zero, "the bag still drops");
        });
    }

    [Test]
    public void EverySwitchOff_MakesDeathFree()
    {
        var (combat, p) = Setup(AllOff);
        long expBefore = p.Exp;

        combat.DegradeEquipped(Idx, NormalWearPercent);
        combat.DestroyCasterDeathReagents(Idx, NormalWearPercent);
        combat.DropNonEquippedInventory(Idx);
        combat.DropRandomNonEquippedInventory(Idx);
        combat.DropRandomEquipped(Idx);
        long lost = combat.ApplyExpLoss(Idx, 1_000);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.EqualTo(MaxDur), "no wear");
            Assert.That(p.Inv[ReagentSlot].Quantity, Is.EqualTo(ReagentsHeld), "no reagents burned");
            Assert.That(OccupiedBagSlots(p), Is.EqualTo(BagSlots), "no drops");
            Assert.That(lost, Is.Zero, "no EXP");
            Assert.That(p.Exp, Is.EqualTo(expBefore));
        });
    }

    // ── Reagents ride durability, not the item drop ───────────────────────────

    [Test]
    public void CastingReagents_RideTheDurabilitySwitch()
    {
        // Reagents leave the bag, so they LOOK like an item drop — but they are priced per cast against
        // the same repair curve a weapon is, which makes them the caster's durability.
        var (offCombat, offPlayer) = Setup(Without(durability: false));
        var (dropOffCombat, dropOffPlayer) = Setup(Without(drop: false));
        var (onCombat, onPlayer) = Setup(ServerConfig.Default);

        offCombat.DestroyCasterDeathReagents(Idx, NormalWearPercent);
        dropOffCombat.DestroyCasterDeathReagents(Idx, NormalWearPercent);
        onCombat.DestroyCasterDeathReagents(Idx, NormalWearPercent);

        Assert.Multiple(() =>
        {
            Assert.That(offPlayer.Inv[ReagentSlot].Quantity, Is.EqualTo(ReagentsHeld),
                "durability off spares reagents");
            Assert.That(dropOffPlayer.Inv[ReagentSlot].Quantity, Is.LessThan(ReagentsHeld),
                "item drop off does NOT — that is the whole point of the grouping");
            Assert.That(onPlayer.Inv[ReagentSlot].Quantity, Is.LessThan(ReagentsHeld),
                "and stock rules burn them");
        });
    }

    // ── Guild war ─────────────────────────────────────────────────────────────

    [Test]
    public void GuildWarWear_RidesTheDurabilitySwitch_AndReportsADeathWorthNothing()
    {
        // A war death's ONLY penalty is gear wear, so the switch has to reach it. What comes back matters
        // as much: Covered=true with nothing drained is what an uncovered death already reports, so the
        // bankruptcy streak is untripped and attrition falls through to the floored base rate.
        var (combat, p) = Setup(Without(durability: false));
        var guild = new GuildRecord { Index = 1, VaultGold = 0 };

        var (covered, drained) = combat.ApplyWarDeathDurability(Idx, guild);

        Assert.Multiple(() =>
        {
            Assert.That(covered, Is.True, "must not read as a vault failure, or five deaths auto-lose the war");
            Assert.That(drained, Is.Zero);
            Assert.That(p.Inv[1].Dur, Is.EqualTo(MaxDur), "and no gear wore");
            Assert.That(p.Inv[ReagentSlot].Quantity, Is.EqualTo(ReagentsHeld), "reagents included");
        });
    }

    [Test]
    public void GuildWarWear_On_StillWearsGear()
    {
        // The control for the test above. An empty vault is deliberate: it takes the whole-or-nothing
        // repair branch that pays nothing, which drops the full doubled wear on the player and keeps
        // the guild bookkeeping (absent in this harness) off the path.
        var (combat, p) = Setup(ServerConfig.Default);
        var guild = new GuildRecord { Index = 1, VaultGold = 0 };

        var (covered, drained) = combat.ApplyWarDeathDurability(Idx, guild);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[1].Dur, Is.LessThan(MaxDur), "war wear lands when the switch is on");
            Assert.That(covered, Is.False, "an empty vault covers nothing");
            Assert.That(drained, Is.Zero, "and therefore drains nothing");
        });
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    const int ReagentsHeld = 10_000;

    static int OccupiedBagSlots(PlayerRecord p)
    {
        int n = 0;
        for (int i = BagSlot; i < BagSlot + BagSlots; i++)
            if (p.Inv[i].Num > 0) n++;
        return n;
    }

    static (CombatSystem Combat, PlayerRecord Player) Setup(ServerConfig config)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        // A real ItemSystem: the wear and destroy paths both push inventory updates through it.
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var combat = new CombatSystem(world, pm, dispatcher, items, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!,
            config: config);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.Level = 50;
        p.Str = p.Def = p.Int = p.Spd = 40;
        // Comfortably clear of the level-50 floor even after the loss, so no test trips the delevel path
        // (which drains stats and revalidates equipment — a different behavior, and not what is under test).
        p.Exp = ExpFormulas.ExpFloorForLevel(50) + 1_000_000;

        foreach (var (num, type) in new[]
                 {
                     (WeaponNum, ItemType.Weapon), (ArmorNum, ItemType.Armor),
                     (HelmetNum, ItemType.Helmet), (ShieldNum, ItemType.Shield),
                 })
        {
            world.Items[num].Name = type.ToString();
            world.Items[num].Type = type;
            world.Items[num].Durability = MaxDur;
            world.Items[num].Power = 20;
            // DestroyOnDrop (the route valor takes) clears the slot without touching the map or the save
            // queue, which this harness has no persistence for. The switch is checked before either exit.
            world.Items[num].DestroyOnDrop = true;
        }

        p.Inv[1].Num = WeaponNum; p.Inv[1].Dur = MaxDur; p.WeaponSlot = 1;
        p.Inv[2].Num = ArmorNum;  p.Inv[2].Dur = MaxDur; p.ArmorSlot = 2;
        p.Inv[3].Num = HelmetNum; p.Inv[3].Dur = MaxDur; p.HelmetSlot = 3;
        p.Inv[4].Num = ShieldNum; p.Inv[4].Dur = MaxDur; p.ShieldSlot = 4;

        world.Items[TrinketNum].Name = "Trinket";
        world.Items[TrinketNum].Type = ItemType.None;
        world.Items[TrinketNum].DestroyOnDrop = true;
        for (int i = BagSlot; i < BagSlot + BagSlots; i++) p.Inv[i].Num = TrinketNum;

        // A caster with a tier and a stack to burn: DestroyCasterDeathReagents prices the loss off the
        // highest-tier known SubHp spell when none is prepared. LevelReq IS the tier — a spell with none
        // has no offensive tier and burns nothing, so this fixture needs one to burn anything at all.
        world.Spells[SpellNum].Name = "Drain";
        world.Spells[SpellNum].Type = SpellType.SubHp;
        world.Spells[SpellNum].LevelReq = 100;
        world.Spells[SpellNum].VitalAmount = 100;
        p.Spell[1] = SpellNum;

        world.Items[Constants.CastingReagentItemIndex].Name = "Reagent";
        world.Items[Constants.CastingReagentItemIndex].Type = ItemType.Currency;
        world.Items[Constants.CastingReagentItemIndex].DestroyOnDrop = true;
        p.Inv[ReagentSlot].Num = Constants.CastingReagentItemIndex;
        p.Inv[ReagentSlot].Quantity = ReagentsHeld;

        return (combat, p);
    }

    // No-op packet dispatcher (per-file convention). Nothing here asserts on what was sent.
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
