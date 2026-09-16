namespace Mirage.Shared.Protocol;

/// <summary>
/// The channels CORE itself speaks on. A game's own are declared by the game and travel beside these.
///
/// <para>🔴 <b>None of these is a taxonomy of a game.</b> They are the five things the engine does with
/// words on its own account: somebody spoke, the server said something happened, one person whispered to
/// another, a guild talked among itself, an administrator did. Whether a world also wants to separate
/// combat from loot from quests is a decision that world makes, by declaring channels of its own — an
/// engine that shipped a "Combat" bucket would be an engine with an opinion about whether its games have
/// fighting in them.</para>
///
/// <para><b>Core owns the AUDIENCE; a channel is only what kind of line it is.</b> Who hears something —
/// one body, everybody on a map, everybody within earshot, a named set, the whole server — is the
/// engine's to work out, proximity included. A guild whisper is not a channel because it is private; it
/// is a channel because a player wants to read guild talk apart from everything else, and it reaches
/// only the guild because of who it was sent to.</para>
/// </summary>
public enum ChatChannel : byte
{
    /// <summary>⚠ Not a channel so much as a mechanism: it bypasses every tab filter and is never drawn
    /// as a toggle, so it cannot be turned off. It carries the welcome batch, which a player has to see
    /// before they have had any chance to configure anything.</summary>
    Always = 0,

    /// <summary>Player-produced. Somebody typed it and everybody can read it.</summary>
    Global = 1,

    /// <summary>Server- and event-produced: what just happened, said by the world rather than a person.
    /// Arrivals and departures, moderation, a level gained, a door unlocked.</summary>
    System = 2,

    /// <summary>One person to one person.</summary>
    Tell = 3,

    /// <summary>A guild talking among itself, officers included. Who receives it is the audience's
    /// business; that it reads apart from open speech is this.</summary>
    Guild = 4,

    /// <summary>Administrators among themselves.</summary>
    Admin = 5,
}

/// <summary>
/// Core's channels as the ids that travel and get saved, and the rules a declared id has to obey.
///
/// <para>🔴 <b>The wire carries a string, not this enum.</b> A game declares channels of its own, and a
/// number would have to be assigned by somebody — so a line says which channel it is by name, and Core's
/// own five are names in the same namespace as everybody else's. The enum stays as the convenience Core
/// writes against; these are what it becomes.</para>
/// </summary>
public static class ChatChannels
{
    public const string Always = nameof(ChatChannel.Always);
    public const string Global = nameof(ChatChannel.Global);
    public const string System = nameof(ChatChannel.System);
    public const string Tell = nameof(ChatChannel.Tell);
    public const string Guild = nameof(ChatChannel.Guild);
    public const string Admin = nameof(ChatChannel.Admin);

    /// <summary>The five names a game may not take for its own, in the order the options panel draws
    /// them. <see cref="Always"/> is not among them — it is never a toggle — but it is reserved too.</summary>
    public static readonly string[] Core = [Global, System, Tell, Guild, Admin];

    /// <summary>The id a Core channel travels as.</summary>
    public static string Name(ChatChannel channel) => channel switch
    {
        ChatChannel.Always => Always,
        ChatChannel.Global => Global,
        ChatChannel.System => System,
        ChatChannel.Tell => Tell,
        ChatChannel.Guild => Guild,
        ChatChannel.Admin => Admin,
        _ => System,
    };

    /// <summary>Whether Core owns that name. A declaration that takes one is rejected at build time:
    /// it would otherwise answer for lines the engine sends on its own account, and a player turning
    /// off a game's feed would lose their tells with it.</summary>
    public static bool IsCore(string id) =>
        string.Equals(id, Always, StringComparison.Ordinal) || Array.IndexOf(Core, id) >= 0;
}
