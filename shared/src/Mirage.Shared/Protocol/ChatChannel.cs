namespace Mirage.Shared.Protocol;

/// <summary>Classification for every server-to-client chat line, routing it to the tabs whose filter
/// accepts the channel. Every send site tags itself explicitly, so adding a channel is a compile error
/// at each one rather than a silent default.
///
/// <para><c>Always</c> is the one un-mutable bucket: it bypasses every tab filter and is never drawn as
/// a toggle. It carries the welcome batch.</para>
///
/// <para>LAYOUT MATTERS. The options panel iterates ranges of this enum to draw the Chat / System /
/// Combat grids, so groups must stay contiguous and nothing may be inserted mid-group without updating
/// that iteration.</para></summary>
public enum ChatChannel : byte
{
    Always = 0,

    // Chat group — player-originated speech (and admin-to-admin chatter). Emotes (`/me`) and dice
    // rolls (`/roll`) are player actions rather than a filter bucket of their own, so they classify
    // as Say — they keep their distinct display colors but ride the Say checkbox in the options panel.
    Say,
    Yell,
    Broadcast,
    Tell,
    AdminChat,

    // System group — server-originated notifications. All three share the same default tab routing
    // (shown in the General tab, hidden from the Combat tab); they're split so players can filter
    // them independently and so each send site reads by intent:
    //   Notice          — server-wide announcements and admin actions: /notice broadcasts, admin
    //                      commands (warp, summon, ban, MOTD, ...), and the player-death and
    //                      Player-Killer broadcasts. "An event the server is announcing to everyone."
    //   JoinLeaveNotice — player join/leave lines, split out so they can be muted on their own.
    //   System          — personal, automatic feedback about your own character: level up/down,
    //                      Level 10 milestones, item durability loss. "What just happened to me."
    Notice,
    JoinLeaveNotice,
    System,

    // Combat group — the blow-by-blow of a fight and its spoils. These two form the options panel's
    // "Combat" section, but their default tab routing deliberately differs (see
    // ChatPanel.MakeInstallDefaultTabs): the General tab hides Combat (too noisy) yet keeps Rewards,
    // while the dedicated Combat tab shows both. So EXP/loot surfaces in the main tab out of the box;
    // the hit-by-hit feed is opt-in.
    //   Combat  — live fight feedback: your hits and hits taken, blocks/dodges/crits, NPC attack
    //             "says" and cast announcements, and the per-victim death-penalty notices. The
    //             default channel for CombatSystem's SendMsg. "What's happening in the fight."
    //   Rewards — the spoils ledger from a kill: EXP gained/lost (including level-gap gating) and
    //             loot drops/rolls. "What I walked away with."
    Combat,
    Rewards,

    // Guild group — private to a guild. Appended at the end so the existing values above keep their
    // wire numbers. Guild carries member chat + guild system notices; GuildOfficer is leader/officer-only
    // chat. GuildWar (also guild-private, member-only) carries the private war messages — peace negotiation
    // and per-guild war results.
    Guild,
    GuildOfficer,
    GuildWar,

    // War — public war messages (grudge declarations/retractions/resolutions + war-death readouts). A
    // combat-group channel: opt-in like Combat.
    War,
}
