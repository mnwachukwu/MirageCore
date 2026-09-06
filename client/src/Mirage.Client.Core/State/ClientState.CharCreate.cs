using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>
/// The character-create screen's own slice of state: what each class starts with, and just enough of
/// the item and spell tables to describe it.
///
/// <para>Deliberately separate from <see cref="Items"/> / <see cref="SpellDefs"/>, which are the
/// in-game tables filled at join. A player on this screen has not joined — these few dozen records
/// arrive with the class list and are the only definitions that exist yet. Keeping them apart means
/// nothing in the game ever reads a half-populated table by accident, and the create screen is never
/// tempted to reach for one that is empty.</para>
/// </summary>
public sealed partial class ClientState
{
    /// <summary>A carried starting item: its number, plus the stack size for currency (0 otherwise).</summary>
    public readonly record struct CarriedStart(int Num, int Value);

    /// <summary>What a new character of one class is created with, already resolved by the server
    /// against the same gates character creation applies — worn is worn, carried is carried, and
    /// anything the class could not use was dropped before it got here.</summary>
    public sealed record ClassLoadout(int[] Worn, CarriedStart[] Carried, int[] Spells)
    {
        public static readonly ClassLoadout Empty = new([], [], []);
    }

    // All three are 1-based/num-keyed and sized to what actually arrived, so an unreferenced number
    // reads as absent rather than as a blank record. The accessors below do the bounds check once.
    private ClassLoadout[] _classLoadouts = [];
    private ItemRecord?[] _loadoutItems = [];
    private SpellRecord?[] _loadoutSpells = [];

    /// <summary>The loadout for a 1-based class number, or <see cref="ClassLoadout.Empty"/> if that
    /// class grants nothing — which is a real authored state, not an error.</summary>
    public ClassLoadout LoadoutFor(int classNum) =>
        classNum > 0 && classNum < _classLoadouts.Length ? _classLoadouts[classNum] : ClassLoadout.Empty;

    /// <summary>An item definition referenced by some class's loadout, or null.</summary>
    public ItemRecord? LoadoutItem(int num) =>
        num > 0 && num < _loadoutItems.Length ? _loadoutItems[num] : null;

    /// <summary>A spell definition referenced by some class's loadout, or null.</summary>
    public SpellRecord? LoadoutSpell(int num) =>
        num > 0 && num < _loadoutSpells.Length ? _loadoutSpells[num] : null;

    /// <summary>The whole item side of the catalog, num-keyed — the shape the spell tooltip wants for
    /// its reagent lookup.</summary>
    public ItemRecord?[] LoadoutItemDefs => _loadoutItems;

    /// <summary>Replace the whole character-create slice from a fresh class list. Wholesale rather than
    /// merged: the packet is the complete answer for the world as it stands, and a leftover entry from a
    /// previous connection would describe a different world.</summary>
    public void SetClassLoadouts(NewCharClassesPacket p)
    {
        _classLoadouts = new ClassLoadout[p.Classes.Length + 1];   // 1-based; index 0 unused
        _classLoadouts[0] = ClassLoadout.Empty;
        for (int i = 0; i < p.Classes.Length; i++)
        {
            var c = p.Classes[i];
            _classLoadouts[i + 1] = new ClassLoadout(
                c.Worn ?? [],
                c.Carried is null ? [] : [.. c.Carried.Select(x => new CarriedStart(x.Num, x.Quantity))],
                c.Spells ?? []);
        }

        _loadoutItems = new ItemRecord?[MaxNumOf(p.ItemDefs.Select(d => d.Num)) + 1];
        foreach (var d in p.ItemDefs)
        {
            _loadoutItems[d.Num] = new ItemRecord
            {
                Name = d.Name,
                Pic = d.Pic,
                ItemSheet = d.ItemSheet,
                Type = d.Type,
                Durability = d.Durability,
                VitalAmount = d.VitalAmount,
                Power = d.Power,
                LevelReq = d.LevelReq,
                AllowedClasses = d.AllowedClasses,
            };
        }

        _loadoutSpells = new SpellRecord?[MaxNumOf(p.SpellDefs.Select(d => d.Num)) + 1];
        foreach (var d in p.SpellDefs)
        {
            _loadoutSpells[d.Num] = new SpellRecord
            {
                Name = d.Name,
                Type = d.Type,
                VitalAmount = d.VitalAmount,
                IntReq = d.IntReq,
                LevelReq = d.LevelReq,
                AllowedClasses = d.AllowedClasses,
            };
        }
    }

    private static int MaxNumOf(IEnumerable<int> nums)
    {
        int max = 0;
        foreach (int n in nums) if (n > max) max = n;
        return max;
    }
}
