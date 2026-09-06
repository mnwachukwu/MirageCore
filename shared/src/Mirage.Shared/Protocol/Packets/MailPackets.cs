using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the account's full mailbox — inbox + sent outbox — plus the server's current UTC-seconds
/// so the client can render "in transit" (DeliverAt > NowUtc) without its own clock. Sent on entering the
/// world and after any change (a maturity sweep re-pushes when an in-transit message matures).</summary>
public sealed record MailboxPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Mailbox;
    [JsonPropertyName("mail")] public List<MailMessage> Mail { get; init; } = new();
    [JsonPropertyName("outbox")] public List<MailMessage> Outbox { get; init; } = new();
    [JsonPropertyName("now")] public long NowUtc { get; init; }
}

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: mark a mail message read.</summary>
public sealed record MailMarkReadPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MailMarkRead;
    [JsonPropertyName("id")] public int Id { get; init; }
}

/// <summary>C→S: delete a mail message from the inbox, or from the sender's OUTBOX when
/// <see cref="Outbox"/> is set — that removes only the sender's own copy; the recipient's inbox copy is
/// independent and untouched.</summary>
public sealed record MailDeletePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MailDelete;
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("outbox")] public bool Outbox { get; init; }
}

/// <summary>C→S: collect a mail message's attachments into the recipient's inventory.</summary>
public sealed record MailClaimPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MailClaim;
    [JsonPropertyName("id")] public int Id { get; init; }
}

/// <summary>C→S: pay a Collect-on-Delivery mail's price to unlock its attachments. The server charges the
/// receiver's gold, releases the items into their inventory, and mails the tax-adjusted net to the sender.</summary>
public sealed record MailPayCodPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MailPayCod;
    [JsonPropertyName("id")] public int Id { get; init; }
}

/// <summary>C→S: compose and send a player-to-player mail to an account (addressed by account name).
/// Attachments reference the sender's inventory slots; the server escrows them at send and refunds them
/// if the recipient account does not exist.</summary>
public sealed record MailSendPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MailSend;
    [JsonPropertyName("to")] public string Recipient { get; init; } = "";
    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("body")] public string Body { get; init; } = "";
    [JsonPropertyName("attach")] public List<MailSendAttach> Attach { get; init; } = new();
    [JsonPropertyName("cod")] public int CodPrice { get; init; }   // >0 = Collect-on-Delivery: recipient pays this to unlock
}

/// <summary>One staged attachment in a compose: an inventory slot + quantity. Quantity applies only to a
/// currency slot (a partial take); a non-currency slot is escrowed whole.</summary>
public sealed record MailSendAttach
{
    [JsonPropertyName("slot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}
