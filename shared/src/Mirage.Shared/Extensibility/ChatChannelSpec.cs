using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// A channel a GAME declares: a kind of line its own rules produce, that a player can read apart from
/// everything else.
///
/// <para>🔴 <b>The taxonomy belongs to the world, not the engine.</b> Core speaks on five channels of its
/// own — somebody talked, the world said something happened, a whisper, a guild, an administrator — and
/// every one of those is a thing the engine does itself. Whether a world separates the blow-by-blow of a
/// fight from what it paid out, or its quests from its weather, is that world's decision, and a channel
/// list compiled into the engine would be that decision made for it.</para>
///
/// <para><b>Declare none and a world still works.</b> Every line a game sends lands on Core's System
/// channel, which is the honest answer for a world that has not said its events are different from one
/// another.</para>
/// </summary>
public sealed record ChatChannelSpec
{
    /// <summary>What a script names when it sends on this channel, and what the tab filters remember.
    ///
    /// <para>⚠ Persisted in a player's own chat settings, so renaming one loses whatever they had chosen
    /// about it — the same care a saved attribute key wants.</para></summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for the name a player sees against the toggle, and in the prefix on a
    /// line where one is drawn.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>The color its lines are drawn in, packed 0xRRGGBB.</summary>
    [JsonPropertyName("rgb")] public int Rgb { get; init; } = GameColor.White;

    /// <summary>Localization key for a tab of its own, carrying this channel instead of the main one.
    /// Blank for a channel that belongs in the main tab.
    ///
    /// <para>⚠ The one to think about for a noisy channel. A blow-by-blow feed in the main tab buries
    /// everything else in the tab a new player is looking at; the same feed on a tab of its own is there
    /// when they look for it. Either way a player can move any channel to any tab afterward — this is
    /// only where it starts.</para>
    ///
    /// <para>Channels naming the SAME key share one tab, which is how a world puts its blow-by-blow and
    /// its payouts together on a "Combat" tab and leaves the main tab for talk.</para></summary>
    [JsonPropertyName("ownTabKey")] public string OwnTabKey { get; init; } = string.Empty;
}

/// <summary>Every chat channel a game declared, in declaration order.</summary>
public sealed class ChatChannelSet
{
    /// <summary>A game that declares none. Its lines land on Core's own System channel.</summary>
    public static readonly ChatChannelSet Empty = new([]);

    private readonly ChatChannelSpec[] _channels;

    public ChatChannelSet(IReadOnlyList<ChatChannelSpec> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _channels = [.. channels];
    }

    public IReadOnlyList<ChatChannelSpec> Channels => _channels;

    public int Count => _channels.Length;

    /// <summary>The channel with that id, or null for one nobody declared.</summary>
    public ChatChannelSpec? Find(string id) =>
        Array.Find(_channels, c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>The distinct <see cref="ChatChannelSpec.OwnTabKey"/> values, in declaration order. One
    /// tab is made per entry for a fresh account.</summary>
    public IReadOnlyList<string> OwnTabKeys
    {
        get
        {
            var keys = new List<string>();
            foreach (var c in _channels)
            {
                if (c.OwnTabKey.Length > 0 && !keys.Contains(c.OwnTabKey, StringComparer.Ordinal))
                    keys.Add(c.OwnTabKey);
            }
            return keys;
        }
    }
}
