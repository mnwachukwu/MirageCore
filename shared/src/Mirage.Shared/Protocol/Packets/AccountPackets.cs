using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C→S: create an account. <c>Locale</c> tells the server which language to reply in, since no session exists yet.
///
/// <para>Carries the machine key for the same reason <see cref="LoginPacket"/> does, and with more at
/// stake: registering again is precisely what an account ban cannot stop, so this is the packet a machine
/// ban exists to reach.</para></summary>
public sealed record NewAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NewAccount;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
    /// <summary>See <see cref="LoginPacket.MachineKey"/>.</summary>
    [JsonPropertyName("mkey")] public string MachineKey { get; init; } = "";
}

/// <summary>C→S: delete an account and every character on it, re-authenticating first.</summary>
public sealed record DelAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DelAccount;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C→S: change an account password, authenticating with the old one.</summary>
public sealed record ChangePasswordPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ChangePassword;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("newpass")] public string NewPassword { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C→S: authenticate. Carries the client's version triple so the server can reject a mismatched build before touching the account store.</summary>
public sealed record LoginPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Login;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("maj")] public int Major { get; init; }
    [JsonPropertyName("min")] public int Minor { get; init; }
    [JsonPropertyName("rev")] public int Revision { get; init; }
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";

    /// <summary>An opaque hash identifying the machine this client runs on — see
    /// <see cref="MachineKey"/>. It rides here rather than in a packet of its own because it must be
    /// known before the login is decided, and this one is already the first thing a client sends.
    ///
    /// <para>Empty is normal and always allowed: a client that could not compute one, or an older build
    /// that does not send one, logs in as though no machine ban existed. Treating a blank as a match
    /// would group every such machine into one identity and ban them together.</para></summary>
    [JsonPropertyName("mkey")] public string MachineKey { get; init; } = "";
}

/// <summary>C→S: switch the language for this session, so later server text arrives localized.</summary>
public sealed record SetLanguagePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetLanguage;
    [JsonPropertyName("locale")] public string Locale { get; init; } = "";
}

/// <summary>C→S: create a character in the next free slot.</summary>
public sealed record AddCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AddChar;
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>Which of the world's offered appearances was chosen, as a 0-based position in the
    /// list the server sent in its hello.
    ///
    /// <para>A position rather than a sprite number: the server holds the list, so it resolves the
    /// choice itself and a client cannot ask to look like something the world never offered.</para></summary>
    [JsonPropertyName("appearance")] public int Appearance { get; init; }
}

/// <summary>C→S: delete the character in <c>Slot</c>.</summary>
public sealed record DelCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DelChar;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>
/// C→S: enter the world as the character in <c>Slot</c>. <c>Locale</c> rides along for the same
/// reason the other pre-session packets carry one: handling this packet immediately produces
/// localized text (the welcome, the MOTD, the join broadcast), so the language has to be settled
/// before the handler runs. A <see cref="SetLanguagePacket"/> cannot do that job — the client only
/// learns it is in-game from the reply to this packet, a round trip after that text is on the wire.
/// </summary>
public sealed record UseCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UseChar;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C→S: leave the world but stay logged in, returning to character select.</summary>
public sealed record LogoutToCharSelectPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LogoutToCharSelect;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: a message for the alert dialog. <c>Code</c> carries the flow-control result for auth alerts so the client branches on a stable value instead of the localized text.</summary>
public sealed record AlertMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AlertMsg;
    [JsonPropertyName("msg")] public string Message { get; init; } = "";
    // Flow-control result for auth alerts; None (the default) for ordinary alerts. Lets the client
    // branch on a stable code rather than matching the localized Message text.
    [JsonPropertyName("code")] public AlertCode Code { get; init; } = AlertCode.None;
}

/// <summary>
/// S→C: what this server is, sent the moment a connection is accepted and before anything else.
///
/// <para><b>This is the pre-login handshake.</b> A client compiles against the PROTOCOL ceilings — the
/// largest numbers the wire can carry — but a given server runs on its own, usually much smaller, limits.
/// Being told them up front is what lets a client work to the server's shape instead of the protocol's.
/// It arrives before credentials are sent, so nothing about it depends on who is connecting.</para>
///
/// <para>Carries the player limit and the game's name. The remaining record ceilings join it, and at that
/// point the client sizes its tables from what it was told rather than from anything compiled in.</para>
/// </summary>
public sealed record ServerHelloPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ServerHello;

    /// <summary>How many player slots THIS server has. Never above the protocol ceiling, because a
    /// higher slot number could not be indexed by a client that allocated for the ceiling.</summary>
    [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; init; }

    /// <summary>What this game is called. A client ships with no game identity — it wears the ENGINE's
    /// name until it is told this, and shows it from here on. Empty leaves the engine name in place.</summary>
    [JsonPropertyName("gameName")] public string GameName { get; init; } = "";

    /// <summary>How many of each record family this server has. The client sizes its tables and bounds
    /// every record number against these, rather than against anything it was compiled with — see
    /// <see cref="RecordLimits"/> for why a compiled-in ceiling is a bug rather than a default.</summary>
    [JsonPropertyName("records")] public RecordLimits Records { get; init; } = RecordLimits.Default;

    /// <summary>The appearances this world offers at character creation. The creation screen shows
    /// these and nothing else — a client never picks from the art it happens to have loaded, because
    /// which looks a game offers is the author's decision rather than a property of the atlas.</summary>
    [JsonPropertyName("appearances")]
    public IReadOnlyList<CharacterAppearance> Appearances { get; init; } = CharacterAppearance.DefaultSet;
}

/// <summary>
/// S→C: you are waiting for a slot on a full server, and this is where you are in the line.
///
/// <para>Numbers rather than a sentence, unlike <see cref="AlertMsgPacket"/>: the client has its own
/// string table and renders the line in the language the PLAYER chose, which is the one the menus are
/// already in. Pushed when the position changes, never polled.</para>
///
/// <para>The connection holding this has no player slot and no session — it is a socket in a list. There
/// is no matching C→S packet: waiting is not something a client does, it is something that happens to
/// it.</para>
/// </summary>
public sealed record QueueUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.QueueUpdate;

    /// <summary>1 = next in line. Never 0 — a promoted connection gets the ordinary login flow instead,
    /// which is the only signal the client needs that the wait is over.</summary>
    [JsonPropertyName("pos")] public int Position { get; init; }

    /// <summary>How many are waiting, so "3rd" can be shown as "3rd of 40".</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
}

/// <summary>S→C: the account's character slots, for the selection screen.</summary>
public sealed record SendCharsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendChars;
    [JsonPropertyName("chars")] public CharSlot[] Chars { get; init; } = [];

    /// <summary>One character slot, as the selection screen shows it.</summary>
    public sealed record CharSlot(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("level")] int Level,
        [property: JsonPropertyName("sprite")] int Sprite,
        [property: JsonPropertyName("spriteSheet")] int SpriteSheet = 0
    );
}
