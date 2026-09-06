using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>Everything the player types as speech: say, emote, yell, broadcast, notices, admin
/// messages, whispers and rolls, plus the runtime locale switch.</summary>
public sealed partial class PacketHandler
{
    //  Chat handlers
    // ===========================================================================

    /// <summary>
    /// Post-login runtime locale change. Pre-session clients send their locale on the actual
    /// pre-session packet (Login/NewAccount/etc.) and don't need this; the <see cref="ServerPlayer.Login"/>
    /// guard rejects stray sends from unauthenticated connections.
    /// </summary>
    private void HandleSetLanguage(int index, SetLanguagePacket p)
    {
        var sp = _pm[index];
        if (sp.Login.Length == 0) return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;
    }

    private void HandleSayMsg(int index, SayMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string say = p.Msg.Trim();
        if (say.Length == 0) return;
        if (!TextValidation.IsValidText(say))
        {
            HackingAttempt(index, "Say Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(say) {sp.Name}: {say}", "Say"), "AddLog/Say");
        _dispatcher.SendLocalizedChatToViewport(index, ServerStrings.PacketHandler_Say,
            new ChatMetadata(GameColor.Say, ChatChannel.Say, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", say));
        _dispatcher.SendChatBubble(index, PacketBuilder.ChatBubble(index, say, kind: 0), sp.Login, wholeRegion: false);
    }

    private void HandleEmoteMsg(int index, EmoteMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Emote Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(emote) {sp.Name} {p.Msg}", "Emote"), "AddLog/Emote");
        // Route through the per-recipient localized path (not a raw SendToViewport) so an emote respects
        // the recipient's ignore list via SpeakerLogin, like say/yell do.
        _dispatcher.SendLocalizedChatToViewport(index, ServerStrings.PacketHandler_Emote,
            new ChatMetadata(GameColor.Emote, ChatChannel.Say, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandleYellMsg(int index, YellMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string yell = p.Msg.Trim();
        if (yell.Length == 0) return;
        if (!TextValidation.IsValidText(yell))
        {
            HackingAttempt(index, "Yell Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        int mapNum = _pm[index].Char.Map;
        _bg.Run(_persistence.AddLogAsync($"(yell) {sp.Name}: {yell}", "Yell"), "AddLog/Yell");
        // Heard across the whole observable region (the speaker's cell and its neighbors).
        ChatToMap(mapNum, ServerStrings.PacketHandler_Yell,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Yell, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", yell));
        _dispatcher.SendChatBubble(index, PacketBuilder.ChatBubble(index, yell, kind: 1), sp.Login, wholeRegion: true);
    }

    /// <summary>Viewport-scoped key-based system chat: heard only within the speaker's earshot.
    /// Used by roll and self-mumble. Each recipient resolves the line in their own session locale
    /// at the dispatcher loop. Channel is required because the two callers classify differently.</summary>
    private void ViewportMsg(int speakerIndex, string key, int color, ChatChannel channel,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToViewport(speakerIndex, key, new ChatMetadata(color, channel), args);

    private void HandleBroadcastMsg(int index, BroadcastMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Broadcast Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        string raw = p.Msg.Trim();
        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(broadcast) {sp.Name}: {raw}", "Broadcast"), "AddLog/Broadcast");
        _dispatcher.SendLocalizedChatToAll(ServerStrings.PacketHandler_Broadcast,
            new ChatMetadata(GameColor.Pink, ChatChannel.Broadcast, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", raw));
        // Broadcast bubble goes to every connected player. Render is viewport-gated client-side, so
        // latent observers see the bubble only if they enter the speaker's region during its lifetime.
        if (raw.Length > 0)
            _dispatcher.SendToAll(PacketBuilder.ChatBubble(index, raw, kind: 2));
    }

    private void HandleRoll(int index, RollPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (IsMutedAndNotify(index)) return;
        int max = p.Max < 2 ? 2 : p.Max;
        int result = _rng.Next(max) + 1;
        string name = _pm[index].Char.Name.Trim();
        if (max == 2)
        {
            ViewportMsg(index, ServerStrings.PacketHandler_RollCoin, GameColor.Roll, ChatChannel.Say,
                ("Name", name), ("Result", result == 1 ? "Heads" : "Tails"));
        }
        else
        {
            ViewportMsg(index, ServerStrings.PacketHandler_RollDice, GameColor.Roll, ChatChannel.Say,
                ("Name", name), ("Result", result), ("Max", max));
        }
    }

    private void HandleNoticeMsg(int index, NoticeMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Notice Text Modification");
            return;
        }
        if (_pm[index].Char.Access <= AdminLevel.Player) return;
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(notice) {sp.Name}: {p.Msg}", "Notice"), "AddLog/Notice");
        // Admin-to-all broadcast: classified as a System Notice (admin announcement), not a Chat channel.
        _dispatcher.SendLocalizedChatToAll(ServerStrings.PacketHandler_Notice,
            new ChatMetadata(GameColor.Notice, ChatChannel.Notice, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandleAdminMsg(int index, AdminMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Admin Text Modification");
            return;
        }
        if (_pm[index].Char.Access <= AdminLevel.Player) return;
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(admin) {sp.Name}: {p.Msg}", "Admin"), "AddLog/Admin");
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.PacketHandler_Admin,
            new ChatMetadata(GameColor.AdminChat, ChatChannel.AdminChat, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandlePlayerMsg(int index, PlayerMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Player Msg Text Modification");
            return;
        }

        int target = _pm.FindPlayerByName(p.Target);

        if (target == index)
        {
            if (IsMutedAndNotify(index)) return;
            string mumbleName = _pm[index].Char.Name.Trim();
            int mumbleMap = _pm[index].Char.Map;
            _bg.Run(_persistence.AddLogAsync($"Map #{mumbleMap}: {mumbleName} begins to mumble to himself.", "Tell"), "AddLog/Tell-mumble");
            ViewportMsg(index, ServerStrings.PacketHandler_SelfMumble, GameColor.Green, ChatChannel.Tell, ("Name", mumbleName));
            return;
        }

        if (target > 0)
        {
            if (IsMutedAndNotify(index)) return;
            var sp = SpeakerOf(index);
            var tp = SpeakerOf(target);
            _bg.Run(_persistence.AddLogAsync($"{sp.Name} tells {tp.Name}, '{p.Msg}'", "Tell"), "AddLog/Tell");
            _dispatcher.SendLocalizedChatTo(target, ServerStrings.PacketHandler_TellFrom,
                new ChatMetadata(GameColor.Tell, ChatChannel.Tell, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
                ("From", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_TellTo,
                new ChatMetadata(GameColor.Tell, ChatChannel.Tell, tp.Name, tp.Access, tp.ShowAsPk),
                ("To", AccessName(tp.Name, tp.Access)), ("Message", p.Msg));
        }
        else
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
        }
    }
}
