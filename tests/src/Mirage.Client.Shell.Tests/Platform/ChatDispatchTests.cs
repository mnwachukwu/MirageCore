using Mirage.Client.Shell.Logic;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Platform;

/// <summary>The chat overhaul's speech routing (<see cref="SpeechChannelRouter"/>): every speech command +
/// alias maps to the right send, /tell parses target+body, plain text follows the dropdown's active channel,
/// the retired symbol prefixes (' - ! @ " =) are now just plain text on the active channel, non-speech
/// commands aren't claimed by the router, and access/guild/rank gating holds. Pure logic — no ChatPanel.</summary>
[TestFixture]
public class ChatDispatchTests
{
    const AdminLevel Admin = AdminLevel.Creator;
    const int InGuild = 5;

    // ── Speech slash commands + aliases ────────────────────────────────────────
    [TestCase("say", SpeechKind.Say)]
    [TestCase("s", SpeechKind.Say)]
    [TestCase("yell", SpeechKind.Yell)]
    [TestCase("y", SpeechKind.Yell)]
    [TestCase("broadcast", SpeechKind.Broadcast)]
    [TestCase("b", SpeechKind.Broadcast)]
    [TestCase("bc", SpeechKind.Broadcast)]
    [TestCase("emote", SpeechKind.Emote)]
    [TestCase("me", SpeechKind.Emote)]
    public void SpeechCommand_MapsToKind(string cmd, SpeechKind expected)
    {
        var intent = SpeechChannelRouter.ForCommand(cmd, "hi there", AdminLevel.Player, 0, GuildRank.None);
        Assert.That(intent, Is.Not.Null);
        Assert.That(intent!.Value.Kind, Is.EqualTo(expected));
        Assert.That(intent.Value.Body, Is.EqualTo("hi there"));
    }

    [TestCase("tell")]
    [TestCase("t")]
    [TestCase("w")]
    [TestCase("whisper")]
    [TestCase("msg")]
    public void Tell_ParsesTargetAndBody(string cmd)
    {
        var intent = SpeechChannelRouter.ForCommand(cmd, "Bob hello world", AdminLevel.Player, 0, GuildRank.None);
        Assert.That(intent!.Value.Kind, Is.EqualTo(SpeechKind.Tell));
        Assert.That(intent.Value.Target, Is.EqualTo("Bob"));
        Assert.That(intent.Value.Body, Is.EqualTo("hello world"));
    }

    [Test]
    public void Tell_NoBody_IsUsageError()
        => Assert.That(SpeechChannelRouter.ForCommand("tell", "Bob", AdminLevel.Player, 0, GuildRank.None)!.Value.Kind,
            Is.EqualTo(SpeechKind.TellUsage));

    // ── Admin gating (non-admin /notice & /admin aren't claimed, so they read as unknown) ──
    [Test]
    public void Notice_Admin_Sends()
        => Assert.That(SpeechChannelRouter.ForCommand("notice", "hi", Admin, 0, GuildRank.None)!.Value.Kind, Is.EqualTo(SpeechKind.Notice));
    [Test]
    public void Notice_Player_NotASpeechCommand()
        => Assert.That(SpeechChannelRouter.ForCommand("notice", "hi", AdminLevel.Player, 0, GuildRank.None), Is.Null);
    [Test]
    public void Admin_Admin_Sends()
        => Assert.That(SpeechChannelRouter.ForCommand("a", "hi", Admin, 0, GuildRank.None)!.Value.Kind, Is.EqualTo(SpeechKind.AdminChat));
    [Test]
    public void Admin_Player_NotASpeechCommand()
        => Assert.That(SpeechChannelRouter.ForCommand("admin", "hi", AdminLevel.Player, 0, GuildRank.None), Is.Null);

    // ── Guild / officer gating ─────────────────────────────────────────────────
    [TestCase("g")]
    [TestCase("guild")]
    public void Guild_InGuild_Sends(string cmd)
        => Assert.That(SpeechChannelRouter.ForCommand(cmd, "hi", AdminLevel.Player, InGuild, GuildRank.Member)!.Value.Kind, Is.EqualTo(SpeechKind.Guild));
    [Test]
    public void Guild_Guildless_IsError()
        => Assert.That(SpeechChannelRouter.ForCommand("g", "hi", AdminLevel.Player, 0, GuildRank.None)!.Value.Kind, Is.EqualTo(SpeechKind.NotInGuild));
    [Test]
    public void Officer_Officer_Sends()
        => Assert.That(SpeechChannelRouter.ForCommand("o", "hi", AdminLevel.Player, InGuild, GuildRank.Officer)!.Value.Kind, Is.EqualTo(SpeechKind.Officer));
    [Test]
    public void Officer_Member_IsNotOfficerError()
        => Assert.That(SpeechChannelRouter.ForCommand("o", "hi", AdminLevel.Player, InGuild, GuildRank.Member)!.Value.Kind, Is.EqualTo(SpeechKind.NotOfficer));
    [Test]
    public void Officer_Guildless_IsNotInGuildError()
        => Assert.That(SpeechChannelRouter.ForCommand("officer", "hi", AdminLevel.Player, 0, GuildRank.None)!.Value.Kind, Is.EqualTo(SpeechKind.NotInGuild));

    // ── Non-speech commands aren't claimed by the router (ChatPanel handles them) ──
    [TestCase("kick")]
    [TestCase("help")]
    [TestCase("roll")]
    [TestCase("r")]
    [TestCase("warpto")]
    public void NonSpeechCommand_ReturnsNull(string cmd)
        => Assert.That(SpeechChannelRouter.ForCommand(cmd, "x", Admin, InGuild, GuildRank.Leader), Is.Null);

    // ── Plain text follows the active dropdown channel ─────────────────────────
    [Test]
    public void PlainText_Say()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Say, "hello", AdminLevel.Player, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.Say));
    [Test]
    public void PlainText_Yell()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Yell, "hello", AdminLevel.Player, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.Yell));

    // The retired symbol prefixes are just plain text now → they go to the active channel (default Say).
    [TestCase("'hello")]
    [TestCase("-hello")]
    [TestCase("!hello")]
    [TestCase("@Bob hi")]
    [TestCase("\"hello")]
    [TestCase("=hello")]
    public void RetiredSymbols_ArePlainSay(string text)
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Say, text, AdminLevel.Player, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.Say));

    // ── Active-channel gating falls back to Say when unqualified (never silently dropped) ──
    [Test]
    public void ActiveAdmin_NonAdmin_FallsBackToSay()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Admin, "hi", AdminLevel.Player, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.Say));
    [Test]
    public void ActiveAdmin_Admin_Sends()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Admin, "hi", Admin, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.AdminChat));
    [Test]
    public void ActiveGuild_Guildless_FallsBackToSay()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Guild, "hi", AdminLevel.Player, 0, GuildRank.None).Kind, Is.EqualTo(SpeechKind.Say));
    [Test]
    public void ActiveOfficer_Member_FallsBackToSay()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Officer, "hi", AdminLevel.Player, InGuild, GuildRank.Member).Kind, Is.EqualTo(SpeechKind.Say));
    [Test]
    public void ActiveOfficer_Officer_Sends()
        => Assert.That(SpeechChannelRouter.ForActiveChannel(ActiveSpeechChannel.Officer, "hi", AdminLevel.Player, InGuild, GuildRank.Officer).Kind, Is.EqualTo(SpeechKind.Officer));

    // ── Bare `/channel` command (no message) switches the active dropdown channel; aliases + gating ──
    [TestCase("say", ActiveSpeechChannel.Say)]
    [TestCase("s", ActiveSpeechChannel.Say)]
    [TestCase("yell", ActiveSpeechChannel.Yell)]
    [TestCase("y", ActiveSpeechChannel.Yell)]
    [TestCase("broadcast", ActiveSpeechChannel.Broadcast)]
    [TestCase("b", ActiveSpeechChannel.Broadcast)]
    [TestCase("bc", ActiveSpeechChannel.Broadcast)]
    public void ChannelSwitch_AlwaysAvailableChannels(string cmd, ActiveSpeechChannel expected)
        => Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, 0, GuildRank.None), Is.EqualTo(expected));

    [TestCase("admin")]
    [TestCase("a")]
    public void ChannelSwitch_Admin_RequiresAdmin(string cmd)
    {
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, Admin, 0, GuildRank.None), Is.EqualTo(ActiveSpeechChannel.Admin));
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, 0, GuildRank.None), Is.Null);
    }

    [TestCase("g")]
    [TestCase("guild")]
    public void ChannelSwitch_Guild_RequiresGuild(string cmd)
    {
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, InGuild, GuildRank.Member), Is.EqualTo(ActiveSpeechChannel.Guild));
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, 0, GuildRank.None), Is.Null);
    }

    [TestCase("o")]
    [TestCase("officer")]
    public void ChannelSwitch_Officer_RequiresOfficerRank(string cmd)
    {
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, InGuild, GuildRank.Officer), Is.EqualTo(ActiveSpeechChannel.Officer));
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, InGuild, GuildRank.Member), Is.Null);
        Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, AdminLevel.Player, 0, GuildRank.None), Is.Null);
    }

    // Tell/Emote/Notice aren't dropdown channels, and non-speech commands aren't either — a bare command
    // never switches for any of them (even a Creator in a guild), so they fall through to normal handling.
    [TestCase("tell")]
    [TestCase("t")]
    [TestCase("w")]
    [TestCase("emote")]
    [TestCase("me")]
    [TestCase("notice")]
    [TestCase("n")]
    [TestCase("kick")]
    [TestCase("help")]
    [TestCase("roll")]
    public void ChannelSwitch_NonDropdownChannel_ReturnsNull(string cmd)
        => Assert.That(SpeechChannelRouter.ChannelSwitchForCommand(cmd, Admin, InGuild, GuildRank.Leader), Is.Null);
}
