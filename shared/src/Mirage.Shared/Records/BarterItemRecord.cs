namespace Mirage.Shared.Records;

/// <summary>One row of a shop's trade list, read from the PLAYER's side: the player gives
/// <see cref="GiveItem"/> x <see cref="GiveQuantity"/> and receives <see cref="GetItem"/> x
/// <see cref="GetQuantity"/>.
///
/// <para>So an ordinary PURCHASE is a row whose GIVE side is the currency item — gold in, goods out — and
/// a BUY-BACK is the same row inverted, goods in and gold out. There is no separate sell path in the
/// engine: <c>ShopSystem</c> exposes only <c>Trade</c> and <c>FixItem</c>, so a shop can buy an item back
/// only if someone authored a row for that exact item.</para>
///
/// <para>This was documented backwards (as the shop's side, with GetValue called "the price") until
/// 2026-08-13. Both the server and the client read it the way described above — <c>ShopSystem.Trade</c>
/// does <c>TakeItem(GiveItem)</c> then <c>GiveItem(GetItem)</c>, and the shop panel lists each row as
/// "give → get" — so the code was always consistent with itself and only the comment was wrong.
/// THE PRICE IS <see cref="GiveQuantity"/>.</para></summary>
public sealed class BarterItemRecord
{
    /// <summary>Item slot the player hands over — the currency item on a normal purchase.</summary>
    public int GiveItem { get; set; }
    /// <summary>Quantity of <see cref="GiveItem"/> required — THE PRICE.</summary>
    public int GiveQuantity { get; set; }
    /// <summary>Item slot the player receives — the goods on a normal purchase.</summary>
    public int GetItem { get; set; }
    /// <summary>Quantity of <see cref="GetItem"/> handed over.</summary>
    public int GetQuantity { get; set; }
}
