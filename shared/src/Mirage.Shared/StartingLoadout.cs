using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// What a brand-new character of a class ACTUALLY receives, resolved from that class's authored
/// starting loadout.
///
/// <para>Shared because two callers have to agree exactly: character creation, which grants the
/// loadout, and the character-create screen, which previews it. A preview derived independently would
/// be a second copy of these gates, free to drift, and a drift shows up as a player being promised a
/// sword they do not get.</para>
///
/// <para>NOBODY STARTS WITH SOMETHING THEY CANNOT USE. An authored line whose gates the class fails is
/// SKIPPED, not granted-and-carried; the class editor's warning column is what should have caught it.
/// Equipment that passes arrives WORN, so nothing requires opening the bag.</para>
///
/// <para>Every gate reads the class's BASE stats, because that is exactly what the character has at
/// this moment — creation copies Str/Def/Spd/Int off the class and sets level 1. There is no character
/// to pass in: while this question is being asked, the class IS the character.</para>
/// </summary>
public static class StartingLoadout
{
    /// <summary>The level a character is created at. Every level gate below is asked against it.</summary>
    public const int CreationLevel = 1;

    /// <summary>One granted item: the bag slot it lands in, what it is, and — via
    /// <see cref="Worn"/> — whether it arrives equipped rather than carried.</summary>
    public readonly record struct GrantedItem(int Slot, int Num, short Value, ItemType Type, short Durability)
    {
        /// <summary>Equipment is worn on arrival; everything else is carried.</summary>
        public bool Worn => ItemRecord.IsEquipment(Type);
    }

    /// <summary>Resolve the class's authored items into the bag slots a new character of it would get.
    /// Slots are assigned in authored order, skipping every line that fails a gate, so the returned
    /// slot numbers are contiguous from 1.</summary>
    public static List<GrantedItem> ResolveItems(ClassRecord cls, int classNum, ItemRecord[] items)
    {
        var granted = new List<GrantedItem>();
        int slot = 1;
        foreach (var start in cls.StartingItems ?? [])
        {
            if (slot > Constants.MaxInv) break;
            if (start.ItemNum < 1 || start.ItemNum >= items.Length) continue;
            var item = items[start.ItemNum];
            if (string.IsNullOrEmpty(item.Name)) continue;   // an authored reference to a blank slot

            // The level gate applies to equipment AND potions alike (ItemRecord.UsesLevelReq), and a
            // level-1 character clears only a level-1 line. Currency has no gate at all.
            if (ItemRecord.UsesLevelReq(item.Type) && item.LevelReq > CreationLevel) continue;

            if (ItemRecord.IsEquipment(item.Type))
            {
                if (!ClassGate.Allows(item.AllowedClasses, classNum)) continue;
                if (CombatFormulas.GearStatRequirement(item.Power, ClassStatFor(cls, item.Type))
                    > ClassStatFor(cls, item.Type)) continue;
            }

            // Currency stacks; everything else is exactly one (the engine reads Value only for
            // currency), so it is normalized here rather than trusted from the record.
            short value = item.Type == ItemType.Currency ? Math.Max((short)1, start.Quantity) : (short)0;
            granted.Add(new GrantedItem(slot, start.ItemNum, value, item.Type, item.Durability));
            slot++;
        }
        return granted;
    }

    /// <summary>Resolve the class's authored spells into the book a new character of it would open with.
    /// Learned outright — no scroll, no study step. The class and INT gates are checked for the same
    /// reason the equipment ones are: an authored spell the class could never cast would sit in the book
    /// forever, and the picker that authored it should have prevented that.</summary>
    public static List<int> ResolveSpells(ClassRecord cls, int classNum, SpellRecord[] spells)
    {
        var granted = new List<int>();
        foreach (int spellNum in cls.StartingSpells ?? [])
        {
            if (granted.Count >= Constants.MaxPlayerSpells) break;
            if (spellNum < 1 || spellNum >= spells.Length) continue;
            var spell = spells[spellNum];
            if (string.IsNullOrEmpty(spell.Name)) continue;
            if (!ClassGate.Allows(spell.AllowedClasses, classNum)) continue;
            if (spell.LevelReq > CreationLevel) continue;
            if (CombatFormulas.GetSpellIntRequirement(spell, cls.Int) > cls.Int) continue;
            granted.Add(spellNum);
        }
        return granted;
    }

    /// <summary>The 1-based spell slot a new character arrives with PREPARED, or 0 when its book holds
    /// nothing preparable. Takes the spells <see cref="ResolveSpells"/> granted, so the slot numbers line
    /// up with the book that was actually written.
    ///
    /// <para>Only a SubHp spell can occupy the prepared slot — it IS the caster's weapon, which is the
    /// same rule the SetPreparedSpell handler enforces — so a class whose opening book has one arrives
    /// armed rather than having to open the spell panel before its first fight. A melee class grants no
    /// SubHp spell and gets 0, exactly as it would after clearing the slot by hand.</para></summary>
    public static int ResolvePreparedSlot(IReadOnlyList<int> grantedSpells, SpellRecord[] spells)
    {
        for (int i = 0; i < grantedSpells.Count; i++)
        {
            int num = grantedSpells[i];
            if (num >= 1 && num < spells.Length && spells[num].Type == SpellType.SubHp) return i + 1;
        }
        return 0;
    }

    /// <summary>Which stat gates which slot: STR for a weapon, DEF for the rest. Read twice per line —
    /// once as the class base that earns the affinity head-start, once as the stat that has to clear the
    /// requirement. At creation those are the same number, and this is the one moment in the game where
    /// that is true, which is exactly why this lives here and not on the equip path.</summary>
    private static int ClassStatFor(ClassRecord cls, ItemType type) =>
        type == ItemType.Weapon ? cls.Str : cls.Def;
}
