using Mirage.Shared.Localization;

namespace Mirage.Server.Shell.Localization;

/// <summary>
/// Every string the management shell shows, and the runtime accessor for them. Same shape as the
/// editor's <c>EditorStrings</c> and the server's <c>ServerStrings</c>: keys declared with the
/// <c>nameof</c> trick so a const's value always equals its JSON key, and renaming one breaks the
/// lookup on purpose rather than silently returning a placeholder.
///
/// <para><b>There is one language setting, not two.</b> The shell reads the same <c>"Language"</c> from
/// serverconfig.json that the server reads for its console output and logs — this window is the operator's
/// view of that server, so it speaks the operator's language by definition. Per-player messages are
/// unaffected; those resolve off each session's own locale.</para>
/// </summary>
public static class ShellStrings
{
    // ── Keys ──────────────────────────────────────────────────────────────────

    public const string LanguageName = nameof(LanguageName);

    public const string Window_Title = nameof(Window_Title);                     // "{GameName} Server"
    public const string Tab_Server = nameof(Tab_Server);
    public const string Commands_Server = nameof(Commands_Server);
    public const string Commands_Update = nameof(Commands_Update);
    public const string Commands_Credits = nameof(Commands_Credits);
    public const string Commands_Shutdown = nameof(Commands_Shutdown);
    public const string Help_Menu = nameof(Help_Menu);
    public const string Help_About = nameof(Help_About);
    public const string About_Title = nameof(About_Title);
    public const string About_Version = nameof(About_Version);
    public const string About_CreatorDeveloper = nameof(About_CreatorDeveloper);
    public const string About_Close = nameof(About_Close);
    public const string About_UpdateAvailable = nameof(About_UpdateAvailable);
    public const string Tab_Console = nameof(Tab_Console);

    // ── The server dashboard ──────────────────────────────────────────────────
    public const string Server_Blurb = nameof(Server_Blurb);
    public const string Server_Offline = nameof(Server_Offline);
    public const string Server_World = nameof(Server_World);
    public const string Server_TimeOfDay = nameof(Server_TimeOfDay);
    public const string Server_Weather = nameof(Server_Weather);
    public const string Server_Motd = nameof(Server_Motd);
    public const string Server_MotdHint = nameof(Server_MotdHint);
    public const string Server_Apply = nameof(Server_Apply);
    public const string Server_Uptime = nameof(Server_Uptime);
    public const string Server_Port = nameof(Server_Port);
    public const string Server_Operators = nameof(Server_Operators);
    public const string Server_Players = nameof(Server_Players);
    public const string Server_PlayersEmpty = nameof(Server_PlayersEmpty);
    public const string Server_Editors = nameof(Server_Editors);
    public const string Server_EditorsEmpty = nameof(Server_EditorsEmpty);
    public const string Server_ColSlot = nameof(Server_ColSlot);
    public const string Server_ColHolding = nameof(Server_ColHolding);
    public const string Server_DisconnectEditor = nameof(Server_DisconnectEditor);
    public const string Server_ColName = nameof(Server_ColName);
    public const string Server_ColAccount = nameof(Server_ColAccount);
    public const string Server_ColLevel = nameof(Server_ColLevel);
    public const string Server_ColClass = nameof(Server_ColClass);
    public const string Server_ColMap = nameof(Server_ColMap);
    public const string Server_ColAccess = nameof(Server_ColAccess);
    public const string Server_Kick = nameof(Server_Kick);
    public const string Server_Mute = nameof(Server_Mute);
    public const string Server_Ban = nameof(Server_Ban);
    public const string Server_Minutes = nameof(Server_Minutes);

    // ── Moderation ────────────────────────────────────────────────────────────
    // Its own tab rather than a strip on the dashboard: the dashboard is who is online NOW, and every
    // row here is about somebody who is not.
    public const string Tab_Moderation = nameof(Tab_Moderation);
    public const string Mod_Blurb = nameof(Mod_Blurb);
    public const string Mod_Refresh = nameof(Mod_Refresh);
    public const string Mod_Bans = nameof(Mod_Bans);
    public const string Mod_BansEmpty = nameof(Mod_BansEmpty);
    public const string Mod_HardwareBans = nameof(Mod_HardwareBans);
    public const string Mod_HardwareBansEmpty = nameof(Mod_HardwareBansEmpty);
    // What a match DOES. Two sentences rather than the config's bare enum name, because the rows look
    // the same under both and only this says whether those people are being refused or watched.
    public const string Mod_HardwareBanModeSignal = nameof(Mod_HardwareBanModeSignal);
    public const string Mod_HardwareBanModeBlock = nameof(Mod_HardwareBanModeBlock);
    public const string Mod_Penalties = nameof(Mod_Penalties);
    public const string Mod_PenaltiesEmpty = nameof(Mod_PenaltiesEmpty);
    public const string Mod_ColAccount = nameof(Mod_ColAccount);
    public const string Mod_ColReason = nameof(Mod_ColReason);
    public const string Mod_ColApplied = nameof(Mod_ColApplied);
    public const string Mod_ColKind = nameof(Mod_ColKind);
    public const string Mod_ColRemaining = nameof(Mod_ColRemaining);
    public const string Mod_ColWhere = nameof(Mod_ColWhere);
    public const string Mod_Lift = nameof(Mod_Lift);
    public const string Mod_MinutesLeft = nameof(Mod_MinutesLeft);
    public const string Mod_Unknown = nameof(Mod_Unknown);
    public const string Mod_Offline = nameof(Mod_Offline);
    public const string Mod_Scanned = nameof(Mod_Scanned);
    public const string Mod_NotLoaded = nameof(Mod_NotLoaded);

    // ── The load benchmark ────────────────────────────────────────────────────
    // Opened from the dashboard rather than given a tab of its own: it is a measurement an operator takes
    // once, not a place they work.
    public const string Bench_Open = nameof(Bench_Open);
    public const string Bench_Title = nameof(Bench_Title);
    public const string Bench_Blurb = nameof(Bench_Blurb);
    public const string Bench_Caveat = nameof(Bench_Caveat);
    public const string Bench_Warning = nameof(Bench_Warning);
    public const string Bench_Target = nameof(Bench_Target);
    public const string Bench_Run = nameof(Bench_Run);
    public const string Bench_Stop = nameof(Bench_Stop);
    public const string Bench_Close = nameof(Bench_Close);
    public const string Bench_Preparing = nameof(Bench_Preparing);
    public const string Bench_Booting = nameof(Bench_Booting);
    public const string Bench_Joining = nameof(Bench_Joining);
    public const string Bench_Measuring = nameof(Bench_Measuring);
    public const string Bench_Finishing = nameof(Bench_Finishing);
    public const string Bench_Progress = nameof(Bench_Progress);           // "{Players}" "{Target}"
    public const string Bench_Bands = nameof(Bench_Bands);
    public const string Bench_BandLabel = nameof(Bench_BandLabel);         // "{Percent}"
    public const string Bench_AtLeast = nameof(Bench_AtLeast);             // "{Players}"
    public const string Bench_Steps = nameof(Bench_Steps);
    public const string Bench_ColPlayers = nameof(Bench_ColPlayers);
    public const string Bench_ColGameThread = nameof(Bench_ColGameThread);
    public const string Bench_ColCpu = nameof(Bench_ColCpu);
    public const string Bench_ColMemory = nameof(Bench_ColMemory);
    public const string Bench_ColOverruns = nameof(Bench_ColOverruns);
    public const string Bench_ColPackets = nameof(Bench_ColPackets);
    public const string Bench_Reached = nameof(Bench_Reached);             // "{Peak}"
    public const string Bench_Saturated = nameof(Bench_Saturated);         // "{Peak}"
    public const string Bench_JoinsFailed = nameof(Bench_JoinsFailed);     // "{Peak}" "{Reason}"
    public const string Bench_Dropped = nameof(Bench_Dropped);             // "{Peak}"
    public const string Bench_Cancelled = nameof(Bench_Cancelled);
    public const string Bench_Baseline = nameof(Bench_Baseline);           // "{Memory}"
    public const string Bench_PerPlayer = nameof(Bench_PerPlayer);         // "{Memory}"
    public const string Bench_Cores = nameof(Bench_Cores);                 // "{Cores}"
    public const string Bench_MissedBeats = nameof(Bench_MissedBeats);     // "{Count}"
    public const string Bench_Apply = nameof(Bench_Apply);
    public const string Bench_Applied = nameof(Bench_Applied);             // "{Players}"
    public const string Bench_Failed = nameof(Bench_Failed);               // "{Error}"
    public const string Bench_Unavailable = nameof(Bench_Unavailable);

    public const string Tab_Configuration = nameof(Tab_Configuration);
    public const string Tab_Commands = nameof(Tab_Commands);

    public const string Commands_Blurb = nameof(Commands_Blurb);
    public const string Commands_Players = nameof(Commands_Players);
    public const string Commands_World = nameof(Commands_World);
    public const string Commands_Guilds = nameof(Commands_Guilds);
    public const string Commands_Run = nameof(Commands_Run);
    public const string Commands_Confirm = nameof(Commands_Confirm);
    public const string Commands_Cancel = nameof(Commands_Cancel);

    public const string Commands_Who = nameof(Commands_Who);
    public const string Commands_Moderation = nameof(Commands_Moderation);
    public const string Commands_Unban = nameof(Commands_Unban);
    public const string Commands_Unkick = nameof(Commands_Unkick);
    public const string Commands_Unmute = nameof(Commands_Unmute);
    public const string Commands_HwBan = nameof(Commands_HwBan);
    public const string Commands_HwUnban = nameof(Commands_HwUnban);
    public const string Commands_Editors = nameof(Commands_Editors);
    public const string Commands_KickEditor = nameof(Commands_KickEditor);
    public const string Commands_Remote = nameof(Commands_Remote);
    public const string Commands_Management = nameof(Commands_Management);
    public const string Commands_Kick = nameof(Commands_Kick);
    public const string Commands_Mute = nameof(Commands_Mute);
    public const string Commands_Ban = nameof(Commands_Ban);
    public const string Commands_SetAccess = nameof(Commands_SetAccess);
    public const string Commands_RefreshBanList = nameof(Commands_RefreshBanList);
    public const string Commands_Tod = nameof(Commands_Tod);
    public const string Commands_Weather = nameof(Commands_Weather);
    public const string Commands_Motd = nameof(Commands_Motd);
    public const string Commands_Respawn = nameof(Commands_Respawn);
    public const string Commands_MapReport = nameof(Commands_MapReport);
    public const string Commands_StartWar = nameof(Commands_StartWar);
    public const string Commands_AdvanceWar = nameof(Commands_AdvanceWar);
    public const string Commands_EndWar = nameof(Commands_EndWar);
    public const string Commands_GuildReset = nameof(Commands_GuildReset);

    public const string Action_Start = nameof(Action_Start);
    public const string Action_Stop = nameof(Action_Stop);
    public const string Action_Send = nameof(Action_Send);
    public const string Action_Save = nameof(Action_Save);
    public const string Action_Revert = nameof(Action_Revert);
    public const string Action_Attach = nameof(Action_Attach);
    public const string Action_Detach = nameof(Action_Detach);
    public const string Action_Generate = nameof(Action_Generate);

    public const string State_Stopped = nameof(State_Stopped);
    public const string State_Running = nameof(State_Running);
    public const string State_Stopping = nameof(State_Stopping);
    public const string State_Detached = nameof(State_Detached);
    public const string State_Attached = nameof(State_Attached);

    public const string Console_CommandHint = nameof(Console_CommandHint);       // placeholder in the command box
    public const string Console_ServerNotFound = nameof(Console_ServerNotFound); // "{Path}"
    public const string Console_StoppingNotice = nameof(Console_StoppingNotice); // "{Seconds}"
    public const string Console_Attaching = nameof(Console_Attaching);           // "{Host}" "{Port}"
    public const string Console_Rejected = nameof(Console_Rejected);
    public const string Console_Unreachable = nameof(Console_Unreachable);       // "{Host}" "{Port}"
    public const string Console_IdentityChanged = nameof(Console_IdentityChanged); // "{Host}" "{Port}"
    // "{Host}" "{Port}" "{Expected}" "{Actual}"
    public const string Console_IdentityChangedDetail = nameof(Console_IdentityChangedDetail);
    public const string Console_ConnectionLost = nameof(Console_ConnectionLost);
    public const string Console_ShutdownBlocked = nameof(Console_ShutdownBlocked);

    // ── The two halves of the Configuration tab ───────────────────────────────
    public const string Config_WindowGroup = nameof(Config_WindowGroup);
    public const string Config_WindowGroupHint = nameof(Config_WindowGroupHint);
    public const string Config_ServerGroup = nameof(Config_ServerGroup);
    public const string Config_ServerGroupHint = nameof(Config_ServerGroupHint);
    public const string Config_SaveScope = nameof(Config_SaveScope);       // "{Group}"

    // ── Connection ────────────────────────────────────────────────────────────
    public const string Connection_Heading = nameof(Connection_Heading);
    public const string Connection_Blurb = nameof(Connection_Blurb);
    public const string Connection_Launch = nameof(Connection_Launch);
    public const string Connection_LaunchHint = nameof(Connection_LaunchHint);
    public const string Connection_Attach = nameof(Connection_Attach);
    public const string Connection_AttachHint = nameof(Connection_AttachHint);
    public const string Connection_Host = nameof(Connection_Host);
    public const string Connection_KnownServers = nameof(Connection_KnownServers);
    public const string Connection_ServerName = nameof(Connection_ServerName);
    public const string Connection_ForgetServer = nameof(Connection_ForgetServer);
    public const string Connection_AddServer = nameof(Connection_AddServer);
    public const string Connection_ClearPin = nameof(Connection_ClearPin);
    public const string Connection_TrustNewCertificate = nameof(Connection_TrustNewCertificate);
    public const string Connection_PinCleared = nameof(Connection_PinCleared);                 // "{Server}"
    public const string Connection_Port = nameof(Connection_Port);
    public const string Connection_Token = nameof(Connection_Token);
    public const string Connection_TokenHint = nameof(Connection_TokenHint);
    public const string Connection_Reveal = nameof(Connection_Reveal);

    // ── Where the server listens and where its world lives ────────────────────
    public const string Hosting_Heading = nameof(Hosting_Heading);
    public const string Hosting_Blurb = nameof(Hosting_Blurb);
    public const string Hosting_GamePort = nameof(Hosting_GamePort);
    public const string Hosting_GamePortHint = nameof(Hosting_GamePortHint);
    public const string Hosting_DataDir = nameof(Hosting_DataDir);
    public const string Hosting_DataDirHint = nameof(Hosting_DataDirHint);
    // The world-vs-game name distinction, said where an operator picks the world folder.
    public const string Hosting_WorldNameNote = nameof(Hosting_WorldNameNote);
    public const string Hosting_DataDirDefault = nameof(Hosting_DataDirDefault);
    public const string Hosting_Browse = nameof(Hosting_Browse);
    public const string Hosting_UseDefault = nameof(Hosting_UseDefault);
    public const string Hosting_GameName = nameof(Hosting_GameName);
    public const string Hosting_GameNameHint = nameof(Hosting_GameNameHint);
    public const string Hosting_MaxPlayers = nameof(Hosting_MaxPlayers);
    public const string Hosting_MaxPlayersHint = nameof(Hosting_MaxPlayersHint);

    // ── How many of each record family this world has room for ────────────────
    public const string Records_Heading = nameof(Records_Heading);
    public const string Records_Blurb = nameof(Records_Blurb);
    public const string Records_Items = nameof(Records_Items);
    public const string Records_Npcs = nameof(Records_Npcs);
    public const string Records_Shops = nameof(Records_Shops);
    public const string Records_Spells = nameof(Records_Spells);
    public const string Records_Quests = nameof(Records_Quests);
    public const string Records_Conversations = nameof(Records_Conversations);
    public const string Records_Maps = nameof(Records_Maps);
    public const string Records_MapGroups = nameof(Records_MapGroups);

    // ── What happens once the limit is reached ────────────────────────────────
    public const string Capacity_Heading = nameof(Capacity_Heading);
    public const string Capacity_Blurb = nameof(Capacity_Blurb);
    public const string Capacity_Reserved = nameof(Capacity_Reserved);
    public const string Capacity_ReservedHint = nameof(Capacity_ReservedHint);
    public const string Capacity_QueueDepth = nameof(Capacity_QueueDepth);
    public const string Capacity_QueueDepthHint = nameof(Capacity_QueueDepthHint);
    public const string Capacity_Grace = nameof(Capacity_Grace);
    public const string Capacity_GraceHint = nameof(Capacity_GraceHint);

    // ── Where players start, and when the weekly contest runs ─────────────────
    public const string World_SpawnHeading = nameof(World_SpawnHeading);
    public const string World_SpawnBlurb = nameof(World_SpawnBlurb);
    public const string World_SpawnMap = nameof(World_SpawnMap);
    public const string World_SpawnX = nameof(World_SpawnX);
    public const string World_SpawnY = nameof(World_SpawnY);
    public const string Schedule_Heading = nameof(Schedule_Heading);
    public const string Schedule_Blurb = nameof(Schedule_Blurb);
    public const string Schedule_WarNightDay = nameof(Schedule_WarNightDay);
    public const string Schedule_WarNightHour = nameof(Schedule_WarNightHour);
    public const string Schedule_WeekResetNote = nameof(Schedule_WeekResetNote);   // "{Day}"

    // ── Logging ───────────────────────────────────────────────────────────────
    public const string Logging_Heading = nameof(Logging_Heading);
    public const string Logging_Blurb = nameof(Logging_Blurb);
    public const string Logging_Level = nameof(Logging_Level);
    public const string Logging_LevelHint = nameof(Logging_LevelHint);
    public const string Logging_OutgoingPackets = nameof(Logging_OutgoingPackets);
    public const string Logging_IncomingPackets = nameof(Logging_IncomingPackets);
    public const string Logging_PacketsHint = nameof(Logging_PacketsHint);
    public const string Logging_ServerRetention = nameof(Logging_ServerRetention);
    public const string Logging_NetworkRetention = nameof(Logging_NetworkRetention);
    public const string Logging_RetentionHint = nameof(Logging_RetentionHint);
    public const string Logging_Unavailable = nameof(Logging_Unavailable);

    // ── Remote management, as the SERVER's own setting ────────────────────────
    public const string Management_Heading = nameof(Management_Heading);
    public const string Management_Blurb = nameof(Management_Blurb);
    public const string Management_Enable = nameof(Management_Enable);
    public const string Management_EnableHint = nameof(Management_EnableHint);
    public const string Management_Port = nameof(Management_Port);
    public const string Management_Token = nameof(Management_Token);
    public const string Management_TokenHint = nameof(Management_TokenHint);
    public const string Management_Copy = nameof(Management_Copy);
    public const string Management_TokenCopied = nameof(Management_TokenCopied);
    public const string Management_LocalOnly = nameof(Management_LocalOnly);

    public const string Config_DeathPenaltyHeading = nameof(Config_DeathPenaltyHeading);
    public const string Config_DeathPenaltyBlurb = nameof(Config_DeathPenaltyBlurb);
    public const string Config_DurabilityLoss = nameof(Config_DurabilityLoss);
    public const string Config_DurabilityLossHint = nameof(Config_DurabilityLossHint);
    public const string Config_ItemDrop = nameof(Config_ItemDrop);
    public const string Config_ItemDropHint = nameof(Config_ItemDropHint);
    public const string Config_ExpLoss = nameof(Config_ExpLoss);
    public const string Config_ExpLossHint = nameof(Config_ExpLossHint);
    public const string Config_LanguageHeading = nameof(Config_LanguageHeading);
    public const string Config_LanguageBlurb = nameof(Config_LanguageBlurb);
    public const string Config_LanguageServerNote = nameof(Config_LanguageServerNote);
    public const string Config_RestartRequired = nameof(Config_RestartRequired);
    public const string Config_Saved = nameof(Config_Saved);                     // "{Path}"
    public const string Config_SaveFailed = nameof(Config_SaveFailed);           // "{Error}"
    public const string Config_LoadFailed = nameof(Config_LoadFailed);           // "{Error}"

    // ── Runtime accessor ──────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    /// <summary>Scans <paramref name="langDir"/> and reads each file's <c>LanguageName</c>, so the
    /// picker lists languages by their own name rather than by locale code — someone choosing Spanish
    /// is looking for "Español", not "es". A malformed file is skipped rather than fatal: one bad
    /// translation must not cost the operator the ability to switch away from it.</summary>
    public static IReadOnlyList<(string Locale, string DisplayName)> GetAvailableLanguages(string langDir)
    {
        var result = new List<(string Locale, string DisplayName)>();
        if (!Directory.Exists(langDir)) return result;
        foreach (string file in Directory.GetFiles(langDir, "*.json"))
        {
            string locale = Path.GetFileNameWithoutExtension(file);
            try
            {
                var dict = StringLoader.Load(file);
                result.Add((locale, dict.TryGetValue(LanguageName, out var n) ? n : locale));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return result;
    }

    /// <summary>The locale the loaded table came from. Anything formatted by the framework rather than
    /// looked up here — day names, dates — resolves against THIS, not the machine's culture, because the
    /// shell's language is its own setting and an operator picked it deliberately.</summary>
    public static string CurrentLocale { get; private set; } = "en";

    /// <summary>Loads <paramref name="langCode"/> from <paramref name="langDir"/>, validating the
    /// translation against en.json. Mismatches throw in DEBUG (a missing key is a bug worth stopping
    /// for while it is cheap to fix) and merge over English in Release (an operator should never be
    /// shown a crash because one label was not translated).</summary>
    public static void Load(string langDir, string langCode = "en")
    {
        CurrentLocale = langCode;
        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        if (langCode == "en") { _current = english; return; }

        var translation = StringLoader.Load(Path.Combine(langDir, $"{langCode}.json"));
        var errors = StringLoader.Validate(english, translation, langCode);
        if (errors.Count > 0)
        {
#if DEBUG
            throw new InvalidOperationException("Translation errors:\n" + string.Join("\n", errors));
#else
            var merged = new Dictionary<string, string>(english);
            foreach (var (k, v) in translation) merged[k] = v;
            _current = merged;
            return;
#endif
        }
        _current = translation;
    }

    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var v)) return v;
#if DEBUG
        throw new InvalidOperationException($"[ShellStrings] Missing key: \"{key}\"");
#else
        return $"[{key}]";
#endif
    }

    public static string Format(string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(Get(key), args);
}
