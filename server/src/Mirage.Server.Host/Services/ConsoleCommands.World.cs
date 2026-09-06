using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Host.Services;

/// <summary>
/// Admin commands that act on the world rather than on the caller. The rest of the in-game set
/// (/warpto, /setsprite, /loc) is relative to a character the console does not have.
///
/// <para>/respawn and /mapreport take a map number here; in-game they default to the caller's map.</para>
/// </summary>
public sealed partial class ConsoleCommands
{
    private void CmdTimeOfDay(string args)
    {
        if (!TryParseEnum<TimePhase>(args, ServerStrings.Console_TodUsage, out var phase)) return;
        _tod.JumpToPhase(phase, ConsoleOperatorName);
        _gameLoop.PersistEnvironmentNow();
        Write(ServerStrings.Console_TodSet, ("Phase", phase));
        _logger.LogInformation("Console jumped time of day to {Phase}.", phase);
    }

    // ── /weather ─────────────────────────────────────────────────────────────

    private void CmdWeather(string args)
    {
        if (!TryParseEnum<WeatherType>(args, ServerStrings.Console_WeatherUsage, out var weather)) return;
        _weather.SetWeatherAdmin(weather, ConsoleOperatorName);
        _gameLoop.PersistEnvironmentNow();
        Write(ServerStrings.Console_WeatherSet, ("Weather", weather));
        _logger.LogInformation("Console set weather to {Weather}.", weather);
    }

    // ── /motd ────────────────────────────────────────────────────────────────

    /// <summary>Set the message of the day, or CLEAR it by passing nothing.
    ///
    /// <para>Clearing is silent. The set announcement quotes the new text to every player, and the
    /// clear has no text to quote — broadcasting it would put an empty "Message of the Day changed to:"
    /// in front of everyone, which says nothing and looks broken. Players find out the next time they
    /// would have been shown one.</para>
    ///
    /// <para>A blank argument is therefore an INSTRUCTION rather than a mistake, which is why there is
    /// no usage line: an empty box on the Commands tab means "no message", and refusing it would leave
    /// no way to take one down.</para></summary>
    private void CmdMotd(string args)
    {
        bool clearing = string.IsNullOrWhiteSpace(args);
        string motd = clearing ? "" : args;

        _world.Motd = motd;
        _bg.Run(_persistence.SaveMotdAsync(motd), nameof(IPersistenceService.SaveMotdAsync));

        if (!clearing)
            _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_MotdChanged,
                new ChatMetadata(GameColor.BrightCyan, ChatChannel.Notice), ("Motd", motd));

        Write(clearing ? ServerStrings.Console_MotdCleared : ServerStrings.Console_MotdSet, ("Motd", motd));
        _logger.LogInformation("Console {Action} the Message of the Day.", clearing ? "cleared" : "changed");
    }

    // ── /setaccess ───────────────────────────────────────────────────────────

    private void CmdSetAccess(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Enum.TryParse takes the NAME or the number, so "Creator" and "4" both work — names to match
        // /tod and /weather, and numbers because an operator with the level to hand should not have to
        // look up its name.
        if (parts.Length != 2 ||
            !Enum.TryParse(parts[0], ignoreCase: true, out AdminLevel level) || !Enum.IsDefined(level))
        {
            System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_SetAccessUsage,
                ("Values", string.Join(" | ", Enum.GetNames<AdminLevel>()))));
            return;
        }
        int slot = _pm.FindPlayerByName(parts[1]);
        if (slot <= 0)
        {
            Write(ServerStrings.Console_PlayerNotOnline, ("Name", parts[1]));
            return;
        }

        string login = _pm[slot].Login;
        string charName = _pm[slot].Char.Name.Trim();
        if (_pm[slot].Char.Access == AdminLevel.Player && level > AdminLevel.Player)
        {
            _dispatcher.SendLocalizedChatToAll(ServerStrings.AdminCommand_PlayerGrantedAccess,
                new ChatMetadata(GameColor.BrightBlue, ChatChannel.Notice), ("Target", charName));
        }

        // Access is per-account, so every online character on it updates too.
        _saver.MutateAccountInBackground(login, a => a.Access = level);
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (!_pm[i].IsPlaying || !string.Equals(_pm[i].Login, login, StringComparison.OrdinalIgnoreCase)) continue;
            _pm[i].Char.Access = level;
            _dispatcher.SendToAll(PacketBuilder.PlayerData(i, _pm[i].Char, _pm[i].Char.Map,
                _pm[i].PkGraceUntilUtc, _pm[i].AggressorUntilUtcNow));
        }
        Write(ServerStrings.Console_AccessSet, ("Name", charName), ("Level", level));
        _logger.LogInformation("Console set {Login}'s account access to {Level}.", login, level);
    }

    // ── /respawn <map> and /mapreport ────────────────────────────────────────

    private void CmdRespawn(string args)
    {
        if (!TryParseMap(args, ServerStrings.Console_RespawnUsage, _world.Limits.Maps, out int mapNum)) return;
        _items.ClearMapItems(mapNum);
        _items.SpawnMapItems(mapNum);
        for (int i = 1; i <= Constants.MaxMapNpcs; i++) _spawn.SpawnNpc(i, mapNum);
        Write(ServerStrings.Console_MapRespawned, ("Map", mapNum));
        _logger.LogInformation("Console respawned map #{Map}.", mapNum);
    }

    private void CmdMapReport()
    {
        // The free-map ranges, same as the in-game report: a map with no name has never been authored.
        var sb = new System.Text.StringBuilder();
        int runStart = 0;
        for (int i = 1; i <= _world.Limits.Maps + 1; i++)
        {
            bool free = i <= _world.Limits.Maps && string.IsNullOrWhiteSpace(_world.Maps[i].Name);
            if (free && runStart == 0) runStart = i;
            else if (!free && runStart != 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(runStart == i - 1 ? $"{runStart}" : $"{runStart}-{i - 1}");
                runStart = 0;
            }
        }
        Write(ServerStrings.Console_MapReport, ("Ranges", sb.Length > 0 ? sb.ToString() : "-"));
    }

    // ── Territory war lifecycle ──────────────────────────────────────────────

    private void CmdStartWar()
    {
        int count = _territory.DebugStartWarNight();
        Write(ServerStrings.Console_WarStarted, ("Count", count));
        _logger.LogInformation("Console started war night ({Count} contest(s)).", count);
    }

    private void CmdAdvanceWar()
    {
        bool advanced = _territory.DebugAdvanceWar();
        Write(advanced ? ServerStrings.Console_WarAdvanced : ServerStrings.Console_NoWarInProgress);
        if (advanced) _logger.LogInformation("Console advanced the war-night phase.");
    }

    private void CmdEndWar()
    {
        int count = _territory.DebugEndWar();
        Write(ServerStrings.Console_WarEnded, ("Count", count));
        _logger.LogInformation("Console ended war night ({Count} contest(s)).", count);
    }

    // ── /guildreset ──────────────────────────────────────────────────────────

    private void CmdGuildReset(string args)
    {
        // Defaults to the daily settlement, matching the in-game command: it is the one that runs on its
        // own every night, so it is the one an operator most often wants to force.
        string arg = string.IsNullOrWhiteSpace(args) ? "day" : args.Trim();
        if (!Enum.TryParse<SettlementScope>(arg, ignoreCase: true, out var scope) ||
            !Enum.IsDefined(typeof(SettlementScope), scope))
        {
            Write(ServerStrings.Console_GuildResetUsage);
            return;
        }
        _guildSchedule.RunManualSettlement(scope);
        Write(ServerStrings.Console_GuildReset, ("Scope", scope));
        _logger.LogInformation("Console ran a manual {Scope} settlement.", scope);
    }

    // ── Shared parsing ───────────────────────────────────────────────────────

    /// <summary>Usage lines list valid values from the enum itself, so the console can never offer one
    /// the server would refuse.</summary>
    private static bool TryParseEnum<T>(string args, string usageKey, out T value) where T : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(args) &&
            Enum.TryParse(args.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value))
        {
            return true;
        }
        System.Console.WriteLine(ServerStrings.Format(usageKey, ("Values", string.Join(" | ", Enum.GetNames<T>()))));
        value = default;
        return false;
    }

    private static bool TryParseMap(string args, string usageKey, int maxMaps, out int mapNum)
    {
        if (int.TryParse(args.Trim(), out mapNum) && mapNum >= 1 && mapNum <= maxMaps) return true;
        System.Console.WriteLine(ServerStrings.Format(usageKey, ("Max", maxMaps)));
        mapNum = 0;
        return false;
    }

    private static void Write(string key, params (string Key, object? Value)[] args) =>
        System.Console.WriteLine(args.Length == 0 ? ServerStrings.Get(key) : ServerStrings.Format(key, args));
}
