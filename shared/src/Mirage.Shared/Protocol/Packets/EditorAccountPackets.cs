using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// The editor's account family — CREATOR only, and the only editor packets that describe a PERSON rather
// than a piece of content.
//
// Three things are absent on purpose and must stay absent:
//
//   The PASSWORD. It is never read into these shapes, never sent, and never round-tripped on save. The
//   editor cannot show what it never receives, and a save that carried one back could overwrite a
//   credential with whatever the form happened to hold.
//
//   The MODERATION timers and the ban list. Those are an operator's job, done from the server window; an
//   account manager that could also punish would put the same decision in two places with two audit
//   trails. See the moderation work for where they live.
//
//   GUILD MEMBERSHIP is sent but never accepted back. GuildRecord.Members is a roster cache kept in step
//   at every mutation, so writing Account.Guild directly would desync the guild from its own roster.
//   Moving somebody between guilds has to go through GuildSystem.

/// <summary>C→S: one page of the account browser. Search matches the login.</summary>
public sealed record EditorRequestAccountsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAccounts;
    [JsonPropertyName("search")] public string Search { get; init; } = "";
    /// <summary>Keep only this access level; null for every level. Costs the server a full scan, because
    /// the level is inside each record rather than in its file name.</summary>
    [JsonPropertyName("access")] public AdminLevel? Access { get; init; }
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; } = 25;
}

/// <summary>S→C: the requested page, plus the total that matched so the browser can size its pager.</summary>
public sealed record EditorAccountListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAccountList;
    [JsonPropertyName("accounts")] public List<EditorAccountRow> Accounts { get; init; } = new();
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
}

/// <summary>One row of the browser. <see cref="IsOnline"/> is live game state, so it is only as fresh as
/// the last request — the browser re-asks while it is open.</summary>
public sealed record EditorAccountRow
{
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("online")] public bool IsOnline { get; init; }
    /// <summary>The character they are playing right now, when they are. Empty otherwise.</summary>
    [JsonPropertyName("playingAs")] public string PlayingAs { get; init; } = "";
    [JsonPropertyName("chars")] public List<string> CharNames { get; init; } = new();
}

/// <summary>C→S: the full record for one account.</summary>
public sealed record EditorRequestAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>S→C: one account's editable state, plus the read-only facts worth seeing beside it.</summary>
public sealed record EditorAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("online")] public bool IsOnline { get; init; }
    /// <summary>Read-only. Shown so a Creator can see it; changing it is GuildSystem's job.</summary>
    [JsonPropertyName("guild")] public int Guild { get; init; }
    [JsonPropertyName("guildRank")] public GuildRank GuildRank { get; init; }
    [JsonPropertyName("chars")] public List<EditorCharRow> Chars { get; init; } = new();
    /// <summary>The account vault, occupied slots only. Account-shared rather than per character, so it sits
    /// beside the access and guild lines rather than on a character card.</summary>
    [JsonPropertyName("bank")] public List<EditorInvSlot> Bank { get; init; } = new();
}

/// <summary>One character slot. Slot is 1-based and identifies the row on save; an empty
/// <see cref="Name"/> means the slot is unused and nothing else on the row means anything.</summary>
public sealed record EditorCharRow
{
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("class")] public int Class { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    /// <summary>long, matching <c>PlayerRecord.Exp</c> — an int would silently clip a high-level total.</summary>
    [JsonPropertyName("exp")] public long Exp { get; init; }
    [JsonPropertyName("map")] public int Map { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("points")] public int Points { get; init; }
    /// <summary>The character's bag, occupied slots only. <b>Sent S→C and never read back</b> — the save
    /// copies named fields onto the record, and a bag arriving on a form filled minutes ago would carry a
    /// stale copy of everything a live player has picked up since. Adding and removing go through
    /// <see cref="EditorGiveItemPacket"/> and <see cref="EditorTakeItemPacket"/>, which name one slot each.</summary>
    [JsonPropertyName("inv")] public List<EditorInvSlot> Inv { get; init; } = new();
    /// <summary>The character's spell book, occupied slots only. Sent S→C and never read back, for the same
    /// reason as <see cref="Inv"/>.</summary>
    [JsonPropertyName("spells")] public List<EditorSpellSlot> Spells { get; init; } = new();
    /// <summary>The character's quest log. Sent S→C and never read back, for the same reason as
    /// <see cref="Inv"/>.</summary>
    [JsonPropertyName("quests")] public List<EditorQuestRow> Quests { get; init; } = new();
}

/// <summary>One row of a character's quest log.</summary>
public sealed record EditorQuestRow
{
    [JsonPropertyName("num")] public int QuestNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("status")] public QuestStatus Status { get; init; }
    /// <summary>Objective progress as "2/5, 0/3", already read against the quest's own objective counts.
    /// Empty for a quest with nothing to track.</summary>
    [JsonPropertyName("progress")] public string Progress { get; init; } = "";
    /// <summary>Whether the character meets what the quest asks of them. False rows are shown, because
    /// seeing a quest somebody should not be holding is the point of showing the log at all.</summary>
    [JsonPropertyName("eligible")] public bool Eligible { get; init; }
}

/// <summary>One occupied spell-book slot.</summary>
public sealed record EditorSpellSlot
{
    /// <summary>1-based slot in the book.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>One occupied bag slot, as the account browser shows it.</summary>
public sealed record EditorInvSlot
{
    /// <summary>1-based inventory slot.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("dur")] public int Dur { get; init; }
    /// <summary>Whether the character is wearing this slot — taking it strips the piece.</summary>
    [JsonPropertyName("worn")] public bool Worn { get; init; }
}

/// <summary>
/// C→S: apply an edit. Only what a Creator may change is here — access, and the per-character fields
/// above. Everything else on the account is left exactly as it was on disk.
/// </summary>
public sealed record EditorSaveAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("chars")] public List<EditorCharRow> Chars { get; init; } = new();
}

/// <summary>
/// C→S: rename one character. Its own packet rather than a field on the save, because a rename is not a
/// field edit: it has to clear the name registry that stops two players sharing a name, and it can be
/// refused for reasons the save has none of.
/// <para>Every character edit that can be refused works this way — one small packet naming exactly what it
/// touches. A whole-record save from a form filled minutes ago would carry stale copies of everything it did
/// NOT mean to change, and overwrite a live player's own doing.</para>
/// </summary>
public sealed record EditorRenameCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRenameChar;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>C→S: put an item in a character's bag. <see cref="Quantity"/> is the stack size for a currency
/// item and is ignored for anything else, which is one indivisible piece.</summary>
public sealed record EditorGiveItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorGiveItem;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: take a stack out of one of a character's bag slots. <see cref="Quantity"/> 0, or more than
/// the pile holds, means all of it — the same convention the drop and deposit paths use. A worn piece comes
/// off with it.</summary>
public sealed record EditorTakeItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorTakeItem;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    /// <summary>1-based inventory slot within that character's bag.</summary>
    [JsonPropertyName("invSlot")] public int InvSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: teach a character a spell. The class, level and INT gates a scroll enforces are NOT applied
/// — an operator granting a spell means it, and casting re-checks INT live anyway, so a spell handed out
/// early is one the character grows into rather than one that breaks anything.</summary>
public sealed record EditorLearnSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLearnSpell;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
}

/// <summary>C→S: take a spell back out of a character's book, by the book slot holding it.</summary>
public sealed record EditorForgetSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorForgetSpell;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    /// <summary>1-based slot within that character's spell book.</summary>
    [JsonPropertyName("spellSlot")] public int SpellSlot { get; init; }
}

/// <summary>C→S: put an item in the account's vault. <b>No character slot</b> — the bank is account-shared,
/// so every character on the account is looking at the same one.</summary>
public sealed record EditorBankGivePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorBankGive;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: take a stack out of one of the account vault's slots. <see cref="Quantity"/> 0, or more than
/// the pile holds, means all of it.</summary>
public sealed record EditorBankTakePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorBankTake;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based vault slot.</summary>
    [JsonPropertyName("bankSlot")] public int BankSlot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: put one quest of a character's log into a given state. A status of
/// <see cref="QuestStatus.NotStarted"/> takes the quest out of the log entirely, which is what that state
/// means — "never accepted, no entry".
/// <para>Refused unless the character meets what the quest asks (level, stats, class, prerequisite), the same
/// gate accepting one goes through: the editor should not be able to put a quest somewhere the game
/// would not.</para></summary>
public sealed record EditorSetQuestStatusPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSetQuestStatus;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    /// <summary>1-based character slot on the account.</summary>
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
    [JsonPropertyName("status")] public QuestStatus Status { get; init; }
}

/// <summary>S→C: what came of an editor operation that can be refused. The message is resolved in the
/// SERVER's language, the same as <see cref="EditorLoginResponsePacket"/> — the rules being reported are the
/// game's, and the editor has no vocabulary for them.</summary>
public sealed record EditorNoticePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorNotice;
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}
