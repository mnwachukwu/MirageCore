using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// A durable write-ahead record of a direct-trade swap in flight — the commit point that makes the swap
/// atomic across the two participants' SEPARATE account files. Written (and fsync'd) BEFORE either character
/// is saved with the swap applied, and deleted only after BOTH are durably saved. If the server dies in
/// between, boot recovery (<c>TradeSystem.RecoverJournalsAsync</c>) replays it: for each side, if that
/// character's escrow (<see cref="PlayerRecord.TradeOffer"/>) is still non-empty the swap hadn't been applied
/// for them, so it grants their <c>AReceives</c> / <c>BReceives</c> and clears their escrow; an already-applied side (escrow
/// empty) is skipped. That idempotency — escrow-empty ⇔ this side already swapped, atomic within one account
/// file — closes the cross-file tearing window (no dupe, no loss) without a per-record applied-id log.
///
/// Server-internal (never sent to a client). <c>AReceives</c> = the items player A receives (B's staged
/// offer); <c>BReceives</c> = the items player B receives (A's staged offer).
/// </summary>
public sealed class TradeJournal
{
    /// <summary>Filename stem inside <c>trades/</c>; the trailing number is the <see cref="Id"/>.</summary>
    public const string FileStem = "journal";

    /// <summary>Sequence number for this journal. In memory only — the next id is taken from the highest
    /// loaded, and a journal is quoted by id in the audit log.
    ///
    /// <para>NOT serialized. The number lives in the filename, <c>trades/journal{Id}.json</c>, and the
    /// loader fills this in from it.</para></summary>
    [JsonIgnore]
    public int Id { get; set; }

    public string ALogin { get; set; } = "";
    public int AChar { get; set; }
    public List<PlayerInvSlot> AReceives { get; set; } = new();

    public string BLogin { get; set; } = "";
    public int BChar { get; set; }
    public List<PlayerInvSlot> BReceives { get; set; } = new();
}
