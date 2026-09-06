namespace Mirage.Shared.Records;

/// <summary>Persisted form of one item lying on a map: the fields of a
/// <see cref="MapItemRecord"/> that must survive a restart. Runtime-only state (the loot tag and its
/// expiry, the allocated slot) is deliberately absent — a tag is meaningless once the server stops.</summary>
/// <param name="Num">Item slot number.</param>
/// <param name="Value">Stack quantity, for a currency-type item.</param>
/// <param name="Dur">Current durability.</param>
/// <param name="X">Map-local tile column it rests on.</param>
/// <param name="Y">Map-local tile row it rests on.</param>
/// <param name="Source">What put it there, which decides whether it respawns.</param>
/// <param name="DropSeq">Monotonic drop order; the highest at a tile is the top of the stack.</param>
public record DroppedItemSaveData(int Num, int Value, int Dur, int X, int Y, ItemSource Source = ItemSource.PlayerDropped, long DropSeq = 0);
