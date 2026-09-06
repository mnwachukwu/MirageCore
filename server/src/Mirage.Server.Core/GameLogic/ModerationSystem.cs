using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Lifting a punishment, and reading back what is still in force.
///
/// <para>Lives here rather than beside either caller because there are TWO of them — the server console
/// and the in-game Creator commands — and a lift written twice is a lift that drifts. The apply side has
/// exactly that problem already: <c>PacketHandler.Admin</c> and <c>ConsoleCommands</c> each spell out
/// kick, ban and mute in full.</para>
///
/// <para><b>The thread split is the whole design.</b> Everything that reads a file is async and must NOT
/// run on the game thread; everything that reads the roster is synchronous and must run on NOTHING ELSE.
/// The two are separated here so each caller can make its own hop — the console has to post onto the
/// loop, a packet handler is already on it, and neither can be written to assume the other's position.
/// </para>
/// </summary>
public sealed class ModerationSystem
{
    private readonly IPersistenceService _persistence;
    private readonly PlayerManager _pm;
    private readonly PlayerSaver _saver;
    private readonly Configuration.ServerConfig _config;

    public ModerationSystem(IPersistenceService persistence, PlayerManager pm, PlayerSaver saver,
                            Configuration.ServerConfig? config = null)
    {
        _persistence = persistence;
        _pm = pm;
        _saver = saver;
        // Optional and defaulted, like the clock and the RNG: only the machine-ban MODE is read from it,
        // and every harness that builds this system predates the setting.
        _config = config ?? Configuration.ServerConfig.Default;
    }

    // ── Game thread ONLY ──────────────────────────────────────────────────────

    /// <summary>Who is logged in, keyed by account login, valued by the character they are on.
    /// <para> Game thread only — the roster is consistent nowhere else. The dictionary is built fresh
    /// so nothing the loop owns escapes it.</para></summary>
    public Dictionary<string, string> OnlineLogins()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (int slot in _pm.Online)
        {
            if (!_pm[slot].IsPlaying) continue;
            map[_pm[slot].Login] = _pm[slot].Char.Name.Trim();
        }
        return map;
    }

    /// <summary>Clears the LIVE mute mirror on every session for this account, returning whether any was
    /// found.
    /// <para> Game thread only, and  not optional. Chat checks read
    /// <see cref="ServerPlayer.MutedUntilUtc"/>, not the account file, so clearing the account alone
    /// leaves somebody muted until they relog. Loops every online slot rather than the first match: one
    /// login cannot currently be online twice, and this should not be what breaks if that changes.</para></summary>
    public bool ClearLiveMute(string login)
    {
        bool any = false;
        foreach (int slot in _pm.Online)
        {
            if (!string.Equals(_pm[slot].Login, login, StringComparison.OrdinalIgnoreCase)) continue;
            _pm[slot].MutedUntilUtc = 0;
            any = true;
        }
        return any;
    }

    // ── Off the game thread (file I/O) ────────────────────────────────────────

    /// <summary>Turns an operator's argument into an account login. Prefers an online character of that
    /// name — the handle they can see — and otherwise takes the argument as the login itself. Null when
    /// it is neither.
    /// <para> The apply commands all resolve through the ONLINE ROSTER alone, which is exactly wrong
    /// here: a kicked or banned account cannot be online to be found.</para></summary>
    public async Task<string?> ResolveLoginAsync(string arg, IReadOnlyDictionary<string, string> online)
    {
        string name = arg.Trim();
        if (name.Length == 0) return null;

        foreach (var (login, charName) in online)
        {
            if (string.Equals(charName, name, StringComparison.OrdinalIgnoreCase)) return login;
        }
        return await _persistence.AccountExistsAsync(name) ? name : null;
    }

    /// <summary>The machine key held on an online session — empty when that login is not online or its
    /// client sent none.
    /// <para> Game thread only — it reads live session state, which is the whole constraint on
    /// <c>/hwban</c>: the key exists nowhere else, so there is nothing to ban about somebody offline.</para></summary>
    public string OnlineMachineKey(string login)
    {
        foreach (int slot in _pm.Online)
        {
            if (!string.Equals(_pm[slot].Login, login, StringComparison.OrdinalIgnoreCase)) continue;
            return _pm[slot].MachineKey;
        }
        return "";
    }

    /// <summary>Lifts a ban. Takes the login straight, without checking the account exists: a ban entry
    /// can name an account whose file was since deleted, and refusing those would leave a row nobody can
    /// ever clear.</summary>
    public async Task<LiftOutcome> UnbanAsync(string login) =>
        await _persistence.UnbanAsync(login) ? LiftOutcome.Lifted : LiftOutcome.NothingToLift;

    /// <summary>Bans the machine behind an ALREADY-CAPTURED key, and the account with it.
    ///
    /// <para>Both, because an operator reaching for this has decided the person should be gone, and
    /// banning only the machine would leave the account free to log in from anywhere else. The account
    /// half is the one that always works; the machine half is the one that survives re-registration.</para></summary>
    public async Task<LiftOutcome> HardwareBanAsync(string login, string machineKey, string reason)
    {
        await _persistence.BanAsync(login, reason);
        return await _persistence.HardwareBanAsync(machineKey, login, reason)
            ? LiftOutcome.Lifted
            : LiftOutcome.NothingToLift;
    }

    /// <summary>Lifts every machine ban recorded against a login. Leaves the ACCOUNT ban alone —
    /// <see cref="UnbanAsync"/> is its own command, and an operator may want only one of the two gone.</summary>
    public async Task<LiftOutcome> HardwareUnbanAsync(string login) =>
        await _persistence.HardwareUnbanAsync(login) > 0 ? LiftOutcome.Lifted : LiftOutcome.NothingToLift;

    public async Task<LiftOutcome> UnkickAsync(string login)
    {
        var account = await _persistence.LoadAccountAsync(login);
        if (account is null || account.KickedUntilUtc <= NowUtc) return LiftOutcome.NothingToLift;

        await WriteAsync(login, a => a.KickedUntilUtc = 0);
        return LiftOutcome.Lifted;
    }

    /// <summary>Lifts a mute on the account. <paramref name="clearedLive"/> is what
    /// <see cref="ClearLiveMute"/> returned — an online session can be muted while the file says
    /// otherwise, so a lift is real if EITHER was set.</summary>
    public async Task<LiftOutcome> UnmuteAsync(string login, bool clearedLive)
    {
        var account = await _persistence.LoadAccountAsync(login);
        bool onAccount = account is not null && account.MutedUntilUtc > NowUtc;
        if (!onAccount && !clearedLive) return LiftOutcome.NothingToLift;

        await WriteAsync(login, a => a.MutedUntilUtc = 0);
        return LiftOutcome.Lifted;
    }

    /// <summary>Everything currently in force. <paramref name="online"/> comes from
    /// <see cref="OnlineLogins"/>, captured by the caller on the game thread.
    /// <para>Reads the ban file and sweeps EVERY account, so it is an on-request cost and never a
    /// per-tick one.</para></summary>
    public async Task<ModerationReport> BuildReportAsync(IReadOnlyDictionary<string, string> online)
    {
        var bans = await _persistence.LoadBanListAsync();
        var hwBans = await _persistence.LoadHardwareBanListAsync();
        var (penalties, scanned) = await _persistence.LoadActivePenaltiesAsync(NowUtc);

        return new ModerationReport
        {
            // Keys are deliberately not projected — see HardwareBanSummary.
            HardwareBans = [.. hwBans.Select(b => new HardwareBanSummary
            {
                Login = b.Login,
                Reason = b.Reason,
                BannedAtUtc = b.BannedAtUtc,
            })],
            HardwareBanMode = _config.HardwareBans.Mode.ToString(),
            Bans = [.. bans.Select(b => new BanSummary
            {
                Login = b.Login,
                Reason = b.Reason,
                BannedAtUtc = b.BannedAtUtc,
            })],
            Penalties = [.. penalties.Select(p =>
            {
                bool isOnline = online.TryGetValue(p.Login, out string? charName);
                return new PenaltySummary
                {
                    Login = p.Login,
                    Kind = p.Kind.ToString(),
                    ExpiresUtc = p.ExpiresUtc,
                    IsOnline = isOnline,
                    CharName = charName ?? "",
                };
            })],
            AccountsScanned = scanned,
        };
    }

    /// <summary>Whole minutes left, rounded UP so a penalty with seconds to run never reads as zero and
    /// looks already over.</summary>
    public static int MinutesLeft(long expiresUtc) =>
        (int)Math.Max(1, (expiresUtc - NowUtc + 59) / 60);

    private static long NowUtc => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // AWAITED, not the fire-and-forget MutateAccountInBackground: every caller re-reads the account to
    // report what is now in force, and the enqueued form let that read return the value just replaced.
    private Task WriteAsync(string login, Action<Shared.Records.AccountRecord> mutate) =>
        _saver.MutateAccountAsync(login, mutate);
}

/// <summary>What a lift actually did. <see cref="NotFound"/> is the caller's to report — the system
/// itself only ever sees a login it was given.</summary>
public enum LiftOutcome
{
    Lifted,
    NothingToLift,
    NotFound,
}
