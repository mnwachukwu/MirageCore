using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ClassRecord
{
    private string _name = string.Empty;
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — class-requirement messages TrimEnd
    /// the class name when a player tries to learn an off-class spell.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    /// <summary>A short pitch shown on the character-create screen — what this class is FOR, in the
    /// player's terms, not the designer's. Two short sentences is the shape the ten shipped classes use
    /// ("Fights for honor. High power melee."). Empty is fine; the screen simply shows nothing.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The sprite a character of this class is created with, one per sex. Chosen once, at
    /// creation, and copied onto the character — so changing a class's art later leaves existing
    /// characters looking exactly as their players last saw them.</summary>
    public int SpriteMale { get; set; }
    public int SpriteFemale { get; set; }

    /// <summary>Which sprite sheet each of the two above is a row of. Always written, including at 0.</summary>
    public int SpriteSheetMale { get; set; }
    public int SpriteSheetFemale { get; set; }

    /// <summary>Legacy single sprite, kept only so a world authored before the split still loads.
    /// <see cref="Normalize"/> folds it into both of the above and clears it, so it is written back out
    /// only for as long as a file still carries it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Sprite { get; set; }

    /// <summary>The sprite this class gives <paramref name="sex"/>. The single place that mapping is
    /// made, so the create screen's preview, the grant at creation, and anything later cannot disagree
    /// about which art a character of a given sex gets.</summary>
    public int SpriteFor(Sex sex) => sex == Sex.Female ? SpriteFemale : SpriteMale;

    /// <summary>The sheet <see cref="SpriteFor"/>'s row is on. Both are read for the same sex.</summary>
    public int SpriteSheetFor(Sex sex) => sex == Sex.Female ? SpriteSheetFemale : SpriteSheetMale;

    public int Str { get; set; }
    public int Def { get; set; }
    public int Spd { get; set; }
    public int Int { get; set; }

    /// <summary>What a new character of this class is created holding. Null or empty = nothing, which is
    /// a valid class.
    ///
    /// <para>NOTHING REQUIRES OPENING THE BAG. Equipment arrives already WORN and everything else is
    /// carried, so there is no "equipped" flag to author and no way to author the wrong one. Asking a
    /// first-time player to open a bag and work out what goes where is a worse opening than any gear is
    /// worth, and mis-authoring a piece into a bag they cannot wear is a worse story still.</para>
    ///
    /// <para>Both gates are checked against the class's BASE stats at grant time — a new character has
    /// exactly <see cref="Str"/>/<see cref="Def"/>/<see cref="Spd"/>/<see cref="Int"/>, so the question
    /// is always "could a brand-new level-1 character of this class equip this?" A piece that fails is
    /// carried rather than worn, never silently dropped.</para></summary>
    public List<ClassStartingItem>? StartingItems { get; set; }

    /// <summary>Spells a new character of this class already knows (1-based spell numbers). Learned
    /// outright — no scroll, no study step. Null or empty = none, which is the Knight's whole book.</summary>
    public List<int>? StartingSpells { get; set; }

    // Computed via StatFormulas at runtime — not persisted.
    [JsonIgnore] public int Hp { get; set; }
    [JsonIgnore] public int Mp { get; set; }
    [JsonIgnore] public int Sp { get; set; }

    /// <summary>Canonicalize the starting loadout: drop inert lines, cap to what a character can actually
    /// hold, and collapse empty lists to null so a class that grants nothing carries no key. Idempotent —
    /// it runs on load and on every editor save, like the other record Normalizes.</summary>
    public void Normalize()
    {
        // A world authored before the sprite split gives both sexes the art it already had, which is
        // exactly how it looked. Only migrates when neither new field is set, so a class that has been
        // given real per-sex art is never overwritten by a stale legacy value.
        if (Sprite > 0 && SpriteMale == 0 && SpriteFemale == 0)
        {
            SpriteMale = Sprite;
            SpriteFemale = Sprite;
        }
        Sprite = 0;

        if (StartingItems is not null)
        {
            StartingItems.RemoveAll(s => s.ItemNum <= 0);
            // A character has MaxInv slots and nothing has been picked up yet, so this is the real
            // ceiling rather than an arbitrary one.
            if (StartingItems.Count > Constants.MaxInv)
                StartingItems.RemoveRange(Constants.MaxInv, StartingItems.Count - Constants.MaxInv);
            if (StartingItems.Count == 0) StartingItems = null;
        }
        if (StartingSpells is not null)
        {
            StartingSpells.RemoveAll(n => n <= 0);
            // Dedupe: the spellbook is a set, and granting the same spell twice would burn a slot for
            // nothing (SpellSystem.HasSpell would reject the second at any other callsite).
            var seen = new HashSet<int>();
            StartingSpells.RemoveAll(n => !seen.Add(n));
            if (StartingSpells.Count > Constants.MaxPlayerSpells)
                StartingSpells.RemoveRange(Constants.MaxPlayerSpells, StartingSpells.Count - Constants.MaxPlayerSpells);
            if (StartingSpells.Count == 0) StartingSpells = null;
        }
    }
}
