namespace Mirage.Shared.Records;

/// <summary>One inventory slot. An empty slot is <see cref="Num"/> == 0 rather than a null entry, so the
/// bag is a fixed-length array and slot numbers stay stable across a drop or a sale.</summary>
public sealed class PlayerInvSlot
{
    /// <summary>Item slot number; 0 = the inventory slot is empty.</summary>
    public int Num { get; set; }
    /// <summary>Stack quantity, for a currency-type item.</summary>
    public int Quantity { get; set; }
    /// <summary>Current durability of this copy; equipment wears per-instance, not per-item-type.</summary>
    public int Dur { get; set; }

    /// <summary>Adds to the stack without wrapping.
    ///
    /// <para>🔴 Gold is a plain <c>int</c> with no ceiling of its own, so a large pile plus a large gift can
    /// exceed what one holds — and a wrapped sum turns a fortune into a debt, silently. Every path that grows
    /// a stack goes through here: a gift from the account editor, loot, a bank withdrawal, a pickup.</para></summary>
    public void AddQuantity(int amount) =>
        Quantity = (int)Math.Clamp((long)Quantity + amount, 0L, int.MaxValue);
}
