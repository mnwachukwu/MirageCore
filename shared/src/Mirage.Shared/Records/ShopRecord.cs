using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ShopRecord
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — the "you traded with {shop}" line TrimEnds the name.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public bool FixesItems { get; set; }
    public ShopType ShopType { get; set; }
    public bool AllowBanking { get; set; }

    // The NPC template number this shop/inn is assigned to (0 = none). Shops are not map-bound: an NPC whose
    // number matches a shop's Keeper is a vendor for it — interacting with that NPC opens
    // this shop, and the NPC shows the $ overhead glyph. Kept on the shop record (not NpcRecord) by design.
    public int Keeper { get; set; }

    // Barters this shop offers — a dense, 0-based list of real trades, as many as the author writes and with
    // no ceiling (the same rule as SalesItem). The purchase/display slot number on the wire stays 1-based (slot N =
    // BarterItem[N-1]) so the client and trade-request protocol are unchanged. Legacy shop JSON stored a
    // fixed 1-based array (a leading null at index 0 + slots 1..8); it deserializes into this list and is
    // compacted on load (drop null/empty) — see JsonPersistenceService and ShopRowViewModel.
    public List<BarterItemRecord> BarterItem { get; set; } = new();

    /// <summary>Item numbers this shop SELLS for gold, priced from <see cref="ItemRecord.Price"/>.
    ///
    /// <para>A plain list of numbers rather than <see cref="BarterItemRecord"/> rows, because once price
    /// lives on the item a sales row carries no other information. That is the whole reason for the split:
    /// an ordinary storefront becomes "these items", authorable by picking them and renderable as a normal
    /// item list — where the barter table renders as a wall of "give X → get Y" strings and needed a row
    /// hand-written per item. 471 items could never have been a trade table.</para>
    ///
    /// <para><see cref="BarterItem"/> keeps everything it always did and is NOT superseded. Barter has cases
    /// a single global price cannot express: "five Witch's Hair for a Ruby Pendant", and treasure, where two
    /// different vendors may deliberately pay differently for the same thing. The accepted trade-off of
    /// pricing from the item is that similar goods cost the same at every vendor; the trade table is the
    /// escape hatch for when that matters.</para>
    ///
    /// <para>Deduplicated and stripped of dead numbers on load (see <see cref="Normalize"/>), so a stocking
    /// pass that lists an item twice is harmless rather than a doubled row in the player's face.</para></summary>
    public List<int> SalesItem { get; set; } = new();

    /// <summary>Canonicalize the sales list: drop anything that is not a live item number, and collapse
    /// duplicates while keeping the authored order — the order IS the display order, so re-sorting would
    /// quietly rearrange a shopfront someone arranged deliberately.</summary>
    public void Normalize(int maxItems)
    {
        if (SalesItem.Count == 0) return;
        var seen = new HashSet<int>();
        var kept = new List<int>(SalesItem.Count);
        foreach (int num in SalesItem)
            if (num > 0 && num <= maxItems && seen.Add(num)) kept.Add(num);
        SalesItem = kept;
    }
}
