using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Host.Services;

/// <summary>
/// The console's half of moderation: lifting a punishment, and listing what is in force.
///
/// <para>The work itself is <see cref="ModerationSystem"/>, shared with the in-game Creator commands.
/// What lives here is the console's own shape — parsing an argument, printing a table, and hopping onto
/// the game thread, which this runs off.</para>
/// </summary>
public sealed partial class ConsoleCommands
{
    // ── /unban ────────────────────────────────────────────────────────────────

    private async Task CmdUnbanAsync(string args)
    {
        string login = args.Trim();
        if (login.Length == 0)
        {
            Write(ServerStrings.Console_LiftUsage, ("Cmd", "/unban"));
            return;
        }

        if (await _moderation.UnbanAsync(login) is LiftOutcome.NothingToLift)
        {
            Write(ServerStrings.Console_NotBanned, ("Login", login));
            return;
        }
        Write(ServerStrings.Console_Unbanned, ("Login", login));
        _logger.LogInformation("Console lifted the ban on {Login}.", login);
        PublishModeration();
    }

    // ── /hwunban ──────────────────────────────────────────────────────────────

    /// <summary>Lifts every machine ban on an account. Deliberately does NOT lift the account ban —
    /// <c>/unban</c> is its own command, and the two were applied for different reasons.</summary>
    private async Task CmdHwUnbanAsync(string args)
    {
        string login = args.Trim();
        if (login.Length == 0)
        {
            Write(ServerStrings.Console_LiftUsage, ("Cmd", "/hwunban"));
            return;
        }

        if (await _moderation.HardwareUnbanAsync(login) is LiftOutcome.NothingToLift)
        {
            Write(ServerStrings.Console_NotHwBanned, ("Login", login));
            return;
        }
        Write(ServerStrings.Console_HwUnbanned, ("Login", login));
        _logger.LogInformation("Console lifted the machine ban on {Login}.", login);
        PublishModeration();
    }

    // ── /unkick ───────────────────────────────────────────────────────────────

    private async Task CmdUnkickAsync(string args)
    {
        string? login = await ResolveLiftTargetAsync(args, "/unkick");
        if (login is null) return;

        if (await _moderation.UnkickAsync(login) is LiftOutcome.NothingToLift)
        {
            Write(ServerStrings.Console_NotKicked, ("Login", login));
            return;
        }
        Write(ServerStrings.Console_Unkicked, ("Login", login));
        _logger.LogInformation("Console lifted the kick on {Login}.", login);
        PublishModeration();
    }

    // ── /unmute ───────────────────────────────────────────────────────────────

    private async Task CmdUnmuteAsync(string args)
    {
        string? login = await ResolveLiftTargetAsync(args, "/unmute");
        if (login is null) return;

        // The live mirror is cleared ON the game thread and awaited, so the account write below knows
        // whether anything was actually muted.
        bool clearedLive = await OnGameThreadAsync(() => _moderation.ClearLiveMute(login));

        if (await _moderation.UnmuteAsync(login, clearedLive) is LiftOutcome.NothingToLift)
        {
            Write(ServerStrings.Console_NotMuted, ("Login", login));
            return;
        }
        Write(ServerStrings.Console_Unmuted, ("Login", login));
        _logger.LogInformation("Console lifted the mute on {Login}.", login);
        PublishModeration();
    }

    // Shared by the two account-field lifts: prints its own usage and not-found lines, so a caller that
    // gets null has nothing left to say.
    private async Task<string?> ResolveLiftTargetAsync(string args, string cmd)
    {
        if (args.Trim().Length == 0)
        {
            Write(ServerStrings.Console_LiftUsage, ("Cmd", cmd));
            return null;
        }
        var online = await OnGameThreadAsync(_moderation.OnlineLogins);
        string? login = await _moderation.ResolveLoginAsync(args, online);
        if (login is null) Write(ServerStrings.Console_AccountNotFound, ("Name", args.Trim()));
        return login;
    }

    // ── /moderation ───────────────────────────────────────────────────────────

    /// <summary>Prints everything in force, and emits the machine line for an attached dashboard. Both
    /// come off ONE gather, so the table and the page can never disagree.</summary>
    private async Task CmdModerationAsync()
    {
        var report = await BuildReportAsync();

        Write(ServerStrings.Console_ModerationBans, ("Count", report.Bans.Count));
        foreach (var ban in report.Bans)
            Write(ServerStrings.Console_ModerationBanLine, ("Login", ban.Login), ("Reason", ban.Reason));
        if (report.Bans.Count == 0) Write(ServerStrings.Console_ModerationNone);

        // The mode is printed with the list, never above it: rows under Signal mean "watched", rows under
        // Block mean "refused", and a table that does not say which invites the wrong conclusion.
        Write(ServerStrings.Console_ModerationHwBans, ("Count", report.HardwareBans.Count));
        Write(ServerStrings.Console_ModerationHwMode, ("Mode", report.HardwareBanMode));
        foreach (var hw in report.HardwareBans)
            Write(ServerStrings.Console_ModerationHwBanLine, ("Login", hw.Login), ("Reason", hw.Reason));
        if (report.HardwareBans.Count == 0) Write(ServerStrings.Console_ModerationNone);

        Write(ServerStrings.Console_ModerationPenalties, ("Count", report.Penalties.Count));
        foreach (var p in report.Penalties)
        {
            Write(ServerStrings.Console_ModerationPenaltyLine,
                ("Login", p.Login), ("Kind", p.Kind),
                ("Minutes", ModerationSystem.MinutesLeft(p.ExpiresUtc)),
                ("Where", p.CharName));
        }
        if (report.Penalties.Count == 0) Write(ServerStrings.Console_ModerationNone);
        Write(ServerStrings.Console_ModerationScanned, ("Count", report.AccountsScanned));

        _status.Emit(ModerationReport.LinePrefix, report);
    }

    /// <summary>Re-gathers and pushes the report after a change, so an open dashboard reflects a lift
    /// without the operator asking again. Fire-and-forget — nothing waits on a dashboard — and silent
    /// when nothing is attached, because the gather reads every account file.</summary>
    private void PublishModeration()
    {
        if (!_status.HasConsumers) return;
        _ = Task.Run(async () =>
        {
            try
            {
                _status.Emit(ModerationReport.LinePrefix, await BuildReportAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish the moderation report.");
            }
        });
    }

    private async Task<ModerationReport> BuildReportAsync() =>
        await _moderation.BuildReportAsync(await OnGameThreadAsync(_moderation.OnlineLogins));

    /// <summary>Runs <paramref name="read"/> on the game thread and awaits its result. The console runs
    /// off-loop, so anything that touches player state has to make this hop.</summary>
    private Task<T> OnGameThreadAsync<T>(Func<T> read)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            try { tcs.TrySetResult(read()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }
}
