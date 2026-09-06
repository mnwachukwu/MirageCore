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

/// <summary>Movement and facing packets — the two highest-frequency messages the server receives — plus
/// /home, the one way a player moves themselves somewhere they are not adjacent to.</summary>
public sealed partial class PacketHandler
{
    //  Movement handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandlePlayerMove(int index, PlayerMovePacket p)
    {
        if (!_pm[index].IsPlaying || _pm[index].GettingMap) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't move (client freezes this; server enforces)
        if (p.Dir > Direction.Right)
        {
            HackingAttempt(index, "Invalid Direction");
            return;
        }
        if (p.Movement < MovementType.Walking || p.Movement > MovementType.Running)
        {
            HackingAttempt(index, "Invalid Movement");
            return;
        }

        // Casting does not block movement, so a move packet is always honored regardless of a recent cast.
        _movement.PlayerMove(index, p.Dir, p.Movement);
    }

    private void HandlePlayerDir(int index, PlayerDirPacket p)
    {
        if (!_pm[index].IsPlaying || _pm[index].GettingMap) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't turn
        if (p.Dir > Direction.Right)
        {
            HackingAttempt(index, "Invalid Direction");
            return;
        }
        _movement.PlayerDir(index, p.Dir);
    }

    /// <summary>/home — go to this character's spawn point, or the server default when they have not set
    /// one. Convenience, and the way out when a player is stuck somewhere they cannot walk off.
    ///
    /// <para>The cooldown is a wall-clock stamp on the character, not a session timer: it keeps running
    /// while the player is logged out, and a relog neither clears nor pauses it. Saved with the same
    /// immediacy the Inn uses, because a cooldown a hard disconnect erases is not a cooldown.</para>
    ///
    /// <para>Refused in combat, so it cannot be an escape hatch from a fight. The client hides it there
    /// too; this is the half that counts, since the packet can arrive regardless.</para></summary>
    private void HandleHomeRequest(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var vp = sp.Char;

        // A corpse stays where it fell. Refused above the cooldown gate so a dead player is never charged
        // the cooldown for a warp that did not happen.
        if (vp.Dead)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeWhileDead,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        if (sp.IsInCombat(Environment.TickCount64))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeInCombat,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        long now = NowUtc;
        long readyAt = vp.HomeUsedAtUtc + Constants.HomeCooldownSeconds;
        if (vp.HomeUsedAtUtc > 0 && now < readyAt)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeCooldown,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
                ("Remaining", PlaytimeFormat.HoursMinutes(readyAt - now)));
            return;
        }

        var (map, x, y) = _config.Spawn.HomeFor(vp);
        // The cooldown is stamped below, after this gate: a home point that names no tile refuses the
        // command outright, and a refusal costs nothing.
        if (!_movement.IsWarpDestinationValid(map, x, y))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeDestinationMissing,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice));
            return;
        }

        vp.HomeUsedAtUtc = now;
        _movement.PlayerWarp(index, map, x, y);
        _saver.SaveCharInBackground(sp.Login, sp.CharNum, vp.Clone(), sp.CloneBank());
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeWarped,
            new ChatMetadata(GameColor.BrightCyan, ChatChannel.Notice));
    }

    /// <summary>/homecd — how long is left on the cooldown. Reads the stamp and writes nothing, so
    /// checking never costs the use being checked for.</summary>
    private void HandleHomeCooldownRequest(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;

        long now = NowUtc;
        long readyAt = sp.Char.HomeUsedAtUtc + Constants.HomeCooldownSeconds;
        if (sp.Char.HomeUsedAtUtc > 0 && now < readyAt)
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeCooldownLeft,
                new ChatMetadata(GameColor.Yellow, ChatChannel.Notice),
                ("Remaining", PlaytimeFormat.HoursMinutes(readyAt - now)));
        else
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_HomeReady,
                new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice));
    }
}
