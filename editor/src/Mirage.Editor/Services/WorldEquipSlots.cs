using Mirage.Shared.Extensibility;

namespace Mirage.Editor.Services;

/// <summary>
/// Where a character may wear something in the world being edited.
///
/// <para><b>Told, not compiled in</b>, for the same reason as <see cref="WorldFamilies"/>: the slots
/// belong to the game the server loaded, and a slot this list does not name is one no item can be
/// assigned to. A connected editor takes them from the login response; a folder on disk carries them in
/// its manifest, which is what lets an author open a world without the game that wrote it.</para>
///
/// <para>Empty is the ordinary answer for Core on its own, and an item editor showing no slots to choose
/// from is the honest way to say that nothing in this world is worn.</para>
/// </summary>
public static class WorldEquipSlots
{
    private static EquipSlotSet _slots = EquipSlotSet.Empty;

    /// <summary>Raised after <see cref="Adopt"/> or <see cref="Reset"/> changes the list.</summary>
    public static event Action? Changed;

    public static EquipSlotSet Current => _slots;

    public static IReadOnlyList<EquipSlot> All => _slots.Slots;

    /// <summary>Takes the slots a server reported or a manifest recorded. A null list is ignored; an
    /// EMPTY one is adopted, because a game with nothing worn is a real answer rather than a silence.</summary>
    public static void Adopt(IReadOnlyList<EquipSlot>? slots)
    {
        if (slots is null) return;
        _slots = new EquipSlotSet(slots);
        Changed?.Invoke();
    }

    /// <summary>Back to none. Called on disconnect: the next thing edited may be a folder on disk.</summary>
    public static void Reset()
    {
        if (_slots.Count == 0) return;
        _slots = EquipSlotSet.Empty;
        Changed?.Invoke();
    }
}
