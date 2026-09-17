using Mirage.Shared.Extensibility;

namespace Mirage.Server.Core.World;

/// <summary>
/// The records of every family a MODULE declared, held as bags because Core has no type for them.
///
/// <para><b>Core's own families are typed arrays on <see cref="GameWorld"/>; these cannot be.</b> A game
/// declares a family the engine was never compiled against, so what the engine can hold is the family's
/// id, a slot number, and whatever keys the record carries. The <see cref="RecordFamily"/> the module
/// declared says what those keys mean, and nothing in here reads them.</para>
///
/// <para><b>Slots are 1-based and pre-allocated</b>, exactly like every other family: a world has a fixed
/// number of them and an author fills the ones they want. Asking for a slot that does not exist answers
/// null rather than throwing, because the number comes off the wire.</para>
/// </summary>
public sealed class ModuleRecords
{
    private readonly Dictionary<string, AttributeBag[]> _byFamily = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordFamily> _declared = new(StringComparer.Ordinal);

    /// <summary>Every family id with room here, in declaration order.</summary>
    public IReadOnlyList<string> Families => [.. _byFamily.Keys];

    /// <summary>Makes room for <paramref name="limit"/> records of this family, all blank. Declaring the
    /// same family twice replaces what was there, as a reload needs.</summary>
    public void Declare(RecordFamily family, int limit)
    {
        ArgumentNullException.ThrowIfNull(family);

        var slots = new AttributeBag[Math.Max(limit, 0) + 1];
        for (int i = 0; i < slots.Length; i++) slots[i] = new AttributeBag();
        _byFamily[family.Id] = slots;
        _declared[family.Id] = family;
    }

    /// <summary>Replaces a family's records wholesale — what a load from disk hands back.</summary>
    public void Adopt(RecordFamily family, AttributeBag[] records)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(records);
        _byFamily[family.Id] = records;
        _declared[family.Id] = family;
    }

    /// <summary>The family a module declared under that id, or null. Saving a record needs the family
    /// rather than its id — it names the folder and the filename the record is written as.</summary>
    public RecordFamily? Family(string? familyId) =>
        familyId is not null && _declared.TryGetValue(familyId, out var family) ? family : null;

    /// <summary>Whether this world holds records for that family.</summary>
    public bool Has(string? familyId) => familyId is not null && _byFamily.ContainsKey(familyId);

    /// <summary>How many slots that family has here. 0 for one this world does not hold, which reads the
    /// same as a family with no room — a caller stops either way.</summary>
    public int Limit(string? familyId) =>
        familyId is not null && _byFamily.TryGetValue(familyId, out var slots) ? slots.Length - 1 : 0;

    /// <summary>The record in that slot, or null when the family or the slot does not exist.</summary>
    public AttributeBag? Get(string? familyId, int num)
    {
        if (familyId is null || !_byFamily.TryGetValue(familyId, out var slots)) return null;
        return num >= 1 && num < slots.Length ? slots[num] : null;
    }

    /// <summary>Stores a record, replacing what the slot held. Silently ignores a slot that does not
    /// exist: the number arrives from an editor, and refusing is the whole response.</summary>
    public void Set(string? familyId, int num, AttributeBag record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (familyId is null || !_byFamily.TryGetValue(familyId, out var slots)) return;
        if (num < 1 || num >= slots.Length) return;
        slots[num] = record;
    }

    /// <summary>Every slot of one family in slot order, blanks included, or empty for a family this world
    /// does not hold. Slot <c>n</c> is at index <c>n-1</c>.</summary>
    public IReadOnlyList<AttributeBag> All(string? familyId)
    {
        if (familyId is null || !_byFamily.TryGetValue(familyId, out var slots)) return [];
        return [.. slots.Skip(1)];
    }

    /// <summary>How many slots this family has, without copying them out to count them.</summary>
    public int CountOf(string? familyId) =>
        familyId is not null && _byFamily.TryGetValue(familyId, out var slots) ? slots.Length - 1 : 0;
}
