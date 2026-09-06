using Mirage.Shared;

namespace Mirage.Server.Core.Players;

public sealed class PlayerManager
{
    // 1-based; indices 1..Slots; index 0 unused
    private readonly ServerPlayer[] _players;

    /// <summary>This server's configured limit, and the highest valid slot. NOT
    /// <c>Slots</c>, which is only the protocol ceiling a client allocates for — every
    /// scan below walks THIS, so a 20-player world does no work for the 480 slots it will never use.</summary>
    public int Slots { get; }

    // The connected slots, unordered, packed into the front of the array. _onlineAt maps a slot to its
    // position so a disconnect is a swap-with-last rather than a scan.
    private readonly int[] _online;
    private readonly int[] _onlineAt;
    private int _onlineCount;

    public PlayerManager(Configuration.ServerConfig? config = null)
    {
        Slots = (config ?? Configuration.ServerConfig.Default).MaxPlayers;
        _players = new ServerPlayer[Slots + 1];
        _online = new int[Slots + 1];
        _onlineAt = new int[Slots + 1];
        for (int i = 1; i <= Slots; i++)
        {
            _players[i] = new ServerPlayer
            {
                DamageByPlayer = new int[Slots + 1],
                Slot = i,
                ConnectionChanged = OnConnectionChanged,
            };
        }
    }

    /// <summary>
    /// The slots that currently hold a connection. Every broadcast walks THIS instead of 1..Slots, so a
    /// server sized for 500 does not pay for 500 when twelve people are on.
    ///
    /// <para>Unordered, and it is a superset of who is in the world — a caller still applies its own
    /// predicate (in-game, admin, guild). The only thing it is NOT a superset of is combat ghosts, which
    /// have no socket left, so nothing can be sent to them either way.</para>
    ///
    /// <para><b>Safe to walk while it changes.</b> A span captures the length at the call, and a removal
    /// swaps the last entry down; the worst a mid-walk disconnect can do is show a slot that has just left
    /// — which every call site filters anyway — or skip one that was already leaving. Both are what the
    /// old array scan did too.</para>
    /// </summary>
    public ReadOnlySpan<int> Online => _online.AsSpan(0, _onlineCount);

    /// <summary>Maintained by <see cref="ServerPlayer.IsConnected"/>, which is the only writer.</summary>
    private void OnConnectionChanged(int slot, bool connected)
    {
        if (slot < 1 || slot > Slots) return;
        if (connected)
        {
            _onlineAt[slot] = _onlineCount;
            _online[_onlineCount++] = slot;
            return;
        }

        int at = _onlineAt[slot];
        int last = _online[--_onlineCount];
        _online[at] = last;
        _onlineAt[last] = at;
    }

    public ServerPlayer this[int index] => _players[index];

    /// <summary>Whether <paramref name="slot"/> indexes a real slot ON THIS SERVER.
    ///
    /// <para>Use this, not <c>SlotValidation.IsValidPlayerSlot</c>, anywhere a slot number is about to
    /// index a player. That one bounds by the PROTOCOL ceiling, which is what a client allocates for and
    /// is far larger than a typical server's array — a client-supplied slot of 400 would pass it and then
    /// throw indexing a 20-slot world.</para></summary>
    public bool IsValidSlot(int slot) => slot >= 1 && slot <= Slots;

    /// <summary>Someone joined or left. Raised ON THE GAME THREAD by <see cref="GameLogic.JoinLeaveSystem"/>
    /// so an operator's roster does not have to wait for a poll. Nothing in the game subscribes; this
    /// exists for the host's status broadcaster, which is why it is an event rather than a dependency.</summary>
    public event Action? RosterChanged;

    internal void NotifyRosterChanged() => RosterChanged?.Invoke();

    /// <summary>Flag a player so <see cref="GameLogic.GameLoop"/> persists it at the end of the current
    /// game tick. Call on any state change a player could otherwise undo by hard-disconnecting before
    /// the 60 s autosave — item drop/pickup, durability break, death, level-up, inventory sort. Cheap
    /// and idempotent.</summary>
    public void MarkDirty(int index)
    {
        if (index >= 1 && index <= Slots)
            _players[index].SaveDirty = true;
    }

    /// <summary>Returns the index of the first disconnected, non-ghost slot, or 0 if none is available.
    ///
    /// <para><paramref name="keepFree"/> slots are held back: the search still returns the lowest free
    /// slot, but only while MORE than that many are free. This is how a server keeps room for its
    /// moderators — staff pass 0 and get any free slot, everyone else passes the reserved count. Counting
    /// and finding in one pass, because both walk the same array.</para></summary>
    public int FindOpenSlot(int keepFree = 0)
    {
        int free = 0, first = 0;
        for (int i = 1; i <= Slots; i++)
        {
            if (_players[i].IsConnected || _players[i].IsGhost) continue;
            if (first == 0) first = i;
            free++;
        }
        return free > keepFree ? first : 0;
    }

    /// <summary>
    /// Returns the slot index of a combat ghost whose account matches <paramref name="login"/>, or 0 if none.
    /// </summary>
    public int FindGhostByLogin(string login)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (_players[i].IsGhost &&
                string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>Index (1..MaxPlayers) of the CONNECTED, in-game player on account
    /// <paramref name="login"/>, or 0 if that account isn't currently playing.</summary>
    public int FindOnlineByLogin(string login)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (_players[i].IsPlaying &&
                string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns the index (1..MaxPlayers) of the in-game player whose name equals
    /// <paramref name="name"/> (case-insensitive, whole-name match), or 0 if not found.
    /// </summary>
    public int FindPlayerByName(string name)
    {
        string target = name.TrimEnd();
        for (int i = 1; i <= Slots; i++)
        {
            if (!_players[i].IsPlaying) continue;
            string pName = _players[i].Char.TrimmedName;
            if (string.Equals(pName, target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    // The three below only ever cared about connected slots, so they walk the online set for the same
    // reason the broadcasts do. FindOpenSlot and FindGhostByLogin cannot: one looks for the ABSENT and the
    // other for slots whose connection has already gone.

    public bool IsMultiAccount(string login, int excludeIndex)
    {
        foreach (int i in Online)
        {
            if (i == excludeIndex) continue;
            if (string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public int TotalOnline
    {
        get
        {
            int count = 0;
            foreach (int i in Online)
                if (_players[i].InGame) count++;
            return count;
        }
    }

    public int GetTotalMapPlayers(int mapNum)
    {
        int count = 0;
        for (int i = 1; i <= Slots; i++)
            if (_players[i].IsPlaying && _players[i].Char.Map == mapNum) count++;
        return count;
    }
}
