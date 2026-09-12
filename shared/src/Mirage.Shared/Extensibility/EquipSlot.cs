using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One place on a character that something can be worn.
///
/// <para><b>Core knows that things are worn; it does not know what they are.</b> A weapon hand, a helm, a
/// saddle, a badge, a paint job — which slots exist and what they are called is a game's decision, and
/// there is nothing in the engine that could make it. What the engine owns is the part that is the same
/// everywhere: one item per slot, worn out of the bag rather than copied into it, put back when the item
/// leaves, and refused when the bag slot is empty.</para>
///
/// <para><b>An engine with no module loaded has no slots at all</b>, so nothing can be worn. That is the
/// same answer <c>DecalSystem.Deposit</c> gives: the machinery is present and nothing drives it.</para>
/// </summary>
public sealed record EquipSlot
{
    /// <summary>Stable identifier, written to a character's save and carried on the wire.
    ///
    /// <para><b>Persisted, so it cannot be renamed freely.</b> A character wearing something in a slot
    /// whose key changed is wearing it in a slot nothing declares, which reads as an empty slot and a
    /// bag item that will not come off.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>Localization key for the caption beside the slot.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Where this sits among the others. Lower shows first; equal values keep declaration
    /// order, so a game that does not care may leave them all at zero.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
}

/// <summary>
/// The equipment slots a world has, in display order.
///
/// <para>Ordered and indexed once at load, because every wear, remove, drop, sale and bag redraw asks it
/// the same two questions — what slots are there, and is this key one of them.</para>
/// </summary>
public sealed class EquipSlotSet
{
    /// <summary>A world where nothing can be worn. What Core describes before a game declares a slot.</summary>
    public static readonly EquipSlotSet Empty = new([]);

    private readonly Dictionary<string, EquipSlot> _byKey;

    public EquipSlotSet(IReadOnlyList<EquipSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Slots = [.. slots.OrderBy(s => s.Ordinal)];
        _byKey = Slots.ToDictionary(s => s.Key, StringComparer.Ordinal);
    }

    /// <summary>Every slot, in the order a character sheet should show them.</summary>
    public IReadOnlyList<EquipSlot> Slots { get; }

    public int Count => Slots.Count;

    /// <summary>Whether this world has a slot by that key. The question every wear has to ask, because an
    /// item may name a slot the loaded game does not declare.</summary>
    public bool Has(string? key) => key is not null && _byKey.ContainsKey(key);

    /// <summary>The slot with this key, or null for one this world does not have.</summary>
    public EquipSlot? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var s) ? s : null;
}
