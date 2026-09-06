using Mirage.Shared;

namespace Mirage.Client.Shell.Logic;

/// <summary>The speech channel plain (non-slash) chat text goes to — driven by the channel dropdown to
/// the left of the input box. Public so the pure router below and its tests share it with ChatPanel.</summary>
public enum ActiveSpeechChannel { Say, Yell, Broadcast, Admin, Guild, Officer }

/// <summary>What a resolved chat input should do. <see cref="SpeechKind"/> names the send (or the error
/// to surface); Target/Body carry the tell target + message.</summary>
public enum SpeechKind { None, Say, Yell, Broadcast, Emote, Tell, Notice, AdminChat, Guild, Officer, TellUsage, NotInGuild, NotOfficer }

public readonly record struct SpeechIntent(SpeechKind Kind, string Body = "", string Target = "")
{
    public static readonly SpeechIntent None = new(SpeechKind.None);
}

/// <summary>Pure resolver for the chat overhaul's speech routing: plain text on the active dropdown
/// channel, and the speech slash commands (+ aliases), with the same access/guild/rank gating ChatPanel
/// enforces. No ChatPanel / ClientStrings / graphics dependency, so it unit-tests cleanly — ChatPanel just
/// executes the returned <see cref="SpeechIntent"/> (calls the matching Send* or shows the error line).</summary>
public static class SpeechChannelRouter
{
    /// <summary>Plain (non-slash) text on the active dropdown channel. Admin/Guild/Officer fall back to Say
    /// when the player no longer qualifies (access lost, left the guild, demoted) so text is never dropped.</summary>
    public static SpeechIntent ForActiveChannel(ActiveSpeechChannel ch, string text,
        AdminLevel access, int guildId, GuildRank rank)
    {
        text = text.Trim();
        if (text.Length == 0) return SpeechIntent.None;
        return ch switch
        {
            ActiveSpeechChannel.Yell => new(SpeechKind.Yell, text),
            ActiveSpeechChannel.Broadcast => new(SpeechKind.Broadcast, text),
            ActiveSpeechChannel.Admin => access > AdminLevel.Player ? new(SpeechKind.AdminChat, text) : new(SpeechKind.Say, text),
            ActiveSpeechChannel.Guild => guildId > 0 ? new(SpeechKind.Guild, text) : new(SpeechKind.Say, text),
            ActiveSpeechChannel.Officer => rank >= GuildRank.Officer ? new(SpeechKind.Officer, text) : new(SpeechKind.Say, text),
            _ => new(SpeechKind.Say, text),
        };
    }

    /// <summary>Resolve a speech slash command word + its argument, or <c>null</c> when <paramref name="cmd"/>
    /// isn't a speech command (the caller then handles it as a utility/admin command or reports it unknown).
    /// A non-admin's /notice or /admin also returns null so the command stays hidden from them.</summary>
    public static SpeechIntent? ForCommand(string cmd, string arg,
        AdminLevel access, int guildId, GuildRank rank)
    {
        switch (cmd)
        {
            case "say": case "s": return Text(SpeechKind.Say, arg);
            case "yell": case "y": return Text(SpeechKind.Yell, arg);
            case "broadcast": case "b": case "bc": return Text(SpeechKind.Broadcast, arg);
            case "emote": case "me": return Text(SpeechKind.Emote, arg);
            case "tell": case "t": case "w": case "whisper": case "msg": return Tell(arg);
            case "notice":
            case "n":
                return access > AdminLevel.Player ? Text(SpeechKind.Notice, arg) : null;
            case "admin":
            case "a":
                return access > AdminLevel.Player ? Text(SpeechKind.AdminChat, arg) : null;
            case "g":
            case "guild":
                if (string.IsNullOrWhiteSpace(arg)) return SpeechIntent.None;
                return guildId > 0 ? new(SpeechKind.Guild, arg.Trim()) : new(SpeechKind.NotInGuild);
            case "o":
            case "officer":
                if (string.IsNullOrWhiteSpace(arg)) return SpeechIntent.None;
                if (guildId <= 0) return new(SpeechKind.NotInGuild);
                return rank >= GuildRank.Officer ? new(SpeechKind.Officer, arg.Trim()) : new(SpeechKind.NotOfficer);
            default: return null;
        }
    }

    /// <summary>Maps a bare speech command (typed with no message, e.g. <c>/g</c>) to the dropdown channel
    /// it should park the input on, or <c>null</c> when the command isn't one of the six dropdown channels
    /// or the player doesn't qualify for it. Mirrors the dropdown's access/guild/rank gating so <c>/a</c>,
    /// <c>/g</c>, <c>/o</c> only switch when Admin/Guild/Officer would actually appear in the list. Every
    /// command alias (<c>/y</c> for <c>/yell</c>, <c>/bc</c> for <c>/broadcast</c>, ...) resolves here too.</summary>
    public static ActiveSpeechChannel? ChannelSwitchForCommand(string cmd,
        AdminLevel access, int guildId, GuildRank rank)
        => cmd switch
        {
            "say" or "s" => ActiveSpeechChannel.Say,
            "yell" or "y" => ActiveSpeechChannel.Yell,
            "broadcast" or "b" or "bc" => ActiveSpeechChannel.Broadcast,
            "admin" or "a" => access > AdminLevel.Player ? ActiveSpeechChannel.Admin : (ActiveSpeechChannel?)null,
            "g" or "guild" => guildId > 0 ? ActiveSpeechChannel.Guild : (ActiveSpeechChannel?)null,
            "o" or "officer" => rank >= GuildRank.Officer ? ActiveSpeechChannel.Officer : (ActiveSpeechChannel?)null,
            _ => null,
        };

    static SpeechIntent Text(SpeechKind kind, string body)
        => string.IsNullOrWhiteSpace(body) ? SpeechIntent.None : new(kind, body.Trim());

    static SpeechIntent Tell(string arg)
    {
        arg = arg.Trim();
        int sp = arg.IndexOf(' ');
        if (sp > 0 && sp < arg.Length - 1)
            return new(SpeechKind.Tell, arg[(sp + 1)..].Trim(), arg[..sp]);
        return new(SpeechKind.TellUsage);
    }
}
