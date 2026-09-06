using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Shell.Localization;
using Mirage.Server.Shell.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Security;
using System.Collections.ObjectModel;

namespace Mirage.Server.Shell.ViewModels;

/// <summary>
/// Supervise the server, relay its console, run its commands, and edit the rules it runs on.
///
/// <para>The rules get an explicit Save/Revert rather than writing on every toggle: they change what a
/// death costs every player, and a restart is what applies them.</para>
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Lines the console keeps. The real log is on disk with its own retention.</summary>
    private const int MaxConsoleLines = 5_000;

    // One string, not a list of lines: it is bound to a read-only TextBox so a selection can sweep
    // ACROSS lines, which is what copying a stack trace needs.
    private readonly System.Text.StringBuilder _log = new();
    private int _logLines;

    private readonly string _configPath = ServerConfigStore.DefaultPath;
    private readonly string _appSettingsPath = AppSettingsStore.DefaultPath;
    private readonly string _settingsPath = ShellSettings.DefaultPath;

    /// <summary>The server this window is driving. Swapped when the operator changes connection mode;
    /// everything downstream binds to the interface and never asks which kind it got.</summary>
    private IServerConnection _server = null!;

    public MainWindowViewModel()
    {
        var settings = ShellSettings.Load(_settingsPath);
        _isRemote = settings.Mode == ConnectionMode.Remote;
        _remoteHost = settings.RemoteHost;
        _remotePort = settings.RemotePort > 0 ? settings.RemotePort : DefaultManagementPort;
        _remoteToken = settings.RemoteToken;

        AttachConnection(CreateConnection());
        RefreshServers();
        LoadConfig();
        string current = ServerConfigStore.Load(_configPath).Config.Language;
        // Assigned to the backing field, not the property: the setter WRITES the file, and selecting
        // the value that is already in it would rewrite serverconfig.json on every launch.
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Locale == current)
                            ?? AvailableLanguages.FirstOrDefault(l => l.Locale == "en");
        BuildCommands();
    }

    private const int DefaultManagementPort = ShellSettings.DefaultManagementPort;

    private IServerConnection CreateConnection() =>
        IsRemote ? new RemoteServerConnection(RemoteHost, _remotePort, RemoteToken) : new ServerProcess();

    private void AttachConnection(IServerConnection connection)
    {
        _server = connection;
        _server.OutputReceived += line => Dispatcher.UIThread.Post(() => Receive(line));
        _server.StateChanged += _ => Dispatcher.UIThread.Post(OnStateChanged);
    }

    /// <summary>One line off the server. Status snapshots are machine traffic on the same stream and are
    /// taken out here — the console shows what a person would have seen at a terminal.</summary>
    private void Receive(string line)
    {
        if (line.StartsWith(ServerStatus.LinePrefix, StringComparison.Ordinal))
        {
            ApplyStatus(line[ServerStatus.LinePrefix.Length..]);
            return;
        }
        if (line.StartsWith(ModerationReport.LinePrefix, StringComparison.Ordinal))
        {
            ApplyModeration(line[ModerationReport.LinePrefix.Length..]);
            return;
        }
        AppendLine(line);
    }

    // ── Chrome ────────────────────────────────────────────────────────────────

    /// <summary>Named after the GAME this server runs, not the engine — an operator running Brightwater
    /// wants a window that says Brightwater. The executable and this window's settings folder stay on the
    /// engine name; see <see cref="ServerConfig.GameName"/>.</summary>
    public string Title => ShellStrings.Format(ShellStrings.Window_Title, ("GameName", GameName));
    public string ConsoleTabHeader => ShellStrings.Get(ShellStrings.Tab_Console);
    public string HelpMenuHeader => ShellStrings.Get(ShellStrings.Help_Menu);
    public string HelpAboutHeader => ShellStrings.Get(ShellStrings.Help_About);
    public string ConfigurationTabHeader => ShellStrings.Get(ShellStrings.Tab_Configuration);
    public string CommandsTabHeader => ShellStrings.Get(ShellStrings.Tab_Commands);
    public string CommandsBlurb => ShellStrings.Get(ShellStrings.Commands_Blurb);
    public string RunLabel => ShellStrings.Get(ShellStrings.Commands_Run);
    public string ConfirmLabel => ShellStrings.Get(ShellStrings.Commands_Confirm);
    public string CancelLabel => ShellStrings.Get(ShellStrings.Commands_Cancel);
    public string SendLabel => ShellStrings.Get(ShellStrings.Action_Send);
    public string SaveLabel => ShellStrings.Get(ShellStrings.Action_Save);
    public string RevertLabel => ShellStrings.Get(ShellStrings.Action_Revert);
    public string GenerateLabel => ShellStrings.Get(ShellStrings.Action_Generate);
    public string CommandHint => ShellStrings.Get(ShellStrings.Console_CommandHint);

    public string DeathPenaltyHeading => ShellStrings.Get(ShellStrings.Config_DeathPenaltyHeading);
    public string DeathPenaltyBlurb => ShellStrings.Get(ShellStrings.Config_DeathPenaltyBlurb);
    public string DurabilityLossLabel => ShellStrings.Get(ShellStrings.Config_DurabilityLoss);
    public string DurabilityLossHint => ShellStrings.Get(ShellStrings.Config_DurabilityLossHint);
    public string ItemDropLabel => ShellStrings.Get(ShellStrings.Config_ItemDrop);
    public string ItemDropHint => ShellStrings.Get(ShellStrings.Config_ItemDropHint);
    public string ExpLossLabel => ShellStrings.Get(ShellStrings.Config_ExpLoss);
    public string ExpLossHint => ShellStrings.Get(ShellStrings.Config_ExpLossHint);
    public string RestartRequiredNotice => ShellStrings.Get(ShellStrings.Config_RestartRequired);

    /// <summary>What the pinned buttons cover. Says the SCOPE as well as the timing, because a pinned
    /// footer sits under whichever panel is scrolled into view and would otherwise look like it saves
    /// that one.</summary>
    public string SaveScopeNotice => ShellStrings.Format(ShellStrings.Config_SaveScope,
        ("Group", ShellStrings.Get(ShellStrings.Config_ServerGroup)));
    public string LanguageHeading => ShellStrings.Get(ShellStrings.Config_LanguageHeading);
    public string LanguageBlurb => ShellStrings.Get(ShellStrings.Config_LanguageBlurb);
    public string LanguageServerNote => ShellStrings.Get(ShellStrings.Config_LanguageServerNote);

    // ── Server supervision ────────────────────────────────────────────────────

    /// <summary>Everything the server has printed, as one selectable block.</summary>
    public string ConsoleText => _log.ToString();

    [ObservableProperty]
    public partial string StateLabel { get; private set; } = ShellStrings.Get(ShellStrings.State_Stopped);

    // Colour is on top of the label, never instead of it — the word is what works for a colourblind
    // reader and in a screenshot.
    private static readonly IImmutableSolidColorBrush RunningBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly IImmutableSolidColorBrush StoppingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly IImmutableSolidColorBrush StoppedBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

    [ObservableProperty]
    public partial IImmutableSolidColorBrush StateBrush { get; private set; } = StoppedBrush;

    // The pill's ground: the same hue at low alpha, so the badge reads as one object rather than as a
    // coloured word sitting on a coloured chip.
    private static readonly IImmutableSolidColorBrush RunningFill = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0x4A, 0xDE, 0x80));
    private static readonly IImmutableSolidColorBrush StoppingFill = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0xFB, 0xBF, 0x24));
    private static readonly IImmutableSolidColorBrush StoppedFill = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0xF8, 0x71, 0x71));

    [ObservableProperty]
    public partial IImmutableSolidColorBrush StateFillBrush { get; private set; } = StoppedFill;

    [ObservableProperty]
    public partial string CommandText { get; set; } = "";

    public bool CanStart => _server.State == ServerState.Stopped;
    public bool CanStop => _server.State == ServerState.Running;

    /// <summary>The Start/Stop buttons say what they will actually do: a local server is started and
    /// stopped, a remote one is attached to and detached from. Nothing else in the window changes.</summary>
    public string StartLabel => ShellStrings.Get(IsRemote ? ShellStrings.Action_Attach : ShellStrings.Action_Start);
    public string StopLabel => ShellStrings.Get(IsRemote ? ShellStrings.Action_Detach : ShellStrings.Action_Stop);

    [RelayCommand]
    private async Task StartAsync()
    {
        DismissIdentityChange();
        if (IsRemote)
        {
            // 🔴 Rebuild before dialling. A RemoteServerConnection takes the host, port and token it was
            // CONSTRUCTED with, and the only other thing that builds one is the local/remote toggle — so
            // editing any of the three left the object holding what it was born with while the boxes,
            // the saved settings and the "Attaching to…" line all showed the new values.
            //
            // Pasting a fresh token and pressing Attach therefore presented the PREVIOUS one and came
            // back "the server refused the token", with every visible piece of state agreeing it should
            // have worked. Restarting the shell "fixed" it, because construction read the saved file.
            _server.Dispose();
            AttachConnection(CreateConnection());

            AppendLine(ShellStrings.Format(ShellStrings.Console_Attaching,
                ("Host", RemoteHost), ("Port", RemotePort)));
        }

        if (await _server.StartAsync() is not { } failure)
        {
            if (IsRemote)
            {
                // Rename, not Remember: this name is the operator's own, typed in the box beside it.
                ServerBookStore.Book.Rename(RemoteName, RemoteHost, _remotePort);
                RefreshServers();
            }
            return;
        }

        AppendLine(failure switch
        {
            RemoteServerConnection.RemoteError.Rejected => ShellStrings.Get(ShellStrings.Console_Rejected),
            RemoteServerConnection.RemoteError.Unreachable => ShellStrings.Format(
                ShellStrings.Console_Unreachable, ("Host", RemoteHost), ("Port", RemotePort)),
            RemoteServerConnection.RemoteError.IdentityChanged => ShellStrings.Format(
                ShellStrings.Console_IdentityChanged, ("Host", RemoteHost), ("Port", RemotePort)),
            // A local start reports the path it looked at, which is the message itself.
            _ => ShellStrings.Format(ShellStrings.Console_ServerNotFound, ("Path", failure)),
        });

        if (failure == RemoteServerConnection.RemoteError.IdentityChanged) ShowIdentityChange();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        // Only the local server is being asked to save and exit. Detaching costs a remote server nothing,
        // so promising it a 30-second wait would be a lie.
        if (!IsRemote)
            AppendLine(ShellStrings.Format(ShellStrings.Console_StoppingNotice,
                ("Seconds", (int)ServerProcess.ShutdownGrace.TotalSeconds)));
        await _server.StopAsync();
    }

    [RelayCommand]
    private void SendCommand()
    {
        string line = CommandText.Trim();
        if (line.Length == 0) return;
        Send(line);
        CommandText = "";
    }

    /// <summary>The one way a command reaches the server, whether it was typed on the console tab or
    /// composed by a form on the commands tab.</summary>
    private void Send(string line)
    {
        // Shutting a remote server down is a one-way door: nothing here can start it again, and the
        // operator would have to go to the machine. Refused rather than confirmed, because there is no
        // undo to offer. This is a guard on the window, not on the server — anything holding the token
        // can still send it, and a server that must go down has its own console.
        if (!_server.CanSupervise && IsShutdown(line))
        {
            AppendLine(ShellStrings.Get(ShellStrings.Console_ShutdownBlocked));
            return;
        }

        // Echoed locally: stdin is a pipe, so nothing else shows what was typed.
        AppendLine("> " + line);
        _server.SendCommand(line);
    }

    /// <summary>Whether a line is the shutdown command. Matched the way the server matches it — first
    /// token, case-insensitive — so "/SHUTDOWN now" is not a way around the check.</summary>
    public static bool IsShutdown(string line)
    {
        ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
        int space = trimmed.IndexOf(' ');
        if (space >= 0) trimmed = trimmed[..space];
        return trimmed.Equals("/shutdown", StringComparison.OrdinalIgnoreCase);
    }

    private void OnStateChanged()
    {
        // Remote says attached/not attached rather than running/stopped: from over here, a quiet socket
        // means this window lost the server, not that the server went down.
        StateLabel = ShellStrings.Get((_server.State, IsRemote) switch
        {
            (ServerState.Running, true) => ShellStrings.State_Attached,
            (ServerState.Running, false) => ShellStrings.State_Running,
            (ServerState.Stopping, _) => ShellStrings.State_Stopping,
            (_, true) => ShellStrings.State_Detached,
            _ => ShellStrings.State_Stopped,
        });
        StateBrush = _server.State switch
        {
            ServerState.Running => RunningBrush,
            ServerState.Stopping => StoppingBrush,
            _ => StoppedBrush,
        };
        StateFillBrush = _server.State switch
        {
            ServerState.Running => RunningFill,
            ServerState.Stopping => StoppingFill,
            _ => StoppedFill,
        };
        // A stopped or detached server has no roster to show. Leaving one up would be a list of people
        // who may not be there, on a server nobody is talking to.
        if (_server.State == ServerState.Stopped)
        {
            Status = null;
            Players.Clear();
            Editors.Clear();
            PendingBan = "";
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(HasPlayers));
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanEditConnection));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void AppendLine(string line)
    {
        _log.Append(line).Append('\n');
        _logLines++;
        // Whole lines off the front, not a character count.
        while (_logLines > MaxConsoleLines)
        {
            int firstBreak = -1;
            for (int i = 0; i < _log.Length; i++)
                if (_log[i] == '\n') { firstBreak = i; break; }
            if (firstBreak < 0) break;
            _log.Remove(0, firstBreak + 1);
            _logLines--;
        }
        OnPropertyChanged(nameof(ConsoleText));
    }

    // ── Server status ─────────────────────────────────────────────────────────
    // The dashboard's data. Arrives as its own message rather than being read out of console text,
    // which is localized and free to be reworded.

    [ObservableProperty]
    public partial ServerStatus? Status { get; private set; }

    private static readonly System.Text.Json.JsonSerializerOptions StatusJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private void ApplyStatus(string json)
    {
        ServerStatus? next;
        try
        {
            next = System.Text.Json.JsonSerializer.Deserialize<ServerStatus>(json, StatusJson);
        }
        catch (System.Text.Json.JsonException)
        {
            // A snapshot this build cannot read is not worth interrupting an operator over; the next
            // one is along shortly.
            return;
        }
        if (next is null) return;

        Status = next;
        Players.Clear();
        foreach (var p in next.Players) Players.Add(p);
        Editors.Clear();
        foreach (var e in next.Editors) Editors.Add(e);
        OnPropertyChanged(nameof(HasEditors));

        // The pickers show what the server SAYS it is, but only while the operator is not mid-choice —
        // otherwise a snapshot landing between click and commit would snatch the selection back.
        if (!_worldPickerBusy)
        {
            _selectedPhase = next.TimePhase;
            _selectedWeather = next.Weather;
            OnPropertyChanged(nameof(SelectedPhase));
            OnPropertyChanged(nameof(SelectedWeather));
        }
        if (!MotdEdited) _motdText = next.Motd;
        OnPropertyChanged(nameof(MotdText));

        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(PortText));
        OnPropertyChanged(nameof(OperatorsText));
        OnPropertyChanged(nameof(HasPlayers));
    }

    // ── The dashboard ─────────────────────────────────────────────────────────

    public string ServerTabHeader => ShellStrings.Get(ShellStrings.Tab_Server);
    public string ServerBlurb => ShellStrings.Get(ShellStrings.Server_Blurb);
    public string ServerOffline => ShellStrings.Get(ShellStrings.Server_Offline);
    public string WorldHeading => ShellStrings.Get(ShellStrings.Server_World);
    public string TimeOfDayLabel => ShellStrings.Get(ShellStrings.Server_TimeOfDay);
    public string WeatherLabel => ShellStrings.Get(ShellStrings.Server_Weather);
    public string MotdLabel => ShellStrings.Get(ShellStrings.Server_Motd);
    public string MotdHint => ShellStrings.Get(ShellStrings.Server_MotdHint);
    public string ApplyLabel => ShellStrings.Get(ShellStrings.Server_Apply);
    public string UptimeLabel => ShellStrings.Get(ShellStrings.Server_Uptime);
    // Not PortLabel: the Connection section already owns that name for the remote-target box.
    public string GamePortLabel => ShellStrings.Get(ShellStrings.Server_Port);
    public string OperatorsLabel => ShellStrings.Get(ShellStrings.Server_Operators);
    public string PlayersHeading => ShellStrings.Get(ShellStrings.Server_Players);
    public string PlayersEmpty => ShellStrings.Get(ShellStrings.Server_PlayersEmpty);

    // ── Editors ───────────────────────────────────────────────────────────────
    // Reported apart from players: an editor session holds no character, so a row of it has nothing to say
    // in a player's columns. What it DOES have is a list of records it is holding open, which is the thing
    // an operator wants to see before ending it.
    public ObservableCollection<EditorSummary> Editors { get; } = [];
    public bool HasEditors => Editors.Count > 0;
    public string EditorsHeading => ShellStrings.Get(ShellStrings.Server_Editors);
    public string EditorsEmpty => ShellStrings.Get(ShellStrings.Server_EditorsEmpty);
    public string ColSlot => ShellStrings.Get(ShellStrings.Server_ColSlot);
    public string ColHolding => ShellStrings.Get(ShellStrings.Server_ColHolding);
    public string DisconnectEditorLabel => ShellStrings.Get(ShellStrings.Server_DisconnectEditor);

    /// <summary>Ends one editor session. Addressed by SLOT rather than by account, because a session that is
    /// still signing in has no account yet and is exactly the kind an operator may want gone.</summary>
    [RelayCommand]
    private void DisconnectEditor(EditorSummary? e)
    {
        if (e is not null) Send($"/kickeditor {e.Slot}");
    }
    public string ColName => ShellStrings.Get(ShellStrings.Server_ColName);
    public string ColAccount => ShellStrings.Get(ShellStrings.Server_ColAccount);
    public string ColLevel => ShellStrings.Get(ShellStrings.Server_ColLevel);
    public string ColClass => ShellStrings.Get(ShellStrings.Server_ColClass);
    public string ColMap => ShellStrings.Get(ShellStrings.Server_ColMap);
    public string ColAccess => ShellStrings.Get(ShellStrings.Server_ColAccess);
    public string KickLabel => ShellStrings.Get(ShellStrings.Server_Kick);
    public string MuteLabel => ShellStrings.Get(ShellStrings.Server_Mute);
    public string BanLabel => ShellStrings.Get(ShellStrings.Server_Ban);
    public string MinutesLabel => ShellStrings.Get(ShellStrings.Server_Minutes);

    // ── The load benchmark ────────────────────────────────────────────────────
    // Opened from here rather than given a tab: it is a measurement taken once, and it owns a whole second
    // server for as long as its window is up.

    public string BenchOpenLabel => ShellStrings.Get(ShellStrings.Bench_Open);
    public string BenchUnavailableNotice => ShellStrings.Get(ShellStrings.Bench_Unavailable);

    /// <summary>The benchmark measures the machine this window is running on. Attached to a remote server
    /// that is the wrong box, and a number read off it would be nobody's answer.</summary>
    public bool CanBenchmark => !IsRemote;

    /// <summary>Builds the dialog's view-model. The config is read from disk when the run starts rather
    /// than captured now, so the benchmark measures the server as it is configured — not as this form
    /// happens to be left.</summary>
    public BenchViewModel CreateBenchmark() => new(
        () => ServerConfigStore.Load(_configPath).Config,
        players => MaxPlayers = players);

    /// <summary>Nothing has been heard from a server yet, so the page has nothing true to show.</summary>
    public bool HasStatus => Status is not null;
    public bool HasPlayers => Players.Count > 0;

    public System.Collections.ObjectModel.ObservableCollection<PlayerSummary> Players { get; } = [];

    // The same formatter the game uses for playtime, so an operator reading "2h 14m" here reads it the
    // same way everywhere else.
    public string UptimeText => Status is null ? "" : PlaytimeFormat.HoursMinutes(Status.UptimeSeconds);
    public string PortText => Status?.Port.ToString() ?? "";
    public string OperatorsText => Status?.Operators.ToString() ?? "";

    public IReadOnlyList<string> Phases { get; } = Enum.GetNames<TimePhase>();
    public IReadOnlyList<string> Weathers { get; } = Enum.GetNames<WeatherType>();

    // Held while a picker is being used so an arriving snapshot cannot pull the choice back out from
    // under the operator between selecting a value and the server confirming it.
    private bool _worldPickerBusy;
    private string _selectedPhase = "";
    private string _selectedWeather = "";

    public string SelectedPhase
    {
        get => _selectedPhase;
        set
        {
            if (value == _selectedPhase || string.IsNullOrEmpty(value)) return;
            _selectedPhase = value;
            OnPropertyChanged();
            _worldPickerBusy = true;
            Send($"/tod {value}");
            _worldPickerBusy = false;
        }
    }

    public string SelectedWeather
    {
        get => _selectedWeather;
        set
        {
            if (value == _selectedWeather || string.IsNullOrEmpty(value)) return;
            _selectedWeather = value;
            OnPropertyChanged();
            _worldPickerBusy = true;
            Send($"/weather {value}");
            _worldPickerBusy = false;
        }
    }

    private string _motdText = "";

    /// <summary>The MOTD box. Once touched it stops following the server, so a snapshot cannot wipe
    /// half-typed text; Apply hands it over and lets it follow again.</summary>
    public string MotdText
    {
        get => _motdText;
        set
        {
            if (value == _motdText) return;
            _motdText = value;
            MotdEdited = true;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial bool MotdEdited { get; private set; }

    [RelayCommand]
    private void ApplyMotd()
    {
        // Newlines fold to spaces for the same two reasons the command form does it: stdin is
        // line-oriented, and the client's sprite font cannot draw U+000A.
        string text = string.Join(' ', MotdText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) return;
        Send($"/motd {text}");
        MotdEdited = false;
    }

    /// <summary>Per-row penalties. Composed as the SAME text the Commands tab builds and sent down the
    /// same pipe, so there is one spelling of each command however an operator reached it.</summary>
    [RelayCommand]
    private void KickPlayer(PlayerSummary? p)
    {
        if (p is not null) Send($"/kick {p.Name} {(int)PenaltyMinutes}");
    }

    [RelayCommand]
    private void MutePlayer(PlayerSummary? p)
    {
        if (p is not null) Send($"/mute {p.Name} {(int)PenaltyMinutes}");
    }

    /// <summary>Banning asks twice, exactly as the command form does — it is permanent, and a row action
    /// is easier to hit by accident than a form is.</summary>
    [RelayCommand]
    private void BanPlayer(PlayerSummary? p)
    {
        if (p is null) return;
        if (PendingBan != p.Name) { PendingBan = p.Name; return; }
        Send($"/ban {p.Name}");
        PendingBan = "";
    }

    [ObservableProperty]
    public partial string PendingBan { get; set; } = "";

    /// <summary>Minutes a kick or mute lasts, shared by every row. The console's own bounds.</summary>
    [ObservableProperty]
    public partial decimal PenaltyMinutes { get; set; } = 60;

    // ── Moderation ────────────────────────────────────────────────────────────
    // Who is punished, and the lift beside each one. Its own tab because the dashboard above is who is
    // online NOW, and a banned or kicked person is by definition not.
    //
    // The report is gathered ON REQUEST — the server has to read its ban file and sweep every account —
    // so nothing here is pushed on a timer. It arrives when this asks, and again after every change the
    // server makes, which is what keeps a lifted row from lingering.

    public System.Collections.ObjectModel.ObservableCollection<BanSummary> Bans { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<PenaltySummary> Penalties { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<HardwareBanSummary> HardwareBans { get; } = [];

    [ObservableProperty]
    public partial ModerationReport? Moderation { get; private set; }

    public bool HasModeration => Moderation is not null;
    public bool HasBans => Bans.Count > 0;
    public bool HasPenalties => Penalties.Count > 0;
    public bool HasHardwareBans => HardwareBans.Count > 0;

    private void ApplyModeration(string json)
    {
        ModerationReport? next;
        try
        {
            next = System.Text.Json.JsonSerializer.Deserialize<ModerationReport>(json, StatusJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }
        if (next is null) return;

        Moderation = next;
        Bans.Clear();
        foreach (var b in next.Bans) Bans.Add(b);
        Penalties.Clear();
        foreach (var p in next.Penalties) Penalties.Add(p);
        HardwareBans.Clear();
        foreach (var h in next.HardwareBans) HardwareBans.Add(h);

        OnPropertyChanged(nameof(HasModeration));
        OnPropertyChanged(nameof(HasBans));
        OnPropertyChanged(nameof(HasPenalties));
        OnPropertyChanged(nameof(HasHardwareBans));
        OnPropertyChanged(nameof(HardwareBanModeText));
        OnPropertyChanged(nameof(ScannedText));
    }

    [RelayCommand]
    private void RefreshModeration() => Send("/moderation");

    /// <summary>Lifting a ban targets the LOGIN, never a character name — the account is what the ban is
    /// stored against, and nobody banned is online to be named.</summary>
    [RelayCommand]
    private void Unban(BanSummary? b)
    {
        if (b is not null) Send($"/unban {b.Login}");
    }

    /// <summary>Lifts every machine ban on an account. Leaves the ACCOUNT ban in place — the two are
    /// separate rows here for the same reason they are separate commands.</summary>
    [RelayCommand]
    private void HardwareUnban(HardwareBanSummary? b)
    {
        if (b is not null) Send($"/hwunban {b.Login}");
    }

    [RelayCommand]
    private void LiftPenalty(PenaltySummary? p)
    {
        if (p is null) return;
        // The console has a command per kind rather than one that guesses, so the shell picks here.
        Send(string.Equals(p.Kind, "Kick", StringComparison.OrdinalIgnoreCase)
            ? $"/unkick {p.Login}"
            : $"/unmute {p.Login}");
    }

    public string ModerationTabHeader => ShellStrings.Get(ShellStrings.Tab_Moderation);
    public string ModerationBlurb => ShellStrings.Get(ShellStrings.Mod_Blurb);
    public string ModerationRefreshLabel => ShellStrings.Get(ShellStrings.Mod_Refresh);
    public string BansHeading => ShellStrings.Get(ShellStrings.Mod_Bans);
    public string BansEmpty => ShellStrings.Get(ShellStrings.Mod_BansEmpty);
    public string HardwareBansHeading => ShellStrings.Get(ShellStrings.Mod_HardwareBans);
    public string HardwareBansEmpty => ShellStrings.Get(ShellStrings.Mod_HardwareBansEmpty);
    public string PenaltiesHeading => ShellStrings.Get(ShellStrings.Mod_Penalties);
    public string PenaltiesEmpty => ShellStrings.Get(ShellStrings.Mod_PenaltiesEmpty);
    public string ModColAccount => ShellStrings.Get(ShellStrings.Mod_ColAccount);
    public string ModColReason => ShellStrings.Get(ShellStrings.Mod_ColReason);
    public string ModColApplied => ShellStrings.Get(ShellStrings.Mod_ColApplied);
    public string ModColKind => ShellStrings.Get(ShellStrings.Mod_ColKind);
    public string ModColRemaining => ShellStrings.Get(ShellStrings.Mod_ColRemaining);
    public string ModColWhere => ShellStrings.Get(ShellStrings.Mod_ColWhere);
    public string LiftLabel => ShellStrings.Get(ShellStrings.Mod_Lift);
    public string ModerationNotLoaded => ShellStrings.Get(ShellStrings.Mod_NotLoaded);

    /// <summary>How many accounts the last sweep read. Shown so an empty list is distinguishable from a
    /// list that was never gathered.</summary>
    public string ScannedText => Moderation is null
        ? ""
        : ShellStrings.Format(ShellStrings.Mod_Scanned, ("Count", Moderation.AccountsScanned));

    /// <summary>What a machine-ban match actually does on this server, in a sentence. Never a bare
    /// "Signal"/"Block" — those are the config's words, and an operator reading a list of banned machines
    /// needs to know whether those people are being kept out or merely watched.</summary>
    public string HardwareBanModeText => Moderation is null
        ? ""
        : ShellStrings.Get(string.Equals(Moderation.HardwareBanMode, "Block", StringComparison.OrdinalIgnoreCase)
            ? ShellStrings.Mod_HardwareBanModeBlock
            : ShellStrings.Mod_HardwareBanModeSignal);

    // ── Commands ──────────────────────────────────────────────────────────────

    public IReadOnlyList<CommandGroup> CommandGroups { get; private set; } = [];

    /// <summary>Builds the command forms. Picker options come from the real enums, so the UI cannot
    /// offer a value the server would refuse.</summary>
    private void BuildCommands()
    {
        CommandGroups = BuildCommandGroups(Send);
        OnPropertyChanged(nameof(CommandGroups));
    }

    /// <summary>The command forms themselves, independent of any window. Takes the send action rather
    /// than reaching for one, so the list can be built and inspected without a settings file, a
    /// connection, or a running server behind it.</summary>
    /// <summary>/management, whose value argument depends on the action chosen beside it. Built here
    /// rather than inline because the value has to be handed the action to watch.</summary>
    private static ShellCommand ManagementCommand(Action<string> send)
    {
        var action = CommandParameter.Choice("action", ["", "port", "token", "off"]);
        var value = CommandParameter.Text("port").ShownWhen(action, "port").Required();
        return new ShellCommand("/management", ShellStrings.Get(ShellStrings.Commands_Management),
                                send, action, value);
    }

    public static IReadOnlyList<CommandGroup> BuildCommandGroups(Action<string> Send)
    {
        static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();

        return
        [
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Players),
            [
                new ShellCommand("/who", ShellStrings.Get(ShellStrings.Commands_Who), Send),
                // The console's own bounds: 1 to 1440 minutes, defaulting to its 60.
                new ShellCommand("/kick", ShellStrings.Get(ShellStrings.Commands_Kick), Send,
                    CommandParameter.Text("name").Required(), CommandParameter.Number("minutes", 1, 1440, 60)),
                new ShellCommand("/mute", ShellStrings.Get(ShellStrings.Commands_Mute), Send,
                    CommandParameter.Text("name").Required(), CommandParameter.Number("minutes", 1, 1440, 60)),
                new ShellCommand("/ban", ShellStrings.Get(ShellStrings.Commands_Ban), Send,
                    CommandParameter.Text("name").Required()) { NeedsConfirmation = true },
                new ShellCommand("/setaccess", ShellStrings.Get(ShellStrings.Commands_SetAccess), Send,
                    CommandParameter.Choice("level", Names<AdminLevel>()),
                    CommandParameter.Text("name").Required()) { NeedsConfirmation = true },
                new ShellCommand("/refreshbanlist", ShellStrings.Get(ShellStrings.Commands_RefreshBanList), Send),
                new ShellCommand("/moderation", ShellStrings.Get(ShellStrings.Commands_Moderation), Send),
                // The lifts take an ACCOUNT: the point of a lift is that the person is locked out and so
                // cannot be found on the roster a name would be looked up in.
                new ShellCommand("/unban", ShellStrings.Get(ShellStrings.Commands_Unban), Send,
                    CommandParameter.Text("account").Required()),
                new ShellCommand("/unkick", ShellStrings.Get(ShellStrings.Commands_Unkick), Send,
                    CommandParameter.Text("account").Required()),
                new ShellCommand("/unmute", ShellStrings.Get(ShellStrings.Commands_Unmute), Send,
                    CommandParameter.Text("account").Required()),
                // /hwban is the exception that takes a NAME: it reads the machine id off a live session,
                // so the target has to be online for there to be anything to ban.
                new ShellCommand("/hwban", ShellStrings.Get(ShellStrings.Commands_HwBan), Send,
                    CommandParameter.Text("name").Required()) { NeedsConfirmation = true },
                new ShellCommand("/hwunban", ShellStrings.Get(ShellStrings.Commands_HwUnban), Send,
                    CommandParameter.Text("account").Required()),
            ]),
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_World),
            [
                new ShellCommand("/tod", ShellStrings.Get(ShellStrings.Commands_Tod), Send,
                    CommandParameter.Choice("phase", Names<TimePhase>())),
                new ShellCommand("/weather", ShellStrings.Get(ShellStrings.Commands_Weather), Send,
                    CommandParameter.Choice("type", Names<WeatherType>())),
                new ShellCommand("/motd", ShellStrings.Get(ShellStrings.Commands_Motd), Send,
                    CommandParameter.Paragraph("text")),
                // Maps are numeric ids, so the box is one — bounded to what the server would accept.
                new ShellCommand("/respawn", ShellStrings.Get(ShellStrings.Commands_Respawn), Send,
                    CommandParameter.Number("map", 1, RecordLimits.Default.Maps, 1)),
                new ShellCommand("/mapreport", ShellStrings.Get(ShellStrings.Commands_MapReport), Send),
            ]),
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Guilds),
            [
                new ShellCommand("/startwar", ShellStrings.Get(ShellStrings.Commands_StartWar), Send),
                new ShellCommand("/advancewar", ShellStrings.Get(ShellStrings.Commands_AdvanceWar), Send),
                new ShellCommand("/endwar", ShellStrings.Get(ShellStrings.Commands_EndWar), Send),
                new ShellCommand("/guildreset", ShellStrings.Get(ShellStrings.Commands_GuildReset), Send,
                    CommandParameter.Choice("scope", Names<SettlementScope>())),
            ]),
            // The tab doubles as the list of what the console accepts, so a command being reachable
            // another way is not a reason to leave it out. /shutdown is also available as the Stop
            // button on the Console tab; here it asks first, the same as the other destructive ones.
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Editors),
            [
                new ShellCommand("/editors", ShellStrings.Get(ShellStrings.Commands_Editors), Send),
                new ShellCommand("/kickeditor", ShellStrings.Get(ShellStrings.Commands_KickEditor), Send,
                    CommandParameter.Text("slot or account").Required()),
            ]),
            // Remote access. Reachable from the Connection tab too, but that sets what THIS shell dials;
            // these set what the SERVER offers, which is the other half and has no other home.
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Remote),
            [
                // ONE row. The console dispatches on the first word, so the verb is /management and
                // the action rides as an argument — the same grammar a hand-typed line uses. A row
                // named "/management port" would post a line whose first word has no case, which is
                // a form that renders, presses, and silently does nothing.
                //
                // Blank action reads the state back; the run composer drops empty arguments, so the
                // one form covers "show", "port n", "token" and "off" without four of them.
                // The value belongs to "port" alone. Shown only there, so three of the four actions do
                // not present an empty box that does nothing, and required there, so Run stays disabled
                // until a port is typed rather than posting "/management port" for the server to refuse.
                ManagementCommand(Send),
            ]),
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Server),
            [
                new ShellCommand("/update", ShellStrings.Get(ShellStrings.Commands_Update), Send),
                new ShellCommand("/credits", ShellStrings.Get(ShellStrings.Commands_Credits), Send),
                new ShellCommand("/shutdown", ShellStrings.Get(ShellStrings.Commands_Shutdown), Send)
                    { NeedsConfirmation = true },
            ]),
        ];
    }

    // ── Connection ────────────────────────────────────────────────────────────

    public string WindowGroupHeading => ShellStrings.Get(ShellStrings.Config_WindowGroup);
    public string WindowGroupHint => ShellStrings.Get(ShellStrings.Config_WindowGroupHint);
    public string ServerGroupHeading => ShellStrings.Get(ShellStrings.Config_ServerGroup);
    public string ServerGroupHint => ShellStrings.Get(ShellStrings.Config_ServerGroupHint);

    public string ConnectionHeading => ShellStrings.Get(ShellStrings.Connection_Heading);
    public string ConnectionBlurb => ShellStrings.Get(ShellStrings.Connection_Blurb);
    public string LaunchLabel => ShellStrings.Get(ShellStrings.Connection_Launch);
    public string LaunchHint => ShellStrings.Get(ShellStrings.Connection_LaunchHint);
    public string AttachLabel => ShellStrings.Get(ShellStrings.Connection_Attach);
    public string AttachHint => ShellStrings.Get(ShellStrings.Connection_AttachHint);
    public string HostLabel => ShellStrings.Get(ShellStrings.Connection_Host);
    public string PortLabel => ShellStrings.Get(ShellStrings.Connection_Port);
    public string TokenLabel => ShellStrings.Get(ShellStrings.Connection_Token);
    public string RemoteTokenHint => ShellStrings.Get(ShellStrings.Connection_TokenHint);
    public string RevealLabel => ShellStrings.Get(ShellStrings.Connection_Reveal);

    private bool _isRemote;
    private string _remoteHost = "";
    private int _remotePort;
    private string _remoteToken = "";

    /// <summary>Where the target can be changed at all. Swapping the connection under a live session
    /// would leave a server running with nothing watching it, so the fields lock while attached.</summary>
    public bool CanEditConnection => _server.State == ServerState.Stopped;

    public bool IsRemote
    {
        get => _isRemote;
        set
        {
            if (_isRemote == value) return;
            _isRemote = value;
            SwapConnection();
        }
    }

    /// <summary>Local is the inverse of remote, as a bindable of its own — a two-way RadioButton needs a
    /// property per option.</summary>
    public bool IsLocal
    {
        get => !_isRemote;
        set { if (value) IsRemote = false; }
    }

    public string RemoteHost
    {
        get => _remoteHost;
        set
        {
            if (!SetProperty(ref _remoteHost, value)) return;
            SaveSettings();
            SyncSelection();
            AddServerCommand.NotifyCanExecuteChanged();
        }
    }

    public decimal RemotePort
    {
        get => _remotePort;
        set
        {
            int port = (int)value;
            if (_remotePort == port) return;
            _remotePort = port;
            OnPropertyChanged();
            SaveSettings();
            SyncSelection();
        }
    }

    public string RemoteToken
    {
        get => _remoteToken;
        set { if (SetProperty(ref _remoteToken, value)) SaveSettings(); }
    }

    // ── Known servers ─────────────────────────────────────────────────────────

    /// <summary>One row of the known-servers picker. Carries its own caption so the address book stays
    /// free of display text.</summary>
    public sealed record ServerChoice(string Label, string Host, int Port);

    public ObservableCollection<ServerChoice> KnownServers { get; } = [];

    public string KnownServersLabel => ShellStrings.Get(ShellStrings.Connection_KnownServers);
    public string ServerNameLabel => ShellStrings.Get(ShellStrings.Connection_ServerName);
    public string ForgetServerLabel => ShellStrings.Get(ShellStrings.Connection_ForgetServer);
    public string AddServerLabel => ShellStrings.Get(ShellStrings.Connection_AddServer);
    public string ClearPinLabel => ShellStrings.Get(ShellStrings.Connection_ClearPin);
    public string TrustNewCertificateLabel => ShellStrings.Get(ShellStrings.Connection_TrustNewCertificate);

    /// <summary>The refused certificate from the last attach, kept so the banner's button knows which pin
    /// to drop. Non-null exactly while <see cref="IdentityMessage"/> is showing.</summary>
    private ServerIdentityChangedException? _pendingIdentity;

    /// <summary>The identity-changed explanation with both fingerprints, empty when there is nothing to
    /// decide. The console carries the same text, but a line that scrolls away is no place to keep the
    /// only way out of a refused connection.</summary>
    [ObservableProperty]
    public partial string IdentityMessage { get; private set; } = "";

    /// <summary>What to call this address in the list. The management port carries no game name — it is a
    /// console socket, not a login — so the operator supplies one.</summary>
    public string RemoteName
    {
        get => _remoteName;
        set => SetProperty(ref _remoteName, value);
    }
    private string _remoteName = "";

    /// <summary>The picked row. Setting it fills the host, port and name fields.</summary>
    public ServerChoice? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value) || value is null) return;
            RemoteHost = value.Host;
            RemotePort = value.Port;
            RemoteName = value.Label.Contains('(') ? value.Label[..value.Label.IndexOf('(')].Trim() : "";
            ForgetServerCommand.NotifyCanExecuteChanged();
        }
    }
    private ServerChoice? _selectedServer;

    private void RefreshServers()
    {
        ServerBookStore.Book.Reload();
        KnownServers.Clear();
        foreach (var e in ServerBookStore.Book.All)
            KnownServers.Add(new ServerChoice(
                e.Name.Length > 0 ? $"{e.Name}  ({e.Host}:{e.Port})" : $"{e.Host}:{e.Port}", e.Host, e.Port));
        SyncSelection();
    }

    // Typing an address that is already known selects it and shows its name. Typing an unknown one keeps
    // whatever name was typed, so a name entered before the address is not thrown away.
    private void SyncSelection()
    {
        string key = ServerBook.KeyFor(_remoteHost, _remotePort);
        _selectedServer = KnownServers.FirstOrDefault(c => ServerBook.KeyFor(c.Host, c.Port) == key);
        OnPropertyChanged(nameof(SelectedServer));
        if (_selectedServer is not null)
            RemoteName = ServerBookStore.Book.Find(_remoteHost, _remotePort)?.Name ?? "";
        ForgetServerCommand.NotifyCanExecuteChanged();
        ClearPinCommand.NotifyCanExecuteChanged();
    }

    private bool CanForgetServer() => SelectedServer is not null;

    [RelayCommand(CanExecute = nameof(CanForgetServer))]
    private void ForgetServer()
    {
        if (SelectedServer is not { } gone) return;
        ServerBookStore.Book.Forget(gone.Host, gone.Port);
        RemoteName = "";
        RefreshServers();
    }

    private bool CanClearPin() => ServerPinStore.Store.PinnedFingerprint(_remoteHost, _remotePort) is not null;

    /// <summary>Drops the certificate on record for the typed address, so the next attach records
    /// whatever is offered. Separate from <see cref="ForgetServerCommand"/>: dropping the entry from the
    /// list leaves the pin behind, which is what makes re-adding the same address fail the same way.</summary>
    [RelayCommand(CanExecute = nameof(CanClearPin))]
    private void ClearPin() => DropPin(_remoteHost, _remotePort);

    /// <summary>Accepts the certificate that was just refused by dropping the old pin. Nothing is
    /// retried here: the next Attach is a first contact, which records the new one.</summary>
    [RelayCommand]
    private void TrustNewCertificate()
    {
        if (_pendingIdentity is { } identity) DropPin(identity.Host, identity.Port);
    }

    private void DropPin(string host, int port)
    {
        if (!ServerPinStore.Store.Forget(host, port)) return;
        DismissIdentityChange();
        AppendLine(ShellStrings.Format(ShellStrings.Connection_PinCleared, ("Server", $"{host}:{port}")));
        ClearPinCommand.NotifyCanExecuteChanged();
    }

    private void ShowIdentityChange()
    {
        if ((_server as RemoteServerConnection)?.LastIdentityChange is not { } identity) return;
        _pendingIdentity = identity;
        IdentityMessage = ShellStrings.Format(ShellStrings.Console_IdentityChangedDetail,
            ("Host", identity.Host), ("Port", identity.Port),
            ("Expected", ServerPins.ForDisplay(identity.Expected)),
            ("Actual", ServerPins.ForDisplay(identity.Actual)));
        AppendLine(IdentityMessage);
    }

    private void DismissIdentityChange()
    {
        _pendingIdentity = null;
        IdentityMessage = "";
    }

    private bool CanAddServer() => !string.IsNullOrWhiteSpace(_remoteHost);

    /// <summary>Puts the typed address in the list under the typed name, without attaching to it.
    /// Attaching records it too; this is for setting one up ahead of time.</summary>
    [RelayCommand(CanExecute = nameof(CanAddServer))]
    private void AddServer()
    {
        ServerBookStore.Book.Rename(RemoteName, RemoteHost, _remotePort);
        RefreshServers();
    }

    /// <summary>Whether the token box shows its contents. Off by default: a server console is the sort of
    /// thing that gets screen-shared.</summary>
    [ObservableProperty]
    public partial bool RevealTokens { get; set; }

    // Tears down the old connection and builds one for the new mode. Only reachable while stopped, so
    // nothing is being dropped mid-session.
    private void SwapConnection()
    {
        _server.Dispose();
        AttachConnection(CreateConnection());
        SaveSettings();
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(CanEditServerFile));
        OnPropertyChanged(nameof(CanBenchmark));
        OnStateChanged();
    }

    private void SaveSettings() => new ShellSettings
    {
        Mode = _isRemote ? ConnectionMode.Remote : ConnectionMode.Local,
        RemoteHost = _remoteHost,
        RemotePort = _remotePort,
        RemoteToken = _remoteToken,
    }.Save(_settingsPath);

    // ── Configuration ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool DurabilityLoss { get; set; } = true;

    [ObservableProperty]
    public partial bool ItemDrop { get; set; } = true;

    [ObservableProperty]
    public partial bool ExpLoss { get; set; } = true;

    /// <summary>The last thing that happened to the config file — saved, or why not. Empty until
    /// something has.</summary>
    [ObservableProperty]
    public partial string ConfigStatus { get; private set; } = "";

    // ── Logging ───────────────────────────────────────────────────────────────
    // The other config file. appsettings.json is hand-authored structure; only these five values are
    // edited here, and each is greyed out rather than guessed at when the file does not expose it.

    public string LoggingHeading => ShellStrings.Get(ShellStrings.Logging_Heading);
    public string LoggingBlurb => ShellStrings.Get(ShellStrings.Logging_Blurb);
    public string LogLevelLabel => ShellStrings.Get(ShellStrings.Logging_Level);
    public string LogLevelHint => ShellStrings.Get(ShellStrings.Logging_LevelHint);
    public string OutgoingPacketsLabel => ShellStrings.Get(ShellStrings.Logging_OutgoingPackets);
    public string IncomingPacketsLabel => ShellStrings.Get(ShellStrings.Logging_IncomingPackets);
    public string PacketsHint => ShellStrings.Get(ShellStrings.Logging_PacketsHint);
    public string ServerRetentionLabel => ShellStrings.Get(ShellStrings.Logging_ServerRetention);
    public string NetworkRetentionLabel => ShellStrings.Get(ShellStrings.Logging_NetworkRetention);
    public string RetentionHint => ShellStrings.Get(ShellStrings.Logging_RetentionHint);
    public string LoggingUnavailableNotice => ShellStrings.Get(ShellStrings.Logging_Unavailable);

    /// <summary>The levels an operator picks between, straight from the shared list so the picker cannot
    /// offer one Serilog would not take.</summary>
    public IReadOnlyList<string> LogLevels { get; } = LogSettings.Levels;

    private LogKnobs _logKnobs = LogKnobs.All;

    [ObservableProperty]
    public partial string LogLevel { get; set; } = "Information";

    [ObservableProperty]
    public partial bool LogOutgoingPackets { get; set; }

    [ObservableProperty]
    public partial bool LogIncomingPackets { get; set; }

    [ObservableProperty]
    public partial decimal ServerRetentionDays { get; set; } = 7;

    [ObservableProperty]
    public partial decimal NetworkRetentionDays { get; set; } = 3;

    public bool CanEditLogLevel => _logKnobs.HasFlag(LogKnobs.MinimumLevel);
    public bool CanEditOutgoingPackets => _logKnobs.HasFlag(LogKnobs.OutgoingPackets);
    public bool CanEditIncomingPackets => _logKnobs.HasFlag(LogKnobs.IncomingPackets);
    public bool CanEditServerRetention => _logKnobs.HasFlag(LogKnobs.ServerRetention);
    public bool CanEditNetworkRetention => _logKnobs.HasFlag(LogKnobs.NetworkRetention);

    /// <summary>True when anything at all failed to resolve, which is what shows the explanation.</summary>
    public bool LoggingIncomplete => _logKnobs != LogKnobs.All;

    private void LoadLogSettings()
    {
        var (log, _) = AppSettingsStore.Load(_appSettingsPath);
        _logKnobs = log.Available;
        LogLevel = log.MinimumLevel;
        LogOutgoingPackets = log.LogOutgoingPackets;
        LogIncomingPackets = log.LogIncomingPackets;
        ServerRetentionDays = log.ServerLogRetentionDays;
        NetworkRetentionDays = log.NetworkLogRetentionDays;

        OnPropertyChanged(nameof(CanEditLogLevel));
        OnPropertyChanged(nameof(CanEditOutgoingPackets));
        OnPropertyChanged(nameof(CanEditIncomingPackets));
        OnPropertyChanged(nameof(CanEditServerRetention));
        OnPropertyChanged(nameof(CanEditNetworkRetention));
        OnPropertyChanged(nameof(LoggingIncomplete));
    }

    // ── Port and world folder ─────────────────────────────────────────────────
    // Both were left in their files when the rest of the config moved into this window, which meant the
    // one page that claims to own the server's settings quietly did not.

    public string HostingHeading => ShellStrings.Get(ShellStrings.Hosting_Heading);
    public string HostingBlurb => ShellStrings.Get(ShellStrings.Hosting_Blurb);
    public string HostingPortLabel => ShellStrings.Get(ShellStrings.Hosting_GamePort);
    public string HostingPortHint => ShellStrings.Get(ShellStrings.Hosting_GamePortHint);
    public string DataDirLabel => ShellStrings.Get(ShellStrings.Hosting_DataDir);
    public string DataDirHint => ShellStrings.Get(ShellStrings.Hosting_DataDirHint);

    /// <summary>That a world names itself, and that the name is the operator's rather than the player's.</summary>
    public string WorldNameNote => ShellStrings.Get(ShellStrings.Hosting_WorldNameNote);
    public string DataDirPlaceholder => ShellStrings.Get(ShellStrings.Hosting_DataDirDefault);
    public string BrowseLabel => ShellStrings.Get(ShellStrings.Hosting_Browse);
    public string UseDefaultLabel => ShellStrings.Get(ShellStrings.Hosting_UseDefault);
    public string GameNameLabel => ShellStrings.Get(ShellStrings.Hosting_GameName);
    public string GameNameHint => ShellStrings.Get(ShellStrings.Hosting_GameNameHint);

    /// <summary>What players will see this world called. Blank falls back to the engine's name, which is
    /// what the setter on <see cref="ServerConfig.GameName"/> does with it on save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string GameName { get; set; } = Constants.GameName;

    public string MaxPlayersLabel => ShellStrings.Get(ShellStrings.Hosting_MaxPlayers);
    public string MaxPlayersHint => ShellStrings.Get(ShellStrings.Hosting_MaxPlayersHint);

    /// <summary>How many players this server accepts at once. Small by default on purpose: the right
    /// number is a property of the machine, and the State tab's benchmark measures it. The spinner's
    /// ceiling is the protocol's — above it the server would issue slot numbers a shipped client cannot
    /// index.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReservedCeiling))]
    public partial decimal MaxPlayers { get; set; } = ServerConfig.Default.MaxPlayers;

    public decimal MaxPlayersCeiling => Constants.MaxPlayers;

    // ── Record slots ──────────────────────────────────────────────────────────
    // Per-server now rather than compiled in, and stated to every client in the pre-login hello — see
    // RecordLimits. A client sizes its own tables from what it was told.

    public string RecordsHeading => ShellStrings.Get(ShellStrings.Records_Heading);
    public string RecordsBlurb => ShellStrings.Get(ShellStrings.Records_Blurb);
    public string RecordItemsLabel => ShellStrings.Get(ShellStrings.Records_Items);
    public string RecordNpcsLabel => ShellStrings.Get(ShellStrings.Records_Npcs);
    public string RecordShopsLabel => ShellStrings.Get(ShellStrings.Records_Shops);
    public string RecordSpellsLabel => ShellStrings.Get(ShellStrings.Records_Spells);
    public string RecordQuestsLabel => ShellStrings.Get(ShellStrings.Records_Quests);
    public string RecordConversationsLabel => ShellStrings.Get(ShellStrings.Records_Conversations);
    public string RecordMapsLabel => ShellStrings.Get(ShellStrings.Records_Maps);
    public string RecordMapGroupsLabel => ShellStrings.Get(ShellStrings.Records_MapGroups);

    /// <summary>The largest any one family may be set to — a backstop against a typo costing gigabytes,
    /// since both ends allocate an array of this length.</summary>
    public decimal RecordCeiling => RecordLimits.Ceiling;

    [ObservableProperty] public partial decimal RecordItems { get; set; } = RecordLimits.Default.Items;
    [ObservableProperty] public partial decimal RecordNpcs { get; set; } = RecordLimits.Default.Npcs;
    [ObservableProperty] public partial decimal RecordShops { get; set; } = RecordLimits.Default.Shops;
    [ObservableProperty] public partial decimal RecordSpells { get; set; } = RecordLimits.Default.Spells;
    [ObservableProperty] public partial decimal RecordQuests { get; set; } = RecordLimits.Default.Quests;
    [ObservableProperty] public partial decimal RecordConversations { get; set; } = RecordLimits.Default.Conversations;
    [ObservableProperty] public partial decimal RecordMaps { get; set; } = RecordLimits.Default.Maps;
    [ObservableProperty] public partial decimal RecordMapGroups { get; set; } = RecordLimits.Default.MapGroups;

    // ── What happens at the limit ─────────────────────────────────────────────

    public string CapacityHeading => ShellStrings.Get(ShellStrings.Capacity_Heading);
    public string CapacityBlurb => ShellStrings.Get(ShellStrings.Capacity_Blurb);
    public string ReservedLabel => ShellStrings.Get(ShellStrings.Capacity_Reserved);
    public string ReservedHint => ShellStrings.Get(ShellStrings.Capacity_ReservedHint);
    public string QueueDepthLabel => ShellStrings.Get(ShellStrings.Capacity_QueueDepth);
    public string QueueDepthHint => ShellStrings.Get(ShellStrings.Capacity_QueueDepthHint);
    public string QueueGraceLabel => ShellStrings.Get(ShellStrings.Capacity_Grace);
    public string QueueGraceHint => ShellStrings.Get(ShellStrings.Capacity_GraceHint);

    [ObservableProperty]
    public partial decimal ReservedSlots { get; set; } = ServerConfig.Default.ReservedSlots;

    /// <summary>One below the player limit, and it MOVES with it. Reserving every slot would lock out
    /// everyone who is not staff — the server clamps this on load, and the spinner refuses to get there in
    /// the first place.</summary>
    public decimal ReservedCeiling => Math.Max(0, MaxPlayers - 1);

    /// <summary>0 turns the queue off, and the server goes back to refusing at the door. Said in the hint
    /// rather than hidden behind a checkbox, because it is one number either way.</summary>
    [ObservableProperty]
    public partial decimal QueueDepth { get; set; } = ServerConfig.Default.Queue.MaxDepth;

    [ObservableProperty]
    public partial decimal QueueGraceSeconds { get; set; } = ServerConfig.Default.Queue.GraceSeconds;

    /// <summary>The game port. Same rule as the management section: this describes the server whose
    /// serverconfig.json sits beside this shell, so it is unavailable while attached to a remote one.</summary>
    [ObservableProperty]
    public partial decimal GamePort { get; set; } = Constants.GamePort;

    /// <summary>Empty means <c>data/</c> beside the server, which is what the placeholder says. Stored as
    /// typed rather than resolved to an absolute path, so a relative world folder stays relative.</summary>
    [ObservableProperty]
    public partial string DataDir { get; set; } = "";

    [RelayCommand]
    private void UseDefaultDataDir() => DataDir = "";

    // ── Spawn point ───────────────────────────────────────────────────────────

    public string SpawnHeading => ShellStrings.Get(ShellStrings.World_SpawnHeading);
    public string SpawnBlurb => ShellStrings.Get(ShellStrings.World_SpawnBlurb);
    public string SpawnMapLabel => ShellStrings.Get(ShellStrings.World_SpawnMap);
    public string SpawnXLabel => ShellStrings.Get(ShellStrings.World_SpawnX);
    public string SpawnYLabel => ShellStrings.Get(ShellStrings.World_SpawnY);

    [ObservableProperty] public partial decimal SpawnMap { get; set; } = 1;
    [ObservableProperty] public partial decimal SpawnX { get; set; }
    [ObservableProperty] public partial decimal SpawnY { get; set; }

    // ── War night ─────────────────────────────────────────────────────────────

    public string ScheduleHeading => ShellStrings.Get(ShellStrings.Schedule_Heading);
    public string ScheduleBlurb => ShellStrings.Get(ShellStrings.Schedule_Blurb);
    public string WarNightDayLabel => ShellStrings.Get(ShellStrings.Schedule_WarNightDay);
    public string WarNightHourLabel => ShellStrings.Get(ShellStrings.Schedule_WarNightHour);

    /// <summary>Spelled out rather than left implicit: the weekly boundary is DERIVED from the chosen day,
    /// and an operator moving war night is also moving territory income, season weeks and weekly quests.</summary>
    public string WeekResetNote => ShellStrings.Format(ShellStrings.Schedule_WeekResetNote,
        ("Day", DayName((DayOfWeek)(((int)WarNightDay + 1) % 7))));

    /// <summary>Day names come from the SHELL's chosen locale, not the machine's — the two are separate
    /// settings, and a French operator on an English box picked French for a reason.</summary>
    public IReadOnlyList<DayChoice> AvailableDays { get; private set; } = BuildDays("en");

    private static DayChoice[] BuildDays(string locale) =>
        [.. Enum.GetValues<DayOfWeek>().Select(d => new DayChoice(d, DayName(d, locale)))];

    private static string DayName(DayOfWeek day, string? locale = null)
    {
        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(locale ?? ShellStrings.CurrentLocale)
                .DateTimeFormat.GetDayName(day);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return day.ToString();
        }
    }

    [ObservableProperty] public partial DayOfWeek WarNightDay { get; set; } = DayOfWeek.Saturday;
    [ObservableProperty] public partial decimal WarNightHour { get; set; } = 20;

    partial void OnWarNightDayChanged(DayOfWeek value)
    {
        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(WeekResetNote));
    }

    /// <summary>The picker binds here rather than to <see cref="WarNightDay"/> so the stored value stays a
    /// plain <see cref="DayOfWeek"/> — rebuilding the list on a language change then cannot orphan the
    /// selection against a stale instance.</summary>
    public DayChoice? SelectedDay
    {
        get => AvailableDays.FirstOrDefault(d => d.Value == WarNightDay);
        set { if (value is not null) WarNightDay = value.Value; }
    }

    /// <summary>One row in the day picker. A named type, for the same reason as
    /// <see cref="LanguageChoice"/>: compiled bindings cannot bind a tuple.</summary>
    public sealed record DayChoice(DayOfWeek Value, string DisplayName);

    // ── Remote management, as the server's own setting ────────────────────────

    public string ManagementHeading => ShellStrings.Get(ShellStrings.Management_Heading);
    public string ManagementBlurb => ShellStrings.Get(ShellStrings.Management_Blurb);
    public string ManagementEnableLabel => ShellStrings.Get(ShellStrings.Management_Enable);
    public string ManagementEnableHint => ShellStrings.Get(ShellStrings.Management_EnableHint);
    public string ManagementPortLabel => ShellStrings.Get(ShellStrings.Management_Port);
    public string ManagementTokenLabel => ShellStrings.Get(ShellStrings.Management_Token);
    public string ManagementTokenHint => ShellStrings.Get(ShellStrings.Management_TokenHint);
    public string ManagementLocalOnlyNotice => ShellStrings.Get(ShellStrings.Management_LocalOnly);

    /// <summary>Everything written to the serverconfig.json BESIDE THIS SHELL — the port, the world folder,
    /// the rules, remote access. Attached to a remote server that file is not the one being run, so those
    /// sections read as unavailable rather than quietly editing the wrong machine's settings.</summary>
    public bool CanEditServerFile => !IsRemote;

    [ObservableProperty]
    public partial bool ManagementEnabled { get; set; }

    [ObservableProperty]
    public partial decimal ManagementPort { get; set; } = DefaultManagementPort;

    public string CopyLabel => ShellStrings.Get(ShellStrings.Management_Copy);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManagementToken))]
    public partial string ManagementToken { get; set; } = "";

    /// <summary>Nothing to copy before one is minted.</summary>
    public bool HasManagementToken => ManagementToken.Length > 0;

    /// <summary>Mints a token. 32 random bytes in URL-safe base64: long enough that the failure limit on
    /// the listener is belt-and-braces, and short enough to paste into a chat window.</summary>
    [RelayCommand]
    private void GenerateToken() =>
        ManagementToken = System.Security.Cryptography.RandomNumberGenerator.GetHexString(48, lowercase: true);

    /// <summary>Said in the status line rather than a toast: it is the one place on this tab that already
    /// reports what just happened, and a copy with no acknowledgement reads as a dead button.</summary>
    public void ReportTokenCopied() => ConfigStatus = ShellStrings.Get(ShellStrings.Management_TokenCopied);

    [RelayCommand]
    private void SaveConfig()
    {
        // Amended with `with`, not built fresh: this form owns some of the file, not all of it. A new
        // ServerConfig would reset the port and language every time someone pressed Save.
        var (existing, _) = ServerConfigStore.Load(_configPath);
        var config = existing with
        {
            GameName = GameName,
            Port = (int)GamePort,
            // Trimmed, because a path with a stray space is a world folder that silently does not exist.
            DataDir = DataDir.Trim(),
            MaxPlayers = (int)MaxPlayers,
            ReservedSlots = (int)ReservedSlots,
            Records = new RecordLimits
            {
                Items = (int)RecordItems,
                Npcs = (int)RecordNpcs,
                Shops = (int)RecordShops,
                Spells = (int)RecordSpells,
                Quests = (int)RecordQuests,
                Conversations = (int)RecordConversations,
                Maps = (int)RecordMaps,
                MapGroups = (int)RecordMapGroups,
            },
            Queue = new QueueConfig { MaxDepth = (int)QueueDepth, GraceSeconds = (int)QueueGraceSeconds },
            Spawn = new SpawnConfig { Map = (int)SpawnMap, X = (int)SpawnX, Y = (int)SpawnY },
            Schedule = new ScheduleConfig { WarNightDay = WarNightDay, WarNightHour = (int)WarNightHour },
            DeathPenalty = new DeathPenaltyConfig
            {
                DurabilityLoss = DurabilityLoss,
                ItemDrop = ItemDrop,
                ExpLoss = ExpLoss,
            },
            // Unticking writes port 0 rather than clearing the token, so turning remote access back on
            // does not mean redistributing a new secret to everyone who had the old one.
            Management = new ManagementConfig
            {
                Port = ManagementEnabled ? (int)ManagementPort : 0,
                Token = ManagementToken,
            },
        };
        if (ServerConfigStore.Save(_configPath, config) is { } error)
        {
            ConfigStatus = ShellStrings.Format(ShellStrings.Config_SaveFailed, ("Error", error));
            return;
        }

        // The log settings live in the other file, but Save is one button: an operator changing two things
        // on one form and having half of it persist would be the surprise.
        var log = new LogSettings
        {
            MinimumLevel = LogLevel,
            LogOutgoingPackets = LogOutgoingPackets,
            LogIncomingPackets = LogIncomingPackets,
            ServerLogRetentionDays = (int)ServerRetentionDays,
            NetworkLogRetentionDays = (int)NetworkRetentionDays,
            Available = _logKnobs,
        };
        ConfigStatus = AppSettingsStore.Save(_appSettingsPath, log) is { } logError
            ? ShellStrings.Format(ShellStrings.Config_SaveFailed, ("Error", logError))
            : ShellStrings.Format(ShellStrings.Config_Saved, ("Path", _configPath));
    }

    [RelayCommand]
    private void RevertConfig() => LoadConfig();

    // ── Language ──────────────────────────────────────────────────────────────

    /// <summary>The locales with a shell translation on disk, by their own name.</summary>
    public IReadOnlyList<LanguageChoice> AvailableLanguages { get; } =
        ShellStrings.GetAvailableLanguages(ShellLangDir)
            .Select(l => new LanguageChoice(l.Locale, l.DisplayName))
            .ToList();

    private LanguageChoice? _selectedLanguage;

    /// <summary>Applied on selection, unlike the rules: a display preference that needed confirming
    /// would read as broken.</summary>
    public LanguageChoice? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || value.Locale == _selectedLanguage?.Locale) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            ApplyLanguage(value.Locale);
        }
    }

    private void ApplyLanguage(string locale)
    {
        // Carries the current values through so a language change cannot revert another setting.
        var (existing, _) = ServerConfigStore.Load(_configPath);
        string? error = ServerConfigStore.Save(_configPath, existing with { Language = locale });
        if (error is not null)
        {
            ConfigStatus = error;
            return;
        }
        ShellStrings.Load(ShellLangDir, locale);
        BuildCommands();
        // Rebuilt, not re-sorted: the day names come from the new locale. SelectedDay resolves off
        // WarNightDay rather than holding an instance, so the selection survives the swap.
        AvailableDays = BuildDays(locale);
        // Null name = everything changed, which a language swap is. Listing the properties instead would
        // go stale the next time a string is added.
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(null));
        OnStateChanged();   // the state word came from the table too
        ConfigStatus = "";
    }

    /// <summary>lang/shell/, NOT lang/ — the server's own table lives there. See the csproj note.</summary>
    private static string ShellLangDir => Path.Combine(AppContext.BaseDirectory, "lang", "shell");

    /// <summary>One row in the language picker. A named type rather than a tuple so the ComboBox can
    /// bind a DisplayMemberBinding to it under compiled bindings.</summary>
    public sealed record LanguageChoice(string Locale, string DisplayName);

    private void LoadConfig()
    {
        var (config, error) = ServerConfigStore.Load(_configPath);
        GameName = config.GameName;
        GamePort = config.Port;
        DataDir = config.DataDir;
        MaxPlayers = config.MaxPlayers;
        ReservedSlots = config.EffectiveReservedSlots;
        RecordItems = config.Records.Items;
        RecordNpcs = config.Records.Npcs;
        RecordShops = config.Records.Shops;
        RecordSpells = config.Records.Spells;
        RecordQuests = config.Records.Quests;
        RecordConversations = config.Records.Conversations;
        RecordMaps = config.Records.Maps;
        RecordMapGroups = config.Records.MapGroups;
        QueueDepth = config.Queue.MaxDepth;
        QueueGraceSeconds = config.Queue.GraceSeconds;
        SpawnMap = config.Spawn.Map;
        SpawnX = config.Spawn.X;
        SpawnY = config.Spawn.Y;
        WarNightDay = config.Schedule.WarNightDay;
        WarNightHour = config.Schedule.WarNightHour;
        DurabilityLoss = config.DeathPenalty.DurabilityLoss;
        ItemDrop = config.DeathPenalty.ItemDrop;
        ExpLoss = config.DeathPenalty.ExpLoss;
        ManagementEnabled = config.Management.IsEnabled;
        ManagementPort = config.Management.Port > 0 ? config.Management.Port : DefaultManagementPort;
        ManagementToken = config.Management.Token;
        LoadLogSettings();
        // On a malformed file the switches show stock rules, which is what the server would run — the
        // message is what separates that from "these are your settings".
        ConfigStatus = error is null ? "" : ShellStrings.Format(ShellStrings.Config_LoadFailed, ("Error", error));
    }

    public void Dispose() => _server.Dispose();
}
