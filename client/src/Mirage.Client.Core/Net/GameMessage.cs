using Mirage.Shared.Protocol;
using System.Text.Json.Serialization;

namespace Mirage.Client.Core.Net;

/// <summary>
/// C→S: one of the loaded game's own messages, composed from a panel the game declared.
///
/// <para>🔴 <b>The command is not known when this is compiled, which is the whole point.</b> Every
/// other packet returns a constant from <see cref="PacketNames"/>; this one carries whichever model a
/// world's rules named, and the server reads it through the parse delegate that world registered. A
/// client sends a message for a game it has never heard of.</para>
///
/// <para>The field values sit at the top level beside <c>cmd</c>, because that is where the server's
/// reader looks for them — a game's model names its own fields and nothing wraps them. Anything the
/// model did not name is dropped on arrival, so a value put here that the rules never declared cannot
/// reach past them.</para>
///
/// <para>⚠ <b>It lives here rather than in <c>Mirage.Shared</c> on purpose.</b> That assembly checks
/// at startup that every <see cref="IPacket"/> in it has a row in the registry, and a packet whose
/// command is decided at run time has no row to find. It is not part of Core's wire vocabulary; it is
/// how a client speaks a vocabulary the server told it about.</para>
/// </summary>
public sealed record GameMessage : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd { get; init; } = string.Empty;

    /// <summary>The model's fields, by their own names, written beside <c>cmd</c> rather than under a
    /// property of their own.</summary>
    [JsonExtensionData] public Dictionary<string, object> Values { get; init; } = [];
}
