using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>C→S: player requests to open the bank.</summary>
public sealed record BankOpenPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankOpen;
}

/// <summary>C→S: deposit an inventory slot into the bank.
/// Quantity = 0 means deposit the whole stack (non-currency items always use 0).</summary>
public sealed record BankDepositPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankDeposit;
    [JsonPropertyName("invSlot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: withdraw a bank slot into the inventory.
/// Quantity = 0 means withdraw the whole stack (non-currency items always use 0).</summary>
public sealed record BankWithdrawPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankWithdraw;
    [JsonPropertyName("bankSlot")] public int BankSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: bulk deposit, keyed by item id rather than a specific inventory slot.
/// Server scans inventory for matching non-currency slots (skipping equipped), clamps to bank
/// capacity, and moves up to <c>Quantity</c> items. Quantity = 0 means "as many as fit".
/// Currency uses the per-slot <see cref="BankDepositPacket"/> instead.</summary>
public sealed record BankDepositBulkPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankDepositBulk;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: bulk withdraw, keyed by item id rather than a specific bank slot. Server scans
/// bank for matching non-currency slots, clamps to inventory capacity, and moves up to
/// <c>Quantity</c> items. Quantity = 0 means "as many as fit". Currency uses
/// <see cref="BankWithdrawPacket"/>.</summary>
public sealed record BankWithdrawBulkPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankWithdrawBulk;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: tidy the account bank into the canonical sort order (see
/// <c>BankSystem.SortBank</c>). No payload — the server reorders the caller's bank and resyncs.</summary>
public sealed record BankSortPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankSort;
}

/// <summary>S→C: full bank contents sent after a successful BankOpenPacket.</summary>
public sealed record SendBankPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendBank;
    [JsonPropertyName("slots")] public BankSlotData[] Slots { get; init; } = [];

    public sealed record BankSlotData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("dur")] int Dur
    );
}

/// <summary>S→C: single bank slot updated after a deposit or withdrawal.</summary>
public sealed record BankSlotUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BankSlotUpdate;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("dur")] public int Dur { get; init; }
}
