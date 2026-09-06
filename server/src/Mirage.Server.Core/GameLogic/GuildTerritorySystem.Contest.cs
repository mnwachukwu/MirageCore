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

/// <summary>The live king-of-the-hill contest: capture points, the scoring tick, the participant-only
/// render state pushed to clients, and the finalization that hands the territory over.</summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    // ── Live contest (KotH) ───────────────────────────────────────────────────
    private void StartContest(MapGroupRecord group, int defenderId, List<int> challengers)
    {
        var maps = TerritoryMaps(group.Index);
        var contest = new TerritoryContest
        {
            TerritoryIndex = group.Index,
            Phase = ContestPhase.Setup,
            PhaseEndUtc = UtcNow() + Constants.TerritoryContestSetupSeconds,
            // 🔴 Load-bearing three times over, and silent when wrong: it pays the defender's per-tick
            // bonus, it hands a TIED contest back to them, and it is what separates them from the
            // challengers at settlement. Left at 0 a defended territory scores like an attacker, falls to
            // nobody on a draw, and settles as though it had no defender at all.
            DefenderGuild = defenderId,
            Points = GenerateContestPoints(maps, defenderId),
            Participants = new HashSet<int>(challengers),
            Layout = BuildTerritoryLayout(maps),
        };
        if (defenderId > 0) contest.Participants.Add(defenderId);
        _contests.Add(contest);

        // Publish the movement/spawn projection (NPC suppression + entry warnings) so those
        // systems act on the war state without a back-reference to this one (see GameWorld.ContestZones).
        _world.ContestZones.Add(new ContestZone
        {
            TerritoryIndex = group.Index,
            Name = TerritoryName(group),
            Participants = new HashSet<int>(contest.Participants),
            Maps = maps,
        });

        // NPCs vanish for the whole war state (no PvE / no income mid-war), guards excepted — they hold their
        // post. Non-participants standing in the territory are warned to leave.
        foreach (int m in maps) _spawn.DespawnMapNpcs(m, keepGuards: true);
        WarnNonParticipantsPresent(contest);

        int mins = Constants.TerritoryContestSetupSeconds / 60;
        foreach (int g in contest.Participants)
            GuildWarNotice(g, ServerStrings.GuildTerritory_SetupBegun, ("Territory", TerritoryName(group)), ("Minutes", mins));
        BroadcastContest(contest);   // reveal the flags/circles to participants at setup start
        _logger.LogInformation("Territory {Terr} contest started with {N} participant(s).", group.Index, contest.Participants.Count);
    }

    // ── Client render state (participant-only flags/circles/names + HUD) ─────────────────
    // Push one contest's live render state (points + KotH scores + phase) to every online member of a
    // participating guild. Sent at setup start + on each 5s tick; the client colors per-viewer and gates the HUD.
    private void BroadcastContest(TerritoryContest c)
    {
        var group = _world.MapGroups.GetValueOrDefault(c.TerritoryIndex);
        var packet = new TerritoryContestPacket
        {
            Active = true,
            TerritoryIndex = c.TerritoryIndex,
            TerritoryName = group is not null ? TerritoryName(group) : "",
            Phase = (int)c.Phase,
            Points = c.Points.Select(p => new ContestPointView
            {
                Label = p.Label, Map = p.Map, X = p.X, Y = p.Y, Layer = p.Layer,
                OwnerGuild = p.OwnerGuild, ChallengerGuild = p.ChallengerGuild, Meter = p.Meter,
            }).ToList(),
            // Emit a row for EVERY participant (defender + challengers), defaulting to 0 — so the client's score
            // list shows all competitors and its territory-swords check can see the full participant set.
            Scores = c.Participants.Select(g => new ContestScoreView
            {
                GuildId = g, GuildName = _guilds.GuildById(g)?.Name ?? "", Score = c.Scores.GetValueOrDefault(g),
            }).ToList(),
            Layout = c.Layout,
        };
        foreach (int g in c.Participants) _dispatcher.SendToGuild(g, packet);
    }

    // Tear the contest render down on every participant (flags/circles/HUD vanish) at war end.
    private void BroadcastContestClear(TerritoryContest c)
    {
        var packet = new TerritoryContestPacket { Active = false, TerritoryIndex = c.TerritoryIndex };
        foreach (int g in c.Participants) _dispatcher.SendToGuild(g, packet);
    }

    // Advance every live contest one 5s tick: score the running ones, then roll phases whose timer elapsed.
    private void TickContests(long now)
    {
        for (int i = _contests.Count - 1; i >= 0; i--)
        {
            var c = _contests[i];
            if (c.Phase == ContestPhase.Contest) ScoreContestTick(c);
            if (now >= c.PhaseEndUtc)
            {
                switch (c.Phase)
                {
                    case ContestPhase.Setup:
                        c.Phase = ContestPhase.Contest;
                        c.PhaseEndUtc = now + Constants.TerritoryContestSeconds;
                        foreach (int g in c.Participants) GuildWarNotice(g, ServerStrings.GuildTerritory_ContestBegun, ("Territory", TerritoryNameOf(c)));
                        break;
                    case ContestPhase.Contest:
                        FinalizeContest(c);
                        c.Phase = ContestPhase.Cooldown;
                        c.PhaseEndUtc = now + Constants.TerritoryContestCooldownSeconds;
                        int mins = Constants.TerritoryContestCooldownSeconds / 60;
                        foreach (int g in c.Participants) GuildWarNotice(g, ServerStrings.GuildTerritory_CooldownBegun, ("Territory", TerritoryNameOf(c)), ("Minutes", mins));
                        break;
                    case ContestPhase.Cooldown:
                        _contests.RemoveAt(i);
                        BroadcastContestClear(c);           // flags + HUD vanish instantly at war end
                        EndContestZone(c.TerritoryIndex);   // lift suppression/walls, then resume the map NPCs
                        if (_contests.Count == 0) AnnounceWarPublic(ServerStrings.GuildTerritory_WarNightEnd);
                        continue;                           // removed — skip the live broadcast below
                }

                // The zone carries the phase for the systems that read it without a reference here.
                if (ZoneFor(c.TerritoryIndex) is { } zone) zone.Phase = c.Phase;
            }
            BroadcastContest(c);   // push the live render state (flags/meters/scores) to participants each tick
        }
    }

    // One 5s scoring tick: push each point's meter by the majority in its radius, then award the owned points.
    private void ScoreContestTick(TerritoryContest c)
    {
        foreach (var pt in c.Points)
        {
            int majority = MajorityGuildInRadius(pt, c.Participants);
            var r = TerritoryContestFormulas.AdvanceMeter(pt.Meter, pt.OwnerGuild, pt.ChallengerGuild, majority);
            pt.Meter = r.Meter;
            pt.OwnerGuild = r.Owner;
            pt.ChallengerGuild = r.Challenger;
            int delta = TerritoryContestFormulas.ScoreDelta(TerritoryContestFormulas.ScorerThisTick(pt.Meter, pt.OwnerGuild), c.DefenderGuild);
            if (delta > 0) c.Scores[pt.OwnerGuild] = c.Scores.GetValueOrDefault(pt.OwnerGuild) + delta;
        }
    }

    private void FinalizeContest(TerritoryContest c)
    {
        if (_world.MapGroups.GetValueOrDefault(c.TerritoryIndex) is not { } group) return;
        int winner = TerritoryContestFormulas.DetermineWinner(c.Scores, c.DefenderGuild);
        var challengers = c.Participants.Where(p => p != c.DefenderGuild).ToList();
        ApplyOutcome(group, _world.TerritoryFor(c.TerritoryIndex), winner, challengers);
    }

    // The strict-plurality participant guild standing in a point's radius (0 = contested tie or empty).
    //
    // 🔴 The radius is measured in WORLD coordinates around the point's map, so it reaches ACROSS SEAMS. A
    // point near a map edge spills its zone onto the neighbor, and the tiles it spills onto hold the point
    // exactly like the ones beside it — the circle the player is standing in is the circle that scores. Asking
    // `ch.Map == pt.Map` instead would cut the zone off at a border the player cannot see, and hand anyone who
    // stepped over that border a safe tile inside the ring.
    private int MajorityGuildInRadius(ContestPoint pt, HashSet<int> participants)
    {
        var counts = new Dictionary<int, int>();
        // The 3x3 around the point's map covers the whole zone: the radius is smaller than a map, so a point
        // can spill onto an immediate neighbor and no further.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, pt.Map);
        var (ptWx, ptWy) = grid.CenterToWorld(pt.X, pt.Y);
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            int g = sp.Guild;
            if (g <= 0 || !participants.Contains(g)) continue;
            var ch = sp.Char;
            // 🔴 A body holds nothing. A dead player stays connected and lies where they fell until they
            // respawn, so without this the corpses count — and because the meter turns on a strict PLURALITY,
            // a wiped team goes on holding the point, or takes it, with nobody alive standing on it.
            if (ch.Hp <= 0) continue;
            // Two-layer world: only players on the point's own layer hold it — standing on the ground UNDER a
            // bridge-top point earns no credit; you must be up on the deck (and vice-versa).
            if (ch.Layer != pt.Layer) continue;
            if (grid.ToWorldRelative(ch.Map, ch.X, ch.Y) is not { } w) continue;   // outside the point's 3x3
            if (!WithinRadius(w.worldX, w.worldY, ptWx, ptWy)) continue;
            counts[g] = counts.GetValueOrDefault(g) + 1;
        }
        int max = 0;
        foreach (int v in counts.Values) if (v > max) max = v;
        if (max == 0) return 0;
        int top = 0, topCount = 0;
        foreach (var kv in counts) if (kv.Value == max) { top = kv.Key; topCount++; }
        return topCount == 1 ? top : 0;
    }

    private static bool WithinRadius(int x, int y, int cx, int cy) =>
        TerritoryContestFormulas.WithinRadius(x, y, cx, cy, Constants.TerritoryCapturePointRadius);

    // Every map that belongs to a territory (its MapGroup index) — the contest's arena for point placement,
    // NPC despawn, and the war-death respawn area.
    private List<int> TerritoryMaps(int territoryIndex)
    {
        var maps = new List<int>();
        for (int m = 1; m <= _world.Limits.Maps; m++)
            if (_world.Maps[m].MapGroup == territoryIndex) maps.Add(m);
        return maps;
    }

    // Place the capture points: 1 per N maps (clamped, counted over the whole territory), one to a map, spread
    // by walking distance across the whole territory — see ChooseCapturePoints, which owns the geometry. The
    // defender (if any) starts securely owning every point.
    private List<ContestPoint> GenerateContestPoints(List<int> allMaps, int defenderId)
    {
        // A point on a safe map can never be taken — no PvP resolves there — so a safe map is not eligible
        // ground. Falls back to the whole territory only when every map is safe, which leaves a contest
        // placeable rather than pointless. Safe maps stay in `allMaps` either way: they are walked THROUGH.
        var pointMaps = allMaps.Where(m => _world.MoralOf(m) != MapMoral.Safe).ToList();
        if (pointMaps.Count == 0)
        {
            _logger.LogWarning("Territory contest: every map is Safe; placing capture points on safe ground.");
            pointMaps = allMaps;
        }

        int count = TerritoryContestFormulas.PointCount(allMaps.Count);
        var picks = ChooseCapturePoints(allMaps, pointMaps, count);
        var points = new List<ContestPoint>();
        for (int i = 0; i < picks.Count; i++)
        {
            points.Add(new ContestPoint
            {
                Label = TerritoryContestFormulas.PointLabels[i], Map = picks[i].Map, X = picks[i].X, Y = picks[i].Y,
                // Auto-placed points stay on the GROUND — auto-placing onto a bridge deck is too fiddly to handle
                // reliably (user call).  The Layer field + credit gate remain so a hand-authored fringe point could
                // work later; a ground point is still held from the ground, not from a bridge above it.
                Layer = WorldLayer.Ground,
                OwnerGuild = defenderId,
                Meter = defenderId > 0 ? -Constants.TerritoryCaptureFull : 0,   // defender secure, else neutral
            });
        }
        return points;
    }


    private bool TryPickWalkable(int mapNum, out int x, out int y)
    {
        var map = _world.Maps[mapNum];
        // Counted, then walked to the winning tile. A list of every walkable tile is 65,000 entries
        // on a large map; the draw is one number either way.
        int walkable = 0;
        for (int tx = 0; tx < map.Width; tx++)
        {
            for (int ty = 0; ty < map.Height; ty++)
                if (map.Tile[tx, ty].Type == TileType.Walkable) walkable++;
        }

        if (walkable == 0)
        {
            x = y = 0;
            return false;
        }

        int pick = Rng.Next(walkable);
        for (int tx = 0; tx < map.Width; tx++)
        {
            for (int ty = 0; ty < map.Height; ty++)
            {
                if (map.Tile[tx, ty].Type != TileType.Walkable) continue;
                if (pick-- > 0) continue;
                x = tx;
                y = ty;
                return true;
            }
        }

        x = y = 0;
        return false;
    }

    private bool HasActiveContest(int territoryIndex)
    {
        foreach (var c in _contests) if (c.TerritoryIndex == territoryIndex) return true;
        return false;
    }
}
