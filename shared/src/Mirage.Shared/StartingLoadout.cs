using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// What a brand-new character ACTUALLY receives, resolved from the world's authored starting items.
///
/// <para>Shared because two callers have to agree exactly: character creation, which grants the loadout,
/// and the character-create screen, which previews it. A preview derived independently would be a second
/// copy of these rules, free to drift, and a drift shows up as a player being promised a sword they do not
/// get.</para>
///
/// <para>Core grants what the world authored and asks nothing else of it. Whether a character has earned
/// a thing, or is strong enough to hold it, is a question only a game with those concepts can ask — and a
/// brand-new character has by definition earned nothing, so it is a strange moment to start asking.</para>
/// </summary>
public static class StartingLoadout
{
    /// <summary>One granted item: the bag slot it lands in, what it is, and — via <see cref="GrantedItem.Worn"/>
    /// — whether it arrives equipped rather than carried.</summary>
    public readonly record struct GrantedItem(int Slot, int Num, short Value, ItemType Type, short Durability)
    {
        /// <summary>Equipment is worn on arrival; everything else is carried.</summary>
        public bool Worn => ItemRecord.IsEquipment(Type);
    }

    /// <summary>Resolve the world's authored starting items into the bag slots a new character gets.
    /// Slots are assigned in authored order, skipping every line that names nothing, so the returned slot
    /// numbers are contiguous from 1 and stop at <see cref="Constants.MaxInv"/>.</summary>
    public static List<GrantedItem> ResolveItems(IReadOnlyList<StartingItem> authored, ItemRecord[] items)
    {
        var granted = new List<GrantedItem>();
        int slot = 1;
        foreach (var start in authored)
        {
            if (slot > Constants.MaxInv) break;
            if (start.ItemNum < 1 || start.ItemNum >= items.Length) continue;
            var item = items[start.ItemNum];
            if (string.IsNullOrEmpty(item.Name)) continue;   // an authored reference to a blank slot

            // Currency stacks; everything else is exactly one (the engine reads Value only for currency),
            // so it is normalized here rather than trusted from the record.
            short value = item.Type == ItemType.Currency ? Math.Max((short)1, start.Quantity) : (short)0;
            granted.Add(new GrantedItem(slot, start.ItemNum, value, item.Type, item.Durability));
            slot++;
        }
        return granted;
    }

    /// <summary>Writes the resolved loadout onto a brand-new character: each grant into its own bag slot,
    /// with equipment worn on arrival.
    ///
    /// <para>The one place the grant happens, so a preview built from <see cref="ResolveItems"/> and the
    /// character actually created cannot disagree. Overwrites slots 1..n and touches nothing else; a
    /// character with anything already in the bag is not what this is for.</para></summary>
    public static void Grant(PlayerRecord chr, IReadOnlyList<StartingItem> authored, ItemRecord[] items)
    {
        foreach (var g in ResolveItems(authored, items))
        {
            chr.Inv[g.Slot] = new PlayerInvSlot { Num = g.Num, Quantity = g.Value, Dur = g.Durability };
            if (!g.Worn) continue;
            switch (g.Type)
            {
                case ItemType.Weapon: chr.WeaponSlot = g.Slot; break;
                case ItemType.Armor: chr.ArmorSlot = g.Slot; break;
                case ItemType.Helmet: chr.HelmetSlot = g.Slot; break;
                case ItemType.Shield: chr.ShieldSlot = g.Slot; break;
            }
        }
    }

    /// <summary>Canonical stored form for an authored list: drop the lines that name nothing and cap to
    /// what a character can actually hold, since nothing has been picked up yet.</summary>
    public static List<StartingItem> Normalize(IReadOnlyList<StartingItem>? authored)
    {
        var kept = new List<StartingItem>();
        foreach (var s in authored ?? [])
        {
            if (s.ItemNum <= 0) continue;
            kept.Add(s);
            if (kept.Count == Constants.MaxInv) break;
        }
        return kept;
    }
}
