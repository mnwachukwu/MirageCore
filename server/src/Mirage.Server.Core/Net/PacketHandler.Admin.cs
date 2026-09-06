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

/// <summary>Operator commands — location and warp tools, sprite changes, map respawn and reports,
/// kick/ban/mute, access level changes, and the MOTD, time-of-day and weather overrides. Every
/// handler re-checks the sender's <c>AdminLevel</c> and reports a mismatch as a hacking attempt.</summary>
public sealed partial class PacketHandler
{
    //  Info request handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleRequestLocation(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        var p = _pm[index].Char;
        // Also read out the map's identifier Name + its MapGroup's Name (identifier names, not display names) —
        // handy when authoring territories. "-" when unset.
        var map = _world.Maps[p.Map];
        var group = map.MapGroup > 0 ? _world.MapGroups.GetValueOrDefault(map.MapGroup) : null;
        string mapName = string.IsNullOrWhiteSpace(map.Name) ? "-" : map.Name.Trim();
        string groupName = group is null || string.IsNullOrWhiteSpace(group.Name) ? "-" : group.Name.Trim();
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_Location, new ChatMetadata(GameColor.Pink, ChatChannel.Notice),
            ("Map", p.Map), ("X", p.X), ("Y", p.Y), ("MapName", mapName), ("GroupName", groupName));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Admin handlers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary><c>/godmode</c> — flip the sender in or out of observer mode: passes through every
    /// obstacle, spends no stamina, cannot act on anything and cannot be acted on. Toggled rather than set,
    /// so the one command both enters and leaves.
    /// <para>Combat state is dropped on the way in. Entering mid-fight would otherwise leave the fight
    /// marked on a player nothing can reach, holding the combat bar up and blocking logout until it
    /// expired.</para></summary>
    private void HandleGodMode(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (!_pm[index].Char.MayUseGodMode)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        var sp = _pm[index];
        sp.Char.GodMode = !sp.Char.GodMode;
        if (sp.Char.GodMode)
        {
            sp.CombatExpiresAt = 0;
            sp.Target = 0;
            sp.TargetType = 0;
        }

        // The flag rides on SendPlayerData, which otherwise only carries it at join — so without this the
        // toggle reaches nobody: the mover's own prediction keeps refusing blocked steps and every client
        // keeps drawing the name in its normal color.
        SendToMap(sp.Char.Map, PacketBuilder.PlayerData(index, sp.Char, sp.Char.Map,
            sp.PkGraceUntilUtc, sp.AggressorUntilUtcNow, godMode: sp.Char.GodMode));

        _dispatcher.SendLocalizedChatTo(index,
            sp.Char.GodMode ? ServerStrings.AdminCommand_GodModeOn : ServerStrings.AdminCommand_GodModeOff,
            new ChatMetadata(GameColor.BrightCyan, ChatChannel.Notice));
        _logger.LogInformation("{Name} turned god mode {State}.",
            _pm[index].Char.Name.Trim(), sp.Char.GodMode ? "on" : "off");
    }

    private void HandleWarpMeTo(int index, WarpMeToPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Developer)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        // A corpse stays where it fell — including an admin's own. The target-side twin of this lives in
        // HandleWarpToMe.
        if (_pm[index].Char.Dead)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_WarpWhileDead,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotWarpSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }

        var tp = _pm[n].Char;
        _movement.PlayerWarp(index, tp.Map, tp.X, tp.Y);
        _dispatcher.SendLocalizedChatTo(n, ServerStrings.AdminCommand_WarpedToPlayer, new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Admin", _pm[index].Char.Name.Trim()));
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_WarpedToTarget, new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Target", tp.Name.Trim()));
        _logger.LogInformation("{Name} has warped to {Target}, map #{Map}.", _pm[index].Char.Name.Trim(), tp.Name.Trim(), tp.Map);
    }

    private void HandleWarpToMe(int index, WarpToMePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Developer)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotWarpSelfToSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }

        // A corpse stays where it fell, and the admin who asked hears why rather than watching nothing
        // happen.
        if (_pm[n].Char.Dead)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_TargetIsDead,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice), ("Target", _pm[n].Char.Name.Trim()));
            return;
        }

        var my = _pm[index].Char;
        _movement.PlayerWarp(n, my.Map, my.X, my.Y);
        _dispatcher.SendLocalizedChatTo(n, ServerStrings.AdminCommand_SummonedYou, new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Admin", my.Name.Trim()));
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_PlayerSummoned, new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Target", _pm[n].Char.Name.Trim()));
        _logger.LogInformation("{Name} warped {Target} to self, map #{Map}.", my.Name.Trim(), _pm[n].Char.Name.Trim(), my.Map);
    }

    private void HandleWarpTo(int index, WarpToPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }
        if (RefuseWhileDead(index)) return;

        if (p.MapNum <= 0 || p.MapNum > _world.Limits.Maps)
        {
            HackingAttempt(index, "Invalid map");
            return;
        }

        // The command names a MAP; the tile is incidental, carried over from wherever they were standing.
        // It is clamped onto the destination so a jump from a large map to a small one still lands.
        var ch = _pm[index].Char;
        var (_, x, y) = _world.RepairPosition(p.MapNum, ch.X, ch.Y, (p.MapNum, 0, 0));
        _movement.PlayerWarp(index, p.MapNum, x, y);
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_WarpedToMap, new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Map", p.MapNum));
        _logger.LogInformation("{Name} warped to map #{Map}.", ch.Name.Trim(), p.MapNum);
    }

    private void HandleSetSprite(int index, SetSpritePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        _pm[index].Char.Sprite = p.Sprite;
        SendToMap(_pm[index].Char.Map, PacketBuilder.PlayerData(index, _pm[index].Char, _pm[index].Char.Map, _pm[index].PkGraceUntilUtc, _pm[index].AggressorUntilUtcNow));
    }

    private void HandleMapRespawn(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int mapNum = _pm[index].Char.Map;

        _items.ClearMapItems(mapNum);
        _items.SpawnMapItems(mapNum);

        // Respawn NPCs (1-based slots 1..MaxMapNpcs)
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            _spawn.SpawnNpc(i, mapNum);

        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_MapRespawned, new ChatMetadata(GameColor.Blue, ChatChannel.Notice));
        _logger.LogInformation("{Name} has respawned map #{Map}.", _pm[index].Char.Name.Trim(), mapNum);
    }

    private void HandleMapReport(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        var sb = new System.Text.StringBuilder("Free Maps: ");
        int start = 1, end = 1;
        for (int i = 1; i <= _world.Limits.Maps; i++)
        {
            if (string.IsNullOrWhiteSpace(_world.Maps[i].Name))
            {
                end = i + 1;
            }
            else
            {
                if (end - start > 0)
                    sb.Append($"{start}-{end - 1}, ");
                start = i + 1;
                end = i + 1;
            }
        }
        if (end - start > 0) sb.Append($"{start}-{end - 1}");
        _dispatcher.SendTo(index, PacketBuilder.ChatMsg(sb.ToString().TrimEnd(',', ' ') + ".", GameColor.Brown, ChatChannel.Notice));
    }

    private const int DefaultPenaltyMinutes = 60;
    private const int MaxPenaltyMinutes = 1440;

    /// <summary>Parses and clamps a penalty duration. 0 → default; out-of-range → returns false and
    /// sends the issuer an "invalid minutes" chat error.</summary>
    private bool TryGetPenaltyMinutes(int index, int requested, out int minutes)
    {
        if (requested == 0)
        {
            minutes = DefaultPenaltyMinutes;
            return true;
        }
        if (requested < 1 || requested > MaxPenaltyMinutes)
        {
            minutes = 0;
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_InvalidMinutes,
                new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return false;
        }
        minutes = requested;
        return true;
    }

    /// <summary>True if the target's character is an admin and therefore immune to kick/ban/mute.</summary>
    private bool TargetIsAdmin(int index, int targetIndex)
    {
        if (_pm[targetIndex].Char.Access == AdminLevel.Player) return false;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotTargetAdmin,
            new ChatMetadata(GameColor.White, ChatChannel.Notice));
        return true;
    }

    private void HandleKickPlayer(int index, KickPlayerPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access <= AdminLevel.Player)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotKickSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }
        if (TargetIsAdmin(index, n)) return;
        if (!TryGetPenaltyMinutes(index, p.Minutes, out int minutes)) return;

        string adminName = _pm[index].Char.Name.Trim();
        string targetName = _pm[n].Char.Name.Trim();
        string targetLogin = _pm[n].Login;
        long expiryUtc = NowUtc + minutes * 60L;
        ApplyAccountKickAsync(targetLogin, expiryUtc);

        _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_KickBroadcast,
            new ChatMetadata(GameColor.White, ChatChannel.Notice),
            ("Target", targetName), ("GameName", _config.GameName), ("Admin", adminName), ("Minutes", minutes));
        _logger.LogInformation("{Admin} has kicked {Target} for {Minutes} minute(s).", adminName, targetName, minutes);
        AlertAndDisconnect(n, ServerStrings.AdminCommand_Kicked,
            ("Admin", adminName), ("Minutes", minutes));
    }

    private void HandleBanPlayer(int index, BanPlayerPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access <= AdminLevel.Player)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotBanSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }
        if (TargetIsAdmin(index, n)) return;

        string adminName = _pm[index].Char.Name.Trim();
        string targetName = _pm[n].Char.Name.Trim();
        string banLogin = _pm[n].Login;
        _bg.Run(_persistence.BanAsync(banLogin, $"Banned by {adminName}"), nameof(IPersistenceService.BanAsync));
        _logger.LogInformation("{Admin} has banned {Target}.", adminName, targetName);
        _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_BanBroadcast,
            new ChatMetadata(GameColor.White, ChatChannel.Notice),
            ("Target", targetName), ("GameName", _config.GameName), ("Admin", adminName));
        AlertAndDisconnect(n, ServerStrings.Auth_Banned, ("GameName", _config.GameName));
    }

    /// <summary>
    /// <c>/hwban</c> — ban the account and the machine it is signed in from. CREATOR only, unlike the
    /// plain ban: this is the last resort, and it is the one punishment that can reach somebody who has
    /// done nothing yet under a name nobody has seen.
    ///
    /// <para> Requires the target to be ONLINE, and says so rather than silently doing half the job. The
    /// machine key lives on the live session and is never written to an account file, so there is nothing
    /// to record for somebody who is not here — that case is what <c>/ban</c> is for.</para>
    /// </summary>
    private void HandleHwBanPlayer(int index, HwBanPlayerPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotBanSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }
        if (TargetIsAdmin(index, n)) return;

        string adminName = _pm[index].Char.Name.Trim();
        string targetName = _pm[n].Char.Name.Trim();
        string banLogin = _pm[n].Login;
        string machineKey = _pm[n].MachineKey;

        // No key means their client never sent one — an older build, or a machine that would not identify
        // itself. The ACCOUNT ban still lands; the operator is told the machine half did not, because a
        // silent downgrade here is how somebody believes a machine is blocked when it is not.
        if (machineKey.Length == 0)
        {
            _bg.Run(_persistence.BanAsync(banLogin, $"Banned by {adminName}"), nameof(IPersistenceService.BanAsync));
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_HwBanNoKey,
                new ChatMetadata(GameColor.Yellow, ChatChannel.Notice), ("Target", targetName));
            _logger.LogWarning("{Admin} hardware-banned {Target}, but the session carried no machine key; the account ban was applied alone.",
                adminName, targetName);
            AlertAndDisconnect(n, ServerStrings.Auth_Banned, ("GameName", _config.GameName));
            return;
        }

        _bg.Run(_moderation.HardwareBanAsync(banLogin, machineKey, $"Hardware banned by {adminName}"),
                nameof(ModerationSystem.HardwareBanAsync));
        _logger.LogInformation("{Admin} has hardware-banned {Target}.", adminName, targetName);
        _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_BanBroadcast,
            new ChatMetadata(GameColor.White, ChatChannel.Notice),
            ("Target", targetName), ("GameName", _config.GameName), ("Admin", adminName));
        AlertAndDisconnect(n, ServerStrings.Auth_Banned, ("GameName", _config.GameName));
    }

    private void HandleMutePlayer(int index, MutePlayerPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Monitor)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotMuteSelf, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }
        if (TargetIsAdmin(index, n)) return;
        if (!TryGetPenaltyMinutes(index, p.Minutes, out int minutes)) return;

        string adminName = _pm[index].Char.Name.Trim();
        string targetName = _pm[n].Char.Name.Trim();
        string targetLogin = _pm[n].Login;
        long expiryUtc = NowUtc + minutes * 60L;
        _pm[n].MutedUntilUtc = expiryUtc;
        ApplyAccountMuteAsync(targetLogin, expiryUtc);

        _logger.LogInformation("{Admin} has muted {Target} for {Minutes} minute(s).", adminName, targetName, minutes);
        _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_MuteBroadcast,
            new ChatMetadata(GameColor.White, ChatChannel.Notice),
            ("Target", targetName), ("Admin", adminName), ("Minutes", minutes));
    }

    private void HandleRefreshBanList(int index)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Monitor)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }
        _bg.Run(_persistence.RefreshBanListAsync(), nameof(IPersistenceService.RefreshBanListAsync));
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_BanListRefreshed,
            new ChatMetadata(GameColor.White, ChatChannel.Notice));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Lifting a punishment — CREATOR only
    // ═══════════════════════════════════════════════════════════════════════════
    // Gated a rung above the commands that APPLY these. Deciding a punishment was wrong is a different
    // call from issuing one, and the person best placed to make it is whoever answers for the server.
    //
    // The work is ModerationSystem, shared with the server console — see there for why a lift is written
    // once. What is here is the packet's own shape: the access check, and the hop off the game thread.
    //
    // These read account FILES, so they cannot run on the loop. Each starts an async continuation and
    // posts the reply back, exactly as the class comment on PacketHandler describes. That also means the
    // handler returns before anything has happened, and the Creator learns the outcome from chat.

    private void HandleUnbanPlayer(int index, UnbanPlayerPacket p) =>
        LiftAsync(index, p.Target, resolveAccount: false, async login =>
            (await _moderation.UnbanAsync(login),
             ServerStrings.AdminCommand_Unbanned, ServerStrings.AdminCommand_NotBanned));

    private void HandleUnkickPlayer(int index, UnkickPlayerPacket p) =>
        LiftAsync(index, p.Target, resolveAccount: true, async login =>
            (await _moderation.UnkickAsync(login),
             ServerStrings.AdminCommand_Unkicked, ServerStrings.AdminCommand_NotKicked));

    private void HandleHwUnbanPlayer(int index, HwUnbanPlayerPacket p) =>
        LiftAsync(index, p.Target, resolveAccount: false, async login =>
            (await _moderation.HardwareUnbanAsync(login),
             ServerStrings.AdminCommand_HwUnbanned, ServerStrings.AdminCommand_NotHwBanned));

    private void HandleUnmutePlayer(int index, UnmutePlayerPacket p) =>
        // The live mirror is cleared HERE, on the game thread, before the continuation leaves it.
        LiftAsync(index, p.Target, resolveAccount: true, async login =>
        {
            bool clearedLive = await OnGameThread(() => _moderation.ClearLiveMute(login));
            return (await _moderation.UnmuteAsync(login, clearedLive),
                    ServerStrings.AdminCommand_Unmuted, ServerStrings.AdminCommand_NotMuted);
        });

    /// <summary>The shape every lift shares: check access, resolve the target to an account, run the
    /// lift off the loop, and report the outcome back on it.</summary>
    private void LiftAsync(int index, string target, bool resolveAccount,
                           Func<string, Task<(LiftOutcome Outcome, string Lifted, string Nothing)>> lift)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        string arg = target.Trim();
        if (arg.Length == 0) return;
        var online = _moderation.OnlineLogins();       // captured on the loop, used off it
        string adminName = _pm[index].Char.Name.Trim();

        _bg.Run(Task.Run(async () =>
        {
            // A ban entry can name an account whose file was deleted, so /unban never demands one.
            string? login = resolveAccount ? await _moderation.ResolveLoginAsync(arg, online) : arg;
            if (login is null)
            {
                Reply(index, ServerStrings.AdminCommand_AccountNotFound, GameColor.BrightRed, ("Name", arg));
                return;
            }

            var (outcome, lifted, nothing) = await lift(login);
            if (outcome is LiftOutcome.Lifted)
            {
                Reply(index, lifted, GameColor.BrightGreen, ("Login", login));
                _logger.LogInformation("{Admin} lifted a punishment on {Login}.", adminName, login);
                // Re-push so the panel drops the row it just acted on rather than showing a lift that
                // already happened beside a button that would now do nothing.
                await SendModerationListAsync(index);
            }
            else
            {
                Reply(index, nothing, GameColor.White, ("Login", login));
            }
        }), "moderation lift");
    }

    private void HandleRequestModeration(int index, RequestModerationPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }
        _bg.Run(SendModerationListAsync(index), "moderation report");
    }

    /// <summary>Gathers the report off the loop and sends it to one Creator. Reading the ban file and
    /// sweeping every account cannot happen on the game thread, so the roster snapshot is taken first.</summary>
    private async Task SendModerationListAsync(int index)
    {
        var online = await OnGameThread(_moderation.OnlineLogins);
        var report = await _moderation.BuildReportAsync(online);
        string login = _pm[index].Login;

        _gameLoop.Post(() =>
        {
            // The slot may have been handed to somebody else while the sweep ran, and this payload is
            // for Creators only.
            if (!_pm[index].IsPlaying || !string.Equals(_pm[index].Login, login, StringComparison.OrdinalIgnoreCase)) return;
            if (_pm[index].Char.Access < AdminLevel.Creator) return;
            _dispatcher.SendTo(index, new ModerationListPacket
            {
                Bans = [.. report.Bans],
                Penalties = [.. report.Penalties],
                HardwareBans = [.. report.HardwareBans],
                HardwareBanMode = report.HardwareBanMode,
                AccountsScanned = report.AccountsScanned,
            });
        });
    }

    /// <summary>Sends a line back to the requesting Creator from off the loop, hopping onto it first and
    /// re-checking they are still the same session — an async reply can land after they disconnected and
    /// the slot was handed to somebody else.</summary>
    private void Reply(int index, string key, int color, params (string Key, object? Value)[] args)
    {
        string login = _pm[index].Login;
        _gameLoop.Post(() =>
        {
            if (!_pm[index].IsPlaying || !string.Equals(_pm[index].Login, login, StringComparison.OrdinalIgnoreCase)) return;
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, ChatChannel.Notice), args);
        });
    }

    private Task<T> OnGameThread<T>(Func<T> read)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            try { tcs.TrySetResult(read()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    // Kick/mute persist the penalty timer through PlayerSaver's per-login chain (the single
    // serialized account writer) so an admin action can't race a concurrent character save.
    // Fire-and-forget — the broadcast + disconnect happen on the caller's tick, staying responsive.
    private void ApplyAccountKickAsync(string login, long expiryUtc) =>
        _saver.MutateAccountInBackground(login, a => a.KickedUntilUtc = expiryUtc);

    private void ApplyAccountMuteAsync(string login, long expiryUtc) =>
        _saver.MutateAccountInBackground(login, a => a.MutedUntilUtc = expiryUtc);

    /// <summary>True if the speaker is muted; sends them a "you are muted" notice and returns true,
    /// in which case the caller must early-return without broadcasting.</summary>
    private bool IsMutedAndNotify(int index)
    {
        long nowUtc = NowUtc;
        long expiry = _pm[index].MutedUntilUtc;
        if (expiry <= nowUtc) return false;
        int minutesLeft = (int)Math.Max(1, (expiry - nowUtc + 59) / 60);
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_YouAreMuted,
            new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice),
            ("Minutes", minutesLeft));
        return true;
    }

    /// <summary>Who is speaking, for player-originated chat: the trimmed character name, their access, and
    /// their PK status frozen at send time (mirroring the renderer's
    /// <c>IsPk(now) && PkGraceUntil <= now</c> rule), plus the ACCOUNT login behind them.
    ///
    /// <para><see cref="Login"/> never reaches the wire: passing it as <c>ChatMetadata.SpeakerLogin</c> is
    /// what lets the dispatch drop the message for recipients who ignore this account. Named rather than a
    /// tuple because it and <see cref="Name"/> are both strings and mean very different things — one is
    /// public, one must not be.</para></summary>
    private readonly record struct Speaker(string Name, AdminLevel Access, bool ShowAsPk, string Login);

    private Speaker SpeakerOf(int index)
    {
        var sp = _pm[index];
        long nowUtc = NowUtc;
        bool showAsPk = sp.Char.IsPk(nowUtc) && sp.PkGraceUntilUtc <= nowUtc;
        return new Speaker(sp.Char.Name.Trim(), sp.Char.Access, showAsPk, sp.Login);
    }

    /// <summary>The speaker's name with their admin rank prefaced ("Monitor Bob") for non-guild channels —
    /// only above Player. The bare name still travels as <c>ChatMetadata.SpeakerName</c>, so name coloring,
    /// the right-click target, and /r resolution use the real name; the rank word is plain leading text.</summary>
    private static string AccessName(string name, AdminLevel access) => access switch
    {
        AdminLevel.Monitor => ServerStrings.Get(ServerStrings.Access_Monitor) + " " + name,
        AdminLevel.Mapper => ServerStrings.Get(ServerStrings.Access_Mapper) + " " + name,
        AdminLevel.Developer => ServerStrings.Get(ServerStrings.Access_Developer) + " " + name,
        AdminLevel.Creator => ServerStrings.Get(ServerStrings.Access_Creator) + " " + name,
        _ => name,
    };

    private void HandleSetAccess(int index, SetAccessPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Trying to use powers not available");
            return;
        }

        int n = _pm.FindPlayerByName(p.Target);
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }
        if (n == index)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_CannotModifyAccess, new ChatMetadata(GameColor.White, ChatChannel.Notice));
            return;
        }
        if (p.Level > AdminLevel.Creator)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_InvalidAccessLevel, new ChatMetadata(GameColor.Warning, ChatChannel.Notice));
            return;
        }

        string login = _pm[n].Login;
        if (_pm[n].Char.Access == AdminLevel.Player && p.Level > AdminLevel.Player)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_PlayerGrantedAccess,
                new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice),
                ("Target", _pm[n].Char.Name.Trim()));
        }

        // Access is per-account: persist to the account, then update every online character on it so the
        // change is account-wide and its overhead name recolors immediately.
        _saver.MutateAccountInBackground(login, a => a.Access = p.Level);
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying || !string.Equals(_pm[i].Login, login, StringComparison.OrdinalIgnoreCase)) continue;
            _pm[i].Char.Access = p.Level;
            SendToMap(_pm[i].Char.Map,
                PacketBuilder.PlayerData(i, _pm[i].Char, _pm[i].Char.Map, _pm[i].PkGraceUntilUtc, _pm[i].AggressorUntilUtcNow));
        }
        _logger.LogInformation("{Admin} set {Target}'s account access to {Level}.", _pm[index].Char.Name.Trim(), login, p.Level);
    }

    private void HandleSetMotd(int index, SetMotdPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Mapper)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }

        // Blank means TAKE IT DOWN, and a clear is silent. The announcement quotes the new text, so an
        // empty one puts "Message of the Day changed to:" in front of every player and says nothing.
        // Same rule as the console's /motd, because they are the same action arrived at from two sides.
        bool clearing = string.IsNullOrWhiteSpace(p.Motd);
        string motd = clearing ? "" : p.Motd;

        _world.Motd = motd;
        _bg.Run(_persistence.SaveMotdAsync(motd), nameof(IPersistenceService.SaveMotdAsync));
        if (!clearing)
            _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_MotdChanged, new ChatMetadata(GameColor.BrightCyan, ChatChannel.Notice), ("Motd", motd));
        _logger.LogInformation("{Name} {Action} the Message of the Day.", _pm[index].Char.Name.Trim(), clearing ? "cleared" : "changed");
    }

    private void HandleSetTimeOfDay(int index, SetTimeOfDayPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Developer)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }
        _tod.JumpToPhase(p.Phase, _pm[index].Char.Name.Trim());
        _gameLoop.PersistEnvironmentNow();
        _logger.LogInformation("{Name} jumped time of day to {Phase}", _pm[index].Char.Name.Trim(), p.Phase);
    }

    private void HandleSetWeather(int index, SetWeatherPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Developer)
        {
            HackingAttempt(index, "Admin Cloning");
            return;
        }
        _weather.SetWeatherAdmin(p.Weather, _pm[index].Char.Name.Trim());
        _gameLoop.PersistEnvironmentNow();
        _logger.LogInformation("{Name} set weather to {Weather}", _pm[index].Char.Name.Trim(), p.Weather);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    // Starts an async handler without blocking the receive loop.
    // Logs any unhandled exception so failures are never silently swallowed.
    private void RunAsync(Task task, string context)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception!.InnerException ?? t.Exception,
                "Unhandled error in {Context}", context),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // Core sender: the message is already localized. Tags the result code (None for ordinary
    // alerts) and disconnects.
    private void SendAlertAndDisconnect(int index, string message, AlertCode code = AlertCode.None)
    {
        _dispatcher.SendTo(index, PacketBuilder.Alert(message, code));
        _dispatcher.GracefulDisconnect(index);
    }

    // Key-based overload — resolves the localized text via the recipient's session locale
    // (which is set from packet.Locale by the pre-session prelude, so auth/validation errors
    // land in the client's chosen language even before login succeeds). There is deliberately no
    // (int, string) literal overload: a no-arg key call must resolve here, not be sent verbatim.
    private void AlertAndDisconnect(int index, string key, params (string Key, object? Value)[] args) =>
        SendAlertAndDisconnect(index, ServerStrings.ForPlayer(index, key, args));

    // Auth outcomes the client's flow logic branches on — carries a stable result code so the
    // client never has to match on (localizable) prose.
    private void AlertAndDisconnect(int index, string key, AlertCode code) =>
        SendAlertAndDisconnect(index, ServerStrings.ForPlayer(index, key), code);

    private void HackingAttempt(int index, string reason)
    {
        if (_pm[index].IsPlaying)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_BootedFor,
                new ChatMetadata(GameColor.White, ChatChannel.Notice),
                ("Player", $"{_pm[index].Login}/{_pm[index].Char.Name.Trim()}"), ("Reason", reason));
        }
        AlertAndDisconnect(index, ServerStrings.AdminCommand_ConnectionLost, ("GameName", _config.GameName));
    }

    private static bool IsValidName(string name) => NameRules.HasValidChars(name);
}
