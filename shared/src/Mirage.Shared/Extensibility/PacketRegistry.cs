using Mirage.Shared.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// How a command string on the wire becomes a packet object.
///
/// <para><b>One table, read by every side.</b> The server's router, the client's dispatcher, and the
/// editor's connection all parse with the same registry, so a packet a game adds is a packet all three
/// can read. A command with no row here does not deserialize, and a line that does not deserialize is
/// dropped before any router sees it — which is why registration has to be the single place a packet
/// becomes known, rather than one of several.</para>
///
/// <para><b>Keyed by the command string, never by a number.</b> There is no ordinal to keep in step, so
/// a game adding packets cannot collide with a later Core one by arithmetic, and two builds that
/// disagree about which packets exist disagree by name.</para>
/// </summary>
public sealed class PacketRegistry
{
    /// <summary>How one command's JSON becomes a packet.</summary>
    /// <param name="json">The whole line.</param>
    /// <param name="hasIndex">Whether the line carried a top-level <c>index</c> field — the only thing
    /// separating the two directions of a command string used by both.</param>
    public delegate IPacket? Parse(string json, bool hasIndex);

    private readonly Dictionary<string, Parse> _byCommand;

    private PacketRegistry(Dictionary<string, Parse> byCommand) => _byCommand = byCommand;

    /// <summary>A registry that knows no commands. Every line fails to deserialize.</summary>
    public static readonly PacketRegistry Empty = new(new Dictionary<string, Parse>(StringComparer.Ordinal));

    /// <summary>Every command this registry can read, for a test that asserts the shape of the
    /// table.</summary>
    public IReadOnlyCollection<string> Commands => _byCommand.Keys;

    public bool Knows(string command) => _byCommand.ContainsKey(command);

    /// <summary>Turns a line into a packet, or null when the command is unknown or the line does not
    /// fit the shape that command names.</summary>
    public IPacket? Deserialize(string command, string json, bool hasIndex)
        => _byCommand.TryGetValue(command, out var parse) ? parse(json, hasIndex) : null;

    /// <summary>Collects command rows and produces the table.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, Parse> _byCommand = new(StringComparer.Ordinal);

        /// <summary>Registers how <paramref name="command"/> is read.</summary>
        /// <exception cref="ArgumentException">The command is blank, or already registered. A second
        /// row would silently shadow the first, and two subsystems claiming one command is the
        /// bug.</exception>
        public Builder Register(string command, Parse parse)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("A packet command cannot be blank.", nameof(command));
            }

            if (!_byCommand.TryAdd(command, parse))
            {
                throw new ArgumentException($"Packet command '{command}' is registered twice.", nameof(command));
            }

            return this;
        }

        /// <summary>Registers a command whose two directions share one string, told apart by whether
        /// the line carries a top-level <c>index</c>.</summary>
        public Builder Register(string command, Parse withIndex, Parse withoutIndex)
            => Register(command, (json, hasIndex) => hasIndex ? withIndex(json, hasIndex) : withoutIndex(json, hasIndex));

        [SuppressMessage("Design", "CA1024:Use properties where appropriate",
            Justification = "Builds a new table on each call rather than exposing stored state.")]
        public PacketRegistry Build() => new(new Dictionary<string, Parse>(_byCommand, StringComparer.Ordinal));
    }
}
