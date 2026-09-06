using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The contest zone as a place — NPC suppression, the combat-integration queries CombatSystem asks
/// of it, and the officer request queue.</summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    // ── Setup zone lifecycle (NPC despawn/resume) ─────────────────────────────────
    // The GameWorld projection for a live contest (NPC suppression + entry warnings).
    private ContestZone? ZoneFor(int territoryIndex)
    {
        foreach (var z in _world.ContestZones) if (z.TerritoryIndex == territoryIndex) return z;
        return null;
    }

    // Cooldown end: drop the projection (lifts NPC-spawn suppression), then resume the map
    // NPCs now that the whole war state is over (NPCs return only after the cooldown).
    private void EndContestZone(int territoryIndex)
    {
        var maps = ZoneFor(territoryIndex)?.Maps ?? TerritoryMaps(territoryIndex);
        _world.ContestZones.RemoveAll(z => z.TerritoryIndex == territoryIndex);
        foreach (int m in maps) _spawn.SpawnMapNpcs(m);
    }

    // Warn non-participants standing in the territory when setup begins — a courtesy so they can leave.
    // The System channel (not War) so a non-participant with the War channel muted still sees it.
    private void WarnNonParticipantsPresent(TerritoryContest c)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying || c.Participants.Contains(sp.Guild)) continue;
            if (!IsInTerritory(i, c.TerritoryIndex)) continue;
            _dispatcher.SendLocalizedChatTo(i, ServerStrings.GuildTerritory_NonParticipantWarning,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System), ("Territory", TerritoryNameOf(c)));
        }
    }

    // ── Combat-integration queries (called by CombatSystem) ───────────────────
    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are opponents in a live contest — both
    /// participants of the same contest, in different guilds, both standing in the territory, during the
    /// CONTEST phase. The war-combat ruleset (durability-only) then applies to their fight.</summary>
    public bool AreContestOpponents(int a, int b)
    {
        int ga = _pm[a].Guild, gb = _pm[b].Guild;
        if (ga <= 0 || gb <= 0 || ga == gb) return false;
        foreach (var c in _contests)
        {
            if (c.Phase != ContestPhase.Contest) continue;
            if (!c.Participants.Contains(ga) || !c.Participants.Contains(gb)) continue;
            if (IsInTerritory(a, c.TerritoryIndex) && IsInTerritory(b, c.TerritoryIndex)) return true;
        }
        return false;
    }

    /// <summary>True if both are participants of the same contest standing in its territory during SETUP or
    /// COOLDOWN — PvP between participants is suppressed in those phases.</summary>
    /// <summary>The PHASE whose truce is stopping <paramref name="a"/> from attacking
    /// <paramref name="b"/>, or null when nothing is. Returns the phase rather than a bare yes, because
    /// the two that suppress mean opposite things to the player: Setup is a truce that is about to lift,
    /// Cooldown is one that has closed behind them.</summary>
    public ContestPhase? ContestTrucePhase(int a, int b)
    {
        int ga = _pm[a].Guild, gb = _pm[b].Guild;
        if (ga <= 0 || gb <= 0) return null;
        foreach (var c in _contests)
        {
            if (c.Phase == ContestPhase.Contest) continue;   // combat is allowed during the contest itself
            if (!c.Participants.Contains(ga) || !c.Participants.Contains(gb)) continue;
            if (IsInTerritory(a, c.TerritoryIndex) && IsInTerritory(b, c.TerritoryIndex)) return c.Phase;
        }
        return null;
    }

    /// <summary>The territory index of the live contest this player is participating in AND standing in, else
    /// 0 — used to mark a territory war-death (its respawn area + readout).</summary>
    public int ContestTerritoryOf(int playerIndex)
    {
        int g = _pm[playerIndex].Guild;
        if (g <= 0) return 0;
        foreach (var c in _contests)
            if (c.Participants.Contains(g) && IsInTerritory(playerIndex, c.TerritoryIndex)) return c.TerritoryIndex;
        return 0;
    }

    /// <summary>True if this player is a participant of a live contest in its CONTEST phase AND standing in the
    /// territory — a war combatant with offensive license there, so striking anyone (a non-participant included)
    /// carries no aggressor/attack penalty. Scoped to the contest window: the setup/cooldown truce
    /// already bars participant-vs-participant PvP, and the no-penalty license does not apply then.</summary>
    public bool IsActiveContestParticipant(int playerIndex)
    {
        int g = _pm[playerIndex].Guild;
        if (g <= 0) return false;
        foreach (var c in _contests)
        {
            if (c.Phase == ContestPhase.Contest && c.Participants.Contains(g) && IsInTerritory(playerIndex, c.TerritoryIndex))
                return true;
        }

        return false;
    }

    /// <summary>Pick a random walkable tile in a territory's maps — the war-death respawn area.
    /// Returns false if the territory has no walkable tile.</summary>
    public bool TerritoryRespawnTile(int territoryIndex, out int map, out int x, out int y)
    {
        map = x = y = 0;
        var maps = TerritoryMaps(territoryIndex);
        for (int attempt = 0; attempt < maps.Count; attempt++)
        {
            int mapNum = maps[Rng.Next(maps.Count)];
            if (TryPickWalkable(mapNum, out x, out y))
            {
                map = mapNum;
                return true;
            }
        }
        return false;
    }

    private bool IsInTerritory(int playerIndex, int territoryIndex)
    {
        var sp = _pm[playerIndex];
        if (!sp.IsPlaying) return false;
        int m = sp.Char.Map;
        return m >= 1 && m <= _world.Limits.Maps && _world.Maps[m].MapGroup == territoryIndex;
    }

    private string TerritoryNameOf(TerritoryContest c) =>
        _world.MapGroups.GetValueOrDefault(c.TerritoryIndex) is { } g ? TerritoryName(g) : "";

    private void AnnounceResults(MapGroupRecord group, int oldOwner, int winner,
                                 List<int> challengers, bool retained, bool hadChallengers)
    {
        string terrName = TerritoryName(group);

        if (winner > 0 && winner != oldOwner)                       // captured (lone claimant on unclaimed/abandoned)
            GuildWarNotice(winner, ServerStrings.GuildTerritory_ResultWon, ("Territory", terrName));
        else if (retained && hadChallengers)                        // defended against challengers
            GuildWarNotice(winner, ServerStrings.GuildTerritory_ResultDefended, ("Territory", terrName));

        if (oldOwner > 0 && winner == 0)                            // abandoned territory fell unclaimed
            GuildWarNotice(oldOwner, ServerStrings.GuildTerritory_ResultAbandonedLost, ("Territory", terrName));

        foreach (int c in challengers)
        {
            if (c != winner)                                        // a losing challenger
                GuildWarNotice(c, ServerStrings.GuildTerritory_ResultLost, ("Territory", terrName));
        }
    }

    // ── Officer-request queue (mirrors GuildWarSystem's) ──────────────────────
    private void QueueChallengeRequest(GuildRecord guild, MapGroupRecord group, ServerPlayer requester, int requesterIndex)
    {
        var result = GuildWarFormulas.TryQueueRequest(guild, GuildWarRequestKind.TerritoryChallenge, group.Index,
            TerritoryName(group), requester.Login, requester.Char.TrimmedName, UtcNow(), Constants.GuildWarMaxPendingRequests);
        switch (result)
        {
            case WarRequestQueueResult.AlreadyPending:
                Notify(requesterIndex, ServerStrings.GuildWar_RequestAlreadyPending);
                return;
            case WarRequestQueueResult.Full:
                Notify(requesterIndex, ServerStrings.GuildWar_RequestsFull);
                return;
        }
        _guilds.SaveGuild(guild);
        _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, ServerStrings.GuildTerritory_OfficerReqChallenge,
            new ChatMetadata(GameColor.BrightCyan, ChatChannel.GuildOfficer),
            ("Name", requester.Char.TrimmedName), ("Territory", TerritoryName(group)));
        NotifyOk(requesterIndex, ServerStrings.GuildWar_RequestSent);
    }

    private static string TerritoryName(MapGroupRecord g) =>
        string.IsNullOrWhiteSpace(g.DisplayName) ? g.Name : g.DisplayName.Trim();

    private string OwnerName(TerritoryRecord t) => _guilds.GuildById(t.ControllingGuild)?.Name ?? "";

    private void SaveTerritory(TerritoryRecord terr) =>
        _bg.Run(_persistence.SaveTerritoryAsync(terr.MapGroup, terr.Clone()), nameof(IPersistenceService.SaveTerritoryAsync));

    private void AnnounceWarPublic(string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.BrightRed, ChatChannel.War), args);

    // Per-guild territory results on the private Guild War channel.
    private void GuildWarNotice(int guildId, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToGuild(guildId, key, new ChatMetadata(GameColor.BrightCyan, ChatChannel.GuildWar), args);
}
