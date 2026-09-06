using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Per-account friends + ignore lists. Runs on the game thread. Both lists hold account LOGINS (the
/// social unit is the account, so ignoring someone blocks every character they own); the live mirrors
/// are <see cref="ServerPlayer.Friends"/> / <see cref="ServerPlayer.Ignore"/>, hydrated at login, and
/// the authoritative copies persist on the <see cref="AccountRecord"/> through the per-login write chain.
///
/// Targets are ADDED by character name and must be online — that is the only way to resolve a character
/// to its account without a name→account index, and it matches the right-click-a-player flow. Removal
/// is by login (the row's own identity), so it works for offline accounts.
/// </summary>
public sealed class SocialSystem : GameSystem
{
    private readonly PlayerManager _pm;
    private readonly PlayerSaver _saver;
    private readonly ILogger<SocialSystem> _logger;

    public SocialSystem(PlayerManager pm, IPacketDispatcher dispatcher, PlayerSaver saver, ILogger<SocialSystem> logger)
        : base(dispatcher)
    {
        _pm = pm;
        _saver = saver;
        _logger = logger;
    }

    // ── Enforcement ──────────────────────────────────────────────────────────────

    /// <summary>Raw predicate: true when <paramref name="index"/> ignores <paramref name="senderLogin"/>.
    /// The chat dispatch enforces ignore per-recipient itself (it cannot depend on this system without a DI
    /// cycle), and does so with a Monitor+ BYPASS — an admin account's messages always get through, the
    /// ignore only re-applies if they drop back to Player. A future non-chat caller that gates message
    /// delivery must apply that same bypass; this raw predicate does not.</summary>
    public bool IsIgnoring(int index, string senderLogin) => _pm[index].Ignores(senderLogin);

    // ── Client sync ──────────────────────────────────────────────────────────────

    /// <summary>Push the account's friends + ignore lists to its client (on entering the world and
    /// after any change).</summary>
    public void SyncTo(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        _dispatcher.SendTo(index, new SocialListPacket
        {
            Friends = BuildEntries(sp.Friends),
            Ignore = BuildEntries(sp.Ignore),
        });
    }

    private List<SocialEntry> BuildEntries(List<string> logins)
    {
        var rows = new List<SocialEntry>(logins.Count);
        foreach (string login in logins) rows.Add(BuildEntry(login));
        return rows;
    }

    /// <summary>Build a display row for an account. Only the ONLINE case can carry a character snapshot:
    /// these lists store logins alone, and there is no per-friend cache to read an offline account's last
    /// character or logout time from (unlike a guild roster, which maintains one).</summary>
    private SocialEntry BuildEntry(string login)
    {
        int idx = _pm.FindOnlineByLogin(login);
        if (idx == 0) return new SocialEntry { Login = login, Online = false };
        var sp = _pm[idx];
        return new SocialEntry
        {
            Login = login,
            Online = true,
            CharName = sp.Char.TrimmedName,
            CharClass = sp.Char.Class,
            CharLevel = sp.Char.Level,
        };
    }

    // ── Friends ──────────────────────────────────────────────────────────────────

    public void AddFriend(int index, string charName)
    {
        if (!TryResolveTarget(index, charName, out string login, out string targetName)) return;
        var sp = _pm[index];
        if (Contains(sp.Friends, login))
        {
            Notify(index, ServerStrings.Social_AlreadyFriend, ("Name", targetName));
            return;
        }

        // Friending someone you ignore is contradictory — the ignore would silently swallow their
        // messages while they sat in your friends list. Adding to one list clears the other.
        Remove(sp.Ignore, login);
        sp.Friends.Add(login);
        Persist(sp);
        SyncTo(index);
        NotifyOk(index, ServerStrings.Social_FriendAdded, ("Name", targetName));
    }

    public void RemoveFriend(int index, string login)
    {
        var sp = _pm[index];
        if (!Remove(sp.Friends, login)) return;
        Persist(sp);
        SyncTo(index);
        NotifyOk(index, ServerStrings.Social_FriendRemoved, ("Name", login));
    }

    // ── Ignore ───────────────────────────────────────────────────────────────────

    public void AddIgnore(int index, string charName)
    {
        if (!TryResolveTarget(index, charName, out string login, out string targetName)) return;
        var sp = _pm[index];
        if (Contains(sp.Ignore, login))
        {
            Notify(index, ServerStrings.Social_AlreadyIgnored, ("Name", targetName));
            return;
        }

        Remove(sp.Friends, login);
        sp.Ignore.Add(login);
        Persist(sp);
        SyncTo(index);
        NotifyOk(index, ServerStrings.Social_IgnoreAdded, ("Name", targetName));
        _logger.LogInformation("{Login} now ignores {Target}.", sp.Login, login);
    }

    public void RemoveIgnore(int index, string login)
    {
        var sp = _pm[index];
        if (!Remove(sp.Ignore, login)) return;
        Persist(sp);
        SyncTo(index);
        NotifyOk(index, ServerStrings.Social_IgnoreRemoved, ("Name", login));
    }

    // Resolve an ONLINE character name to its account login, rejecting self-targeting. Emits the
    // rejection to the caller, so a false return means "already handled, stop".
    private bool TryResolveTarget(int index, string charName, out string login, out string targetName)
    {
        login = "";
        targetName = "";
        int t = _pm.FindPlayerByName(charName);
        if (t == 0)
        {
            Notify(index, ServerStrings.Social_TargetOffline, ("Name", charName));
            return false;
        }
        if (t == index)
        {
            Notify(index, ServerStrings.Social_CantAddSelf);
            return false;
        }
        var target = _pm[t];
        // Per-account: another character on MY OWN account is still me.
        if (string.Equals(target.Login, _pm[index].Login, StringComparison.OrdinalIgnoreCase))
        {
            Notify(index, ServerStrings.Social_CantAddSelf);
            return false;
        }
        login = target.Login;
        targetName = target.Char.TrimmedName;
        return true;
    }

    // Persist both lists from detached snapshots through the per-login account write chain.
    private void Persist(ServerPlayer sp)
    {
        var friends = new List<string>(sp.Friends);
        var ignore = new List<string>(sp.Ignore);
        _saver.MutateAccountInBackground(sp.Login, a => { a.Friends = friends; a.Ignore = ignore; });
    }

    private static bool Contains(List<string> list, string login) =>
        list.Any(x => string.Equals(x, login, StringComparison.OrdinalIgnoreCase));

    private static bool Remove(List<string> list, string login) =>
        list.RemoveAll(x => string.Equals(x, login, StringComparison.OrdinalIgnoreCase)) > 0;
}
