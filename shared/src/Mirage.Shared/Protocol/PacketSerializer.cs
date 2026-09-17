using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Mirage.Shared.Protocol;

public static class PacketSerializer
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The table every command is read through — the server's router, the client's dispatcher,
    /// and the editor's connection alike.
    ///
    /// <para>Starts as Core's own rows. A host that loads game modules builds one table from Core's
    /// rows and theirs and assigns it here, <b>once, before any connection is accepted</b>. A command
    /// with no row in it does not deserialize, and a line that does not deserialize is dropped before
    /// any router sees it — so this is the single place a packet becomes known.</para></summary>
    public static PacketRegistry Registry { get; set; } = CorePackets.Build();

    /// <summary>
    /// The header fields a dispatcher needs before it can pick a concrete packet type: the
    /// <c>cmd</c> discriminator, and whether a top-level <c>index</c> field is present.
    /// <para><see cref="HasIndex"/> exists for the shared-cmd pairs — PlayerMove/SendPlayerMove and
    /// PlayerDir/SendPlayerDir deliberately reuse one <c>cmd</c> string, and the only thing that
    /// tells them apart on the wire is that the S→C form carries an index and the C→S form does
    /// not. <see cref="Cmd"/> is null when the line is not a JSON object, carries no <c>cmd</c>, or
    /// carries one that is not a string.</para>
    /// </summary>
    public readonly record struct PacketHeader(string? Cmd, bool HasIndex);

    // UTF-8 property names to compare against, so the scan never materializes a key string.
    private static readonly byte[] CmdUtf8 = "cmd"u8.ToArray();
    private static readonly byte[] IndexUtf8 = "index"u8.ToArray();

    // Lines above this encoded size go through the array pool instead of the stack. The server
    // rate-limits non-admins to 1000 bytes/s (PacketHandler.HandlePacket), so ordinary gameplay
    // traffic stays on the stack path; only bulk editor saves and map payloads rent.
    private const int StackScanLimit = 1024;

    /// <summary>
    /// Reads <see cref="PacketHeader"/> off a JSON line without building a DOM. Allocation-free:
    /// the UTF-8 bytes go to the stack (or the array pool for large lines) and the reader stops as
    /// soon as it has both fields, so it never walks a big payload it doesn't care about.
    /// <para>Never throws — an unreadable line yields <c>default</c>, which every caller treats as
    /// "drop this packet", matching the behavior of the DOM parse this replaced.</para>
    /// </summary>
    public static PacketHeader ReadHeader(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return default;

        int maxBytes = Encoding.UTF8.GetMaxByteCount(line.Length);
        if (maxBytes <= StackScanLimit)
        {
            Span<byte> stackBuf = stackalloc byte[StackScanLimit];
            return ScanHeader(stackBuf[..Encoding.UTF8.GetBytes(line, stackBuf)]);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try { return ScanHeader(rented.AsSpan(0, Encoding.UTF8.GetBytes(line, rented))); }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    // Walks only the top level: every container value is Skip()ed the moment its start token
    // appears, so the reader never descends and a nested "cmd"/"index" key cannot be mistaken for
    // the real one. That also makes the first EndObject we see the close of the outer object.
    private static PacketHeader ScanHeader(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        string? cmd = null;
        bool hasIndex = false;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return default;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                bool isCmd = reader.ValueTextEquals(CmdUtf8);
                bool isIndex = reader.ValueTextEquals(IndexUtf8);

                if (!reader.Read()) break;
                // A non-string cmd leaves Cmd null, which the caller treats as "drop this packet".
                if (isCmd && reader.TokenType == JsonTokenType.String) cmd = reader.GetString();
                else if (isIndex && reader.TokenType != JsonTokenType.Null) hasIndex = true;

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                if (cmd is not null && hasIndex) break;
            }
        }
        catch (JsonException) { return default; }

        return new PacketHeader(cmd, hasIndex);
    }

#if DEBUG
    // Fires once on first use. Round-trips every IPacket in this assembly through Serialize and
    // TryDeserialize, so a packet with no row in the registry — or a row naming the wrong type —
    // fails at startup rather than being dropped silently at runtime.
    //
    // The identity check covers the two commands used in both directions as well. Their two shapes
    // are told apart by whether the line carries a top-level index, and the round trip reads that
    // off the line it just serialized, so each shape is checked against the row it actually resolves.
    static PacketSerializer() => ValidateAllRegistered();

    private static void ValidateAllRegistered()
    {
        var errors = typeof(IPacket).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IPacket).IsAssignableFrom(t))
            .Select(t => (IPacket?)Activator.CreateInstance(t))
            .Where(p => p is not null)
            .Select(p => p!)
            .Select(p =>
            {
                string line = Serialize(p);
                var roundTripped = TryDeserialize(line);
                if (roundTripped is null)
                {
                    return $"{p.GetType().Name} (cmd: \"{p.Cmd}\") — no row in the packet registry";
                }

                return roundTripped.GetType() != p.GetType()
                    ? $"{p.GetType().Name} (cmd: \"{p.Cmd}\") — registry resolves it to {roundTripped.GetType().Name}"
                    : null;
            })
            .Where(msg => msg is not null)
            .ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The packet registry is incomplete: {string.Join(", ", errors)}");
        }
    }
#endif

    /// <summary>Serialize a packet POCO to a JSON line (terminated with \n).</summary>
    public static string Serialize<T>(T packet) where T : IPacket =>
        JsonSerializer.Serialize(packet, packet.GetType(), Options) + "\n";

    /// <summary>
    /// Reads a JSON line as the packet its <c>cmd</c> names. Returns null when the command has no row
    /// in <see cref="Registry"/>, or when the line does not fit the shape that row names.
    /// </summary>
    public static IPacket? TryDeserialize(string line)
    {
        var header = ReadHeader(line);
        return header.Cmd is null ? null : Registry.Deserialize(header.Cmd, line, header.HasIndex);
    }

    /// <summary>
    /// Same as <see cref="TryDeserialize(string)"/> for a caller that has already read the header — it
    /// passes it back in rather than paying for a second scan.
    /// </summary>
    public static IPacket? TryDeserialize(string line, in PacketHeader header)
        => header.Cmd is null ? null : Registry.Deserialize(header.Cmd, line, header.HasIndex);

    /// <summary>
    /// Reads a line whose command is already known.
    ///
    /// <para><paramref name="hasIndex"/> resolves the two commands used in both directions —
    /// <c>playermove</c> and <c>playerdir</c> — whose shapes are told apart only by whether a
    /// top-level <c>index</c> is present. A caller that does not have it gets the form without one,
    /// which is all a server receiving from a client ever sees.</para>
    /// </summary>
    public static IPacket? TryDeserialize(string line, string cmd, bool hasIndex = false)
        => Registry.Deserialize(cmd, line, hasIndex);
}
