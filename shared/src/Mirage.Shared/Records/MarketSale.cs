namespace Mirage.Shared.Records;

/// <summary>One completed marketplace sale — the seller's history row and the on-disk audit record. Recorded
/// at purchase (including each partial currency buy). <see cref="Price"/> is the gross the buyer paid;
/// <see cref="Tax"/> is the sale tax withheld, so the seller's net is <c>Price - Tax</c>.</summary>
public sealed class MarketSale
{
    /// <summary>Stable global sale id (1-based, monotonic).</summary>
    public int Id { get; set; }
    public string Seller { get; set; } = "";
    public string Buyer { get; set; } = "";
    /// <summary>What sold: 1-based item number and the quantity/currency amount in this sale.</summary>
    public int ItemNum { get; set; }
    public int Quantity { get; set; }
    /// <summary>Gross gold the buyer paid for this (possibly partial) sale.</summary>
    public int Price { get; set; }
    /// <summary>Sale tax withheld from the seller's payout (a gold sink).</summary>
    public int Tax { get; set; }
    /// <summary>UTC-seconds the sale completed.</summary>
    public long TimeUtc { get; set; }

    public MarketSale Clone() => (MarketSale)MemberwiseClone();
}
