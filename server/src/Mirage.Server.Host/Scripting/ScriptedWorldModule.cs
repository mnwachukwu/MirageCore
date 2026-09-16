using Mirage.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Serilog;

namespace Mirage.Server.Host.Scripting;

/// <summary>
/// The module that makes a world's own scripts part of the game.
///
/// <para><b>This is the route the engine exists for.</b> Every other seam is reached by writing C#,
/// building an assembly, and listing it — which means a designer cannot try an idea, an operator cannot
/// run a variant, and nothing ships without a toolchain. A world carrying its own <c>scripts/</c> folder
/// needs none of that: the folder is content, it travels with the world, and changing it is an edit and
/// a restart.</para>
///
/// <para>What a module writes is a shared model called <c>Rules</c> with public handlers on it. Each is
/// optional — the engine asks whether one is offered before calling it — so a world that only cares
/// about movement writes one function and nothing else:</para>
///
/// <code>
/// shared model Rules
///     public function Configure(Builder game)
///         game.Attribute("harvest.baskets", "owner");
///         game.Field("harvest.baskets", "Baskets");
///         game.Action("harvest.gather", "Gather here", "Harvest");
///     end function
///
///     public function OnAction(Player who, string action, string on, integer map, integer x, integer y)
///         who.SetNumber("harvest.baskets", who.Number("harvest.baskets") + 1);
///     end function
/// end model
/// </code>
///
/// <para><b>Declaring and reacting are the same two phases a C# module has</b>, and for the same reason:
/// what a module declares shapes the engine that is then built, so it has to be said before anything
/// exists to act on. <see cref="Configure"/> is where a script's declarations are collected;
/// <see cref="Start"/> is where the world arrives.</para>
///
/// <para><b>A world with no scripts is the ordinary case</b>, and so is one whose scripts do not compile:
/// the problems are logged against the world and the server runs the game unscripted. A server that
/// refused to start because somebody's rules had a typo would be a server an operator cannot recover
/// without an editor.</para>
/// </summary>
public sealed class ScriptedWorldModule
    : ICoreModule, IWorldObserver, ITickWork, IActionHandler, IPacketRoute, IConsoleHandler,
      IDeathPolicy, ILingerPolicy, IMovePolicy, IUsePolicy, ILootPolicy, IDisposable
{
    /// <summary>The folder inside a world that holds its rules.</summary>
    public const string ScriptsFolder = "scripts";

    /// <summary>The shared model the engine looks for its handlers on.</summary>
    public const string Rules = "Rules";

    /// <summary>The function a model writes to say it is a kind of record this game authors.
    ///
    /// <para>On the model rather than on <c>Rules</c>, so everything about a kind of record sits in
    /// one place: the fields, what they are called, and what they may hold.</para>
    ///
    /// <para>It must be <c>shared</c>. A model describes a TYPE, and there is no particular record
    /// to describe — so the function needs no receiver, and the engine has no instance to give it.</para></summary>
    public const string Describes = "Describe";


    /// <summary>
    /// Every handler a module may write, and when each is called.
    ///
    /// <para><b>One table, read by everything.</b> What the engine asks the module for, what the
    /// reference page lists, and what the site renders all come from here — so a handler added to the
    /// engine appears in the documentation because it exists, rather than because somebody remembered.
    /// Written twice, the second copy is wrong the first time the first one changes, and the reader
    /// finds out by writing a function nothing ever calls.</para>
    /// </summary>
    public static IReadOnlyList<ScriptHandler> Handlers { get; } =
    [
        new("Configure", 1, "function Configure(Builder game)",
            "once, before the world exists, so a module can declare what it adds"),
        new("OnPlayerJoined", 1, "function OnPlayerJoined(Player who)",
            "a player is in the world and has been sent everything they need"),
        new("OnPlayerLeft", 1, "function OnPlayerLeft(Player who)",
            "they have left, while their record is still readable"),
        new("OnPlayerMoved", 3, "function OnPlayerMoved(Player who, integer fromX, integer fromY)",
            "every accepted step, seam crossings included"),
        new("OnAction", 7,
            "function OnAction(Player who, string action, string on, integer map, integer x, integer y, "
            + "string picked)",
            "the player picked one of this module's own verbs; 'on' names the body it was used on, and "
            + "is blank for a verb offered on a square or on the HUD; 'picked' is the line of a panel's "
            + "list that was selected, and is blank everywhere else", Was: 6),
        new("OnTick", 0, "function OnTick()",
            "the module's tick came round, however often game.TickEvery asked for"),
        new("OnPlayerTick", 1, "function OnPlayerTick(Player who)",
            "the same tick, once for each player in the world, which a script has no other way "
            + "to walk"),
        new("OnConsole", 2, "string function OnConsole(string command, string rest)",
            "somebody typed a command at the SERVER'S OWN console that the engine does not know. "
            + "Yield what it should print, or blank for one this world does not know either. The "
            + "console is the operator's, so there is no rank to check - and there is no seam like "
            + "this for a player's chat. What it is for is forcing work that runs on a schedule "
            + "nobody can sit and watch: a nightly settlement, a season, a war night"),
        new("OnMayRun", 1, "string function OnMayRun(Player who)",
            "somebody is about to take a step at a run; yield a reason to bring them down to a walk, "
            + "or blank to let them run. Asked on every running step, so keep it to reading a number "
            + "off the body"),
        new("OnRan", 1, "function OnRan(Player who)",
            "they took a step at a run, under their own power, onto a tile of the same map. What a run "
            + "costs is charged here - a walk, a warp and a step across a map edge never reach it"),
        new("OnMayDie", 2, "string function OnMayDie(Player who, string cause)",
            "somebody is about to die; yield a reason to stop it, or blank to let it happen"),
        new("OnDied", 3, "function OnDied(Player who, Player killer, string cause)",
            "somebody died, and the body has not moved yet. What a death costs is written here - "
            + "gear wear, a dropped bag, lost experience - and it runs while they are still lying "
            + "where they fell, so anything shed lands on the tile they can go back for. 'killer' is "
            + "nobody when the world itself did it, which Player.IsHere answers. Call "
            + "Player.RespawnAt from in here to say where they come back"),
        new("OnRose", 1, "function OnRose(Player who)",
            "they got up, and are standing where they will actually be. The other end of being put out "
            + "of action: Core puts NOTHING back, so a body comes back exactly as it fell unless this "
            + "says otherwise - full pools, an empty bag, a penalty that lingers. Called after the move, "
            + "so a rule writing onto them is writing onto the body in its new place"),
        new("OnLoot", 1, "function OnLoot(Spoils drop)",
            "a creature is about to drop one line of its table, before the roll. Called once per "
            + "line, so a table of three things calls it three times. Write on what you are handed: "
            + "drop.Rolls sets how often the line lands, drop.Yields how much of it there is, and "
            + "drop.ClaimedBy who may pick it up and for how long. The body leaves its tile the moment "
            + "this returns, so read it now and do not hold on to it"),
        new("OnLinger", 1, "integer function OnLinger(Player who)",
            "their connection dropped; yield how many seconds the body stays in the world"),
        new("OnMessage", 3, "function OnMessage(Player who, string message, Values values)",
            "a client sent one of this game's own messages, carrying the fields its model declared"),
        new("OnNpcSpawned", 1, "function OnNpcSpawned(Npc it)",
            "a creature has just come into the world and is already standing on its tile. This is "
            + "where a creature gets its numbers: Core spawns a body carrying a copy of its template and "
            + "has never heard of health or of what one is worth to kill, so a game with either writes "
            + "them on here. It is also the only place a fresh body can be told apart from the one "
            + "before it, so anything that varies per spawn - a champion, a night-time boost - is "
            + "decided here. Raised for every arrival: the respawn clock, a chase guest coming "
            + "home, and a map being refilled"),
        new("OnContact", 2, "function OnContact(Npc it, Player who)",
            "a creature reached the player it was chasing. Only for a player target \u2014 the "
            + "parameter says Player, and a handler handed the wrong kind of body is worse than one "
            + "that is not called. A creature that reached another creature raises OnNpcContact"),
        new("OnNpcContact", 2, "function OnNpcContact(Npc it, Npc other)",
            "a creature reached the creature it was chasing. Creatures notice each other on their own "
            + "\u2014 anything not its own kind and not sharing its group, within its range \u2014 so a "
            + "world with two hostile species needs nothing but this handler to make them fight"),
        new("OnMayUse", 3, "string function OnMayUse(Player who, integer item, integer slot)",
            "somebody is about to use something out of their bag; yield a reason to stop it, or blank "
            + "to let it happen. Asked before anything happens, unlike OnItemUsed: a rule told "
            + "afterwards can only take the gear off again, and the player sees a flicker. A refusal "
            + "costs them neither the item nor the beat"),
        new("OnItemUsed", 3, "function OnItemUsed(Player who, integer item, integer slot)",
            "they used something out of their bag. Raised for every use, the engine's own two "
            + "included: wearing a piece of gear and opening a door have already happened by the "
            + "time this is called. Everything else an item might mean is a game's, and this is "
            + "where it is written - a scroll that teaches, a potion that heals, a horn that is "
            + "heard across the map"),
        new("OnPlayerWarped", 4,
            "function OnPlayerWarped(Player who, integer fromMap, integer fromX, integer fromY)",
            "they arrived somewhere they did not walk to. A warp is not a step, so it raises "
            + "nothing at OnPlayerMoved - a rule that watched only steps would miss every door, "
            + "every teleport, and every respawn"),
    ];

    private readonly string _worldDir;
    private readonly string _folder;
    private readonly List<string> _actions = [];
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private IWorld? _world;
    private LoadedScript? _loaded;
    private bool _onJoined, _onLeft, _onMoved, _onTick, _onPlayerTick, _onMayDie, _onDied, _onLinger;
    private bool _onLoot;
    private bool _onAction, _onMessage, _onContact, _onNpcContact, _onNpcSpawned, _onItemUsed, _onWarped;
    private bool _onRose;
    private bool _onMayRun, _onRan;
    private bool _onMayUse;

    /// <summary>The messages this world's rules declared, which is also what this route owns.</summary>
    private readonly List<string> _messages = [];

    // How often the tick comes round, which a script sets while it declares. One, until it says
    // otherwise: a module asked more often than it needs is work the loop does for nothing.
    private int _everyTicks = 1;

    /// <param name="worldDir">The world folder; its <c>scripts/</c> subfolder is the module.</param>
    public ScriptedWorldModule(string worldDir)
    {
        ArgumentNullException.ThrowIfNull(worldDir);
        _worldDir = worldDir;
        _folder = Path.Combine(worldDir, ScriptsFolder);
    }

    public string Name => "Scripts";

    /// <summary>What the module said when it was read, whether or not it loaded.</summary>
    public IReadOnlyList<ScriptProblem> Problems { get; private set; } = [];

    /// <summary>Whether a module was found, compiled, and loaded.</summary>
    public bool IsLoaded => _loaded is not null;

    /// <summary>The handlers this world's rules actually offered, by name.
    ///
    /// <para>A handler is matched by name AND arity, so a function whose signature drifts from
    /// <see cref="Handlers"/> is simply not here — it compiles, it loads, and it is never called.
    /// Nothing inside the module can see that, so this is exposed: a test can hold what the script
    /// wrote against what the engine took.</para></summary>
    public IReadOnlyCollection<string> Offered => _offered;

    /// <summary>The action ids the script declared. Empty for a world that declares none.</summary>
    public IReadOnlyCollection<string> Actions => _actions;

    // ── Declaring ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the world's scripts, checks them against what the engine offers, loads them, and lets them
    /// declare.
    ///
    /// <para>Compiling belongs here rather than in <see cref="Start"/> because a script declares, and a
    /// declaration has to be made before the engine is built. The catalog's bindings act through a world
    /// that does not exist yet — which is safe because nothing a script can reach during this phase
    /// carries a <c>Player</c>, and one that somehow does is told so rather than crashing.</para>
    /// </summary>
    public void Configure(ICoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Registered whether or not a world has scripts. All three are no-ops with nothing loaded,
        // and the scripts are not compiled until further down - so a registration conditional on what
        // they offer would be deciding against an empty list.
        builder.AddObserver(this);
        builder.AddTickWork(this);
        builder.AddConsoleHandler(this);

        if (!Directory.Exists(_folder)) return;

        ScriptCatalog catalog = Catalog();
        WriteTheStubs(catalog);

        var (module, problems) = ScriptCompiler.CompileFolder(_folder, catalog);
        Problems = problems;

        // An opinion is the language remarking on how a CORRECT script is written, so it is logged as
        // information. Logged as a warning it reads as a list of faults on a load that had none, and an
        // author learns to skip the whole block — warnings included.
        foreach (ScriptProblem problem in problems)
        {
            if (problem.Severity == ScriptSeverity.Error) Log.Error("Scripts: {Problem}", problem);
            else if (problem.Severity == ScriptSeverity.Warning) Log.Warning("Scripts: {Problem}", problem);
            else Log.Information("Scripts: {Problem}", problem);
        }

        if (module is null)
        {
            if (problems.Count > 0) Log.Error("Scripts: this world's rules did not load; it runs unscripted.");
            return;
        }

        _loaded = LoadedScript.Load(module);

        foreach (ScriptHandler handler in Handlers)
        {
            // The signature as it stands, then the one it used to be. A handler only ever GAINS
            // arguments here, and Compass matches on name AND count - so a world written before the
            // gain is offered the shorter call rather than silently never being called at all, which
            // presents as a game's verbs quietly doing nothing.
            foreach (int arity in handler.Arities)
            {
                if (!_loaded.Offers(Rules, handler.Name, arity)) continue;

                _offered.Add(handler.Name);
                _takes[handler.Name] = arity;
                break;
            }
        }

        Remember();
        Declare(builder);

        // Only when the script actually declared something to handle. A handler claiming no ids is one
        // the engine would keep in a list and never reach.
        if (_actions.Count > 0) builder.AddActionHandler(this);

        // A policy that always allows and never lingers is what the engine already does, so registering
        // one a script did not write would put a call into the death path for no answer.
        if (_onMayDie || _onDied) builder.AddDeathPolicy(this);
        if (_onLinger) builder.AddLingerPolicy(this);
        if (_onMayRun || _onRan) builder.AddMovePolicy(this);
        if (_onMayUse) builder.AddUsePolicy(this);
        if (_onLoot) builder.AddLootPolicy(this);

        // Both halves, or the message goes nowhere. Registering the command lets a line deserialize;
        // the route delivers what it became. The commands were registered while the script declared,
        // so this is the half that had to wait for the list to be complete.
        if (_messages.Count > 0) builder.AddPacketRoute(this);

        Log.Information("Scripts: loaded {Module} ({Handlers} handler(s), {Actions} action(s)).",
                        module.Name, _offered.Count(h => h != "Configure"), _actions.Count);
    }

    /// <summary>
    /// Writes the engine's own types into the world, so a checker outside the server can read them.
    ///
    /// <para>Without this, every declaring line in a world's rules is reported as an unknown
    /// type by <c>cm check</c> and by the VS Code extension — <c>Builder</c>, <c>Player</c> and the
    /// rest exist only while a server is running. An author told their correct code is wrong on every
    /// line that matters learns to ignore the tooling.</para>
    ///
    /// <para>A world that cannot be written to still runs. A read-only world, a locked file, a
    /// folder somebody is watching — none of those is a reason to refuse to serve the game.</para>
    /// </summary>
    private void WriteTheStubs(ScriptCatalog catalog)
    {
        try
        {
            // Every folder holding scripts, because a project's `source` does not descend.
            var folders = Directory.EnumerateDirectories(_folder, "*", SearchOption.AllDirectories)
                .Prepend(_folder)
                .Where(d => Directory.EnumerateFiles(d, "*" + ScriptModule.Extension).Any())
                .Select(d => Path.GetRelativePath(_worldDir, d).Replace('\\', '/'))
                .OrderBy(d => d, StringComparer.Ordinal);

            if (ScriptStubs.Write(_worldDir, catalog, folders))
            {
                Log.Information("Scripts: wrote {Folder}/ and {Project} so an editor can check this world.",
                                ScriptStubs.Folder, ScriptStubs.ProjectFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Scripts: could not write the editor stubs into {World} - {Why}. The world "
                        + "still runs; an editor will not know the engine's types.", _worldDir, ex.Message);
        }
    }

    /// <summary>Lets the script declare, with each declaration guarded on its own.
    ///
    /// <para><b>A collision cannot be allowed to stop the server.</b> Two modules claiming one
    /// attribute key is an error the engine raises at startup, which is right when both are assemblies
    /// somebody built — and wrong when one of them is content a stranger wrote, because the operator is
    /// then holding a server that will not start and a world they did not author.</para>
    ///
    /// <para>So a declaration that collides is refused, named in the log, and the rest are still made.
    /// What it is NOT is silent: the engine's rule against last-one-wins is a rule against nobody being
    /// told, and the line says which declaration lost and to whom.</para>
    /// </summary>
    private void Declare(ICoreBuilder builder)
    {
        var declaring = new Declaring(
            builder, _actions, _messages, _loaded!.Models, n => _everyTicks = n);

        DescribeRecords(declaring);

        if (_offered.Contains("Configure"))
        {
            ScriptOutcome outcome = _loaded.Call(Rules, "Configure", declaring);

            if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());

            if (outcome.Fault is not null)
            {
                Log.Error("Scripts: Configure failed - {Fault}", outcome.Fault);
            }
        }

        declaring.Close();

        // Held so Feed can tell a channel this world declared from a name somebody mistyped.
        _declared = new ChatChannelSet([.. declaring.Channels]);

        foreach (string refused in declaring.Refused)
        {
            Log.Error("Scripts: {Refused}", refused);
        }

        Problems = [.. Problems, .. declaring.Refused.Select(
            r => new ScriptProblem("MS0004", ScriptSeverity.Warning, r, _loaded.Name, 0, 0))];
    }


    /// <summary>
    /// Lets every model that describes itself declare the records it is.
    ///
    /// <para><b>Describe has to be shared, and one that is not is refused BY NAME.</b> A model
    /// describes a TYPE, so there is no particular record to hand the function and the engine has no
    /// instance to give it. An instance function of that name compiles, loads, and is never called —
    /// which from inside the module looks exactly like working code.</para>
    /// </summary>
    private void DescribeRecords(Declaring declaring)
    {
        foreach (ScriptModelInfo model in _loaded!.Models)
        {
            if (model.Function(Describes, 1) is not { } describe) continue;

            if (!describe.IsShared)
            {
                declaring.Refuse($"the records '{model.Name}'",
                    $"{Describes} has to be shared - write 'public shared function {Describes}' - "
                    + "because a model configures the record type, not one record");
                continue;
            }

            if (declaring.Begin(model) is { } describing)
            {
                ScriptOutcome outcome = _loaded.Call(model.Name, Describes, describing);

                if (outcome.Fault is not null)
                {
                    Log.Error("Scripts: {Model}.{Describes} failed - {Fault}",
                              model.Name, Describes, outcome.Fault);
                }
            }
        }
    }

    // The flags the event path reads. Taken from the set rather than asked of it each time, because
    // one of these is checked on every tick and every step of every player.
    private void Remember()
    {
        _onJoined = _offered.Contains(nameof(OnPlayerJoined));
        _onLeft = _offered.Contains(nameof(OnPlayerLeft));
        _onMoved = _offered.Contains(nameof(OnPlayerMoved));
        _onAction = _offered.Contains("OnAction");
        _onTick = _offered.Contains("OnTick");
        _onPlayerTick = _offered.Contains("OnPlayerTick");
        _onMayDie = _offered.Contains("OnMayDie");
        _onMayRun = _offered.Contains("OnMayRun");
        _onRan = _offered.Contains("OnRan");
        _onDied = _offered.Contains("OnDied");
        _onLoot = _offered.Contains("OnLoot");
        _onRose = _offered.Contains("OnRose");
        _onLinger = _offered.Contains("OnLinger");
        _onMessage = _offered.Contains("OnMessage");
        _onContact = _offered.Contains("OnContact");
        _onNpcContact = _offered.Contains("OnNpcContact");
        _onNpcSpawned = _offered.Contains("OnNpcSpawned");
        _onItemUsed = _offered.Contains("OnItemUsed");
        _onMayUse = _offered.Contains("OnMayUse");
        _onWarped = _offered.Contains("OnPlayerWarped");
    }

    /// <summary>Hands the module the world. Everything a binding does goes through this.</summary>
    public void Start(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    // ── What the world tells it ───────────────────────────────────────────────

    /// <summary>
    /// A creature reached what it was chasing, routed by what it caught.
    ///
    /// <para><b>Two handlers rather than one, because the target is two different kinds of body.</b> A
    /// single handler would have to name one of them in its signature and be handed the other, and a
    /// rule that runs on the wrong kind of body is worse than one that is not called. A game that
    /// answers both the same way writes one function and calls it from each.</para>
    /// </summary>
    public void OnContact(EntityHandle npc, EntityHandle target)
    {
        if (target.IsPlayer)
        {
            if (_onContact) Run("OnContact", npc, target);
            return;
        }

        if (_onNpcContact && target.IsNpc) Run("OnNpcContact", npc, target);
    }

    /// <summary>A creature is in the world, and this is the moment a game writes its numbers onto it.
    ///
    /// <para>Raised after everyone who can see the body has been sent it, so anything the handler sets
    /// reaches people who already have something to hang it on.</para></summary>
    public void OnNpcSpawned(EntityHandle npc)
    {
        if (_onNpcSpawned) Run("OnNpcSpawned", npc);
    }

    /// <summary>They used something out of their bag.
    ///
    /// <para>Raised for EVERY use, the engine's own two included: wearing a piece of gear and opening a
    /// door have already happened by the time a game hears about it. Everything else an item might mean
    /// has nowhere else to be written — a scroll that teaches, a potion that heals, a horn heard across
    /// the map.</para></summary>
    public void OnItemUsed(EntityHandle who, int itemNum, int invSlot)
    {
        if (_onItemUsed) Run("OnItemUsed", who, (long)itemNum, (long)invSlot);
    }

    /// <summary>They arrived somewhere they did not walk to.
    ///
    /// <para>A warp is not a step, so nothing reaches <c>OnPlayerMoved</c> for one. A rule watching
    /// only steps would miss every door, every teleport, and every respawn.</para></summary>
    public void OnPlayerWarped(EntityHandle who, in WorldPlace from, in WorldPlace to)
    {
        if (_onWarped) Run("OnPlayerWarped", who, (long)from.Map, (long)from.X, (long)from.Y);
    }

    public void OnPlayerJoined(EntityHandle who)
    {
        if (_onJoined) Run(nameof(OnPlayerJoined), who);
    }

    public void OnPlayerLeft(EntityHandle who)
    {
        if (_onLeft) Run(nameof(OnPlayerLeft), who);
    }

    public void OnPlayerMoved(EntityHandle who, in WorldPlace from, in WorldPlace to)
    {
        if (_onMoved) Run(nameof(OnPlayerMoved), who, (long)from.X, (long)from.Y);
    }

    /// <summary>
    /// The player picked one of the script's own verbs.
    ///
    /// <para>The square carries its MAP as well as its coordinates, because a client can name a square on
    /// a neighboring map — everything within the seamless view is pointable, and a handler given only
    /// x and y would act on the wrong tile the moment somebody stood near a border.</para>
    ///
    /// <para><b>How far a verb reaches is the game's question.</b> The engine re-checks that the square is
    /// one the player could see; whether they are close enough to do that particular thing is not
    /// something the engine could know.</para>
    /// </summary>
    public void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at,
                       string picked)
    {
        // The target reaches a script as its NAME rather than as a Player, because the boundary has no
        // way to carry "somebody, or nobody" — a registered type has no optional form. Blank is the
        // answer for a verb offered on a square or on the HUD, which is most of them.
        if (_onAction)
        {
            Run("OnAction", from, actionId, World.NameOf(on),
                (long)at.Map, (long)at.X, (long)at.Y, picked);
        }
    }

    // ── What it does on the tick ──────────────────────────────────────────────

    /// <summary>How often the loop comes back, as the rules asked. Read after they have declared, so
    /// <c>game.TickEvery</c> is what decides it.</summary>
    public int EveryTicks => _everyTicks;

    /// <summary>
    /// The module's tick, and then each player on it.
    ///
    /// <para><b>The per-player handler is the only way a script can walk the roster.</b> Nothing
    /// crosses the boundary as a collection, so a rule about everybody in the world — resting,
    /// starving, healing — has no other shape. The engine asks the world which slots hold somebody
    /// rather than keeping a list of its own, for the same reason a compiled module does: a roster kept
    /// beside the world is a roster that drifts from it.</para>
    /// </summary>
    public void Tick(long tick)
    {
        if (_onTick) Run("OnTick");
        if (!_onPlayerTick) return;

        for (int i = 1; i <= Constants.MaxPlayers; i++)
        {
            var who = EntityHandle.ForPlayer(i);
            if (World.IsInWorld(who)) Run("OnPlayerTick", who);
        }
    }

    // ── What a client sends it ──────────────────────────────────────────

    /// <summary>The messages this world's rules declared. Empty for a world that declared none, which
    /// is why the route is only registered when there is something for it to own.</summary>
    IReadOnlyCollection<string> IPacketRoute.Commands => _messages;

    /// <summary>One of this game's own messages, from a client that knew how to send it.
    ///
    /// <para>The values arrive as the model declared them rather than as the line wrote them, and a
    /// field the model did not name never made it this far — so a sender cannot reach past what the
    /// rules said they may send.</para></summary>
    public void Handle(EntityHandle from, IPacket packet)
    {
        if (_onMessage && packet is ScriptedPacket sent) Run("OnMessage", from, sent.Cmd, sent.Values);
    }

    // ── What it decides ────────────────────────────────────────────────

    /// <summary>Whether somebody dies, asked of the rules.
    ///
    /// <para><b>A handler that fails allows the death.</b> The alternative is a world where nobody
    /// can die because a script has a bug in it, which is worse and much harder to notice — a refusal
    /// looks exactly like a rule working. So silence means yes, and the failure is in the log.</para>
    ///
    /// <para>The cause travels as text because that is what the boundary carries, and a game deciding
    /// on it needs to read it: "drowned" and "starved" are different questions.</para></summary>
    public Refusal MayDie(in Death death)
    {
        if (!_onMayDie) return Refusal.Allow;

        object? said = Ask("OnMayDie", death.Who, death.CauseKey);
        string reason = said as string ?? string.Empty;

        return reason.Length > 0 ? Refusal.Deny(reason) : Refusal.Allow;
    }

    /// <summary>What a death costs, handed to the rules while the body is still where it fell.
    ///
    /// <para>The handler may name where they come back, through <c>Player.RespawnAt</c>. That arrives
    /// here rather than as a result because a place is three numbers and a handler yields one value;
    /// <see cref="RespawnFor"/> is called immediately afterwards and reads what was set.</para>
    ///
    /// <para>A handler that fails is logged, and the death proceeds. Stopping it at this point is not
    /// available — the refusal happens at <see cref="MayDie"/>, before anything has been taken.</para></summary>
    public void OnDied(in Death death)
    {
        if (!_onDied) return;

        _risesAt = Respawn.Default;
        _risen = death.Who;
        Run("OnDied", death.Who, death.Killer, death.CauseKey);
    }

    /// <summary>Where the body comes back: whatever the <c>OnDied</c> handler named, or Core's own
    /// answer when it named nothing. Checked against the body that died, so a place set for one death
    /// cannot be read by the next.</summary>
    public Respawn RespawnFor(in Death death) =>
        _risen == death.Who ? _risesAt : Respawn.Default;

    private Respawn _risesAt;
    private EntityHandle _risen;

    /// <summary>Records a place for the body currently dying. Anything else is ignored, so the setter
    /// is safe to offer on every player rather than only on the one a handler was handed.</summary>
    private void Rises(EntityHandle who, int map, int x, int y)
    {
        if (who.IsSet && who == _risen) _risesAt = new Respawn(map, x, y);
    }

    /// <summary>Whether somebody can still manage a run, asked of the rules.
    ///
    /// <para><b>A handler that fails lets them run</b>, for the same reason a failed death handler
    /// allows the death: a world where nobody can run because a script has a bug in it is worse and
    /// much harder to notice, since a refusal looks exactly like an empty stamina bar.</para></summary>
    public Refusal MayRun(EntityHandle who)
    {
        if (!_onMayRun) return Refusal.Allow;

        string reason = Ask("OnMayRun", who) as string ?? string.Empty;

        return reason.Length > 0 ? Refusal.Deny(reason) : Refusal.Allow;
    }

    /// <summary>A console command Core did not recognize, offered to this world's rules.
    ///
    /// <para>Blank from the script means "not mine" and is returned as null, so the console goes on to
    /// say the command is unknown. A world that wants to answer with nothing has nothing to say and no
    /// reason to be asked.</para></summary>
    public string? Console(string command, string rest)
    {
        if (!_offered.Contains("OnConsole")) return null;

        string answer = Ask("OnConsole", command, rest) as string ?? string.Empty;

        return answer.Length > 0 ? answer : null;
    }

    /// <summary>They ran a step, and the game charges for it.</summary>
    public void OnRan(EntityHandle who)
    {
        if (_onRan) Run("OnRan", who);
    }

    /// <summary>Whether somebody may use a thing, asked of the rules.
    ///
    /// <para><b>A handler that fails ALLOWS the use</b>, for the same reason a failed death handler
    /// allows the death: a world where nothing can be used because a script has a bug in it is worse
    /// and much harder to notice, since a refusal looks exactly like a rule working. Silence means yes,
    /// and the failure is in the log.</para></summary>
    public Refusal MayUse(in Use use)
    {
        if (!_onMayUse) return Refusal.Allow;

        object? said = Ask("OnMayUse", use.Who, (long)use.ItemNum, (long)use.InvSlot);
        string reason = said as string ?? string.Empty;

        return reason.Length > 0 ? Refusal.Deny(reason) : Refusal.Allow;
    }

    /// <summary>They got up, and the game says what they got up with.</summary>
    public void OnRose(EntityHandle who)
    {
        if (_onRose) Run("OnRose", who);
    }

    /// <summary>What one line of a dead creature's table is worth, asked of the rules.
    ///
    /// <para>Handed over rather than asked for and returned, because there are three answers and a
    /// handler that wanted to change one of them would otherwise have to restate the other two. A
    /// handler that fails leaves the line exactly as the world authored it, which is the same "silence
    /// means the engine's own answer" every other policy here takes.</para></summary>
    public void Weigh(Spoil spoil)
    {
        if (_onLoot) Run("OnLoot", spoil);
    }

    /// <summary>How long a dropped player's body stays, asked of the rules. Zero takes them out at
    /// once, as the engine does with no policy at all.</summary>
    public Deadline LingerFor(EntityHandle who)
    {
        if (!_onLinger) return Deadline.None;

        long seconds = Ask("OnLinger", who) as long? ?? 0L;

        return Deadline.InSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), seconds);
    }

    // ── Calling in ────────────────────────────────────────────────────────────

    /// <summary>
    /// One call into the module, with whatever it printed and whatever went wrong ending up in the log
    /// rather than anywhere a player can see.
    ///
    /// <para><b>A handler that fails is logged and the world carries on.</b> This runs in the middle
    /// of a join, a step, or a tick — so throwing from here would mean one game's mistake ending the
    /// event for everybody, and on the tick it would mean ending the world.</para>
    /// </summary>
    private void Run(string handler, params object?[] arguments)
    {
        ScriptOutcome outcome = _loaded!.Call(Rules, handler, Fitting(handler, arguments));

        if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());
        if (outcome.Fault is null) return;

        Log.Error("Scripts: {Handler} failed - {Fault}", handler, outcome.Fault);

        // A handler that fails every time would otherwise fill a log with the same line at the tick
        // rate. What it did once it will do again, so it is asked once more and then left alone.
        if (Repeats(outcome.Fault.Kind)) Disable(handler);
    }

    /// <summary>The same, for a handler whose ANSWER is the point. Null where it failed, which every
    /// caller reads as "the engine's own default" rather than as a decision.</summary>
    /// <summary>Whether a list-valued field on a record permits a number, reading an ABSENT field as
    /// permitting everything.
    ///
    /// <para>That default is what lets a world author only the exceptions: MSR restricts about a third
    /// of its gear and none of its potions, and writing "every class" onto the rest would be a field on
    /// every record to say the thing that is true anyway.</para></summary>
    private bool Allowed(string records, int number, string field, long value) =>
        World.RecordAt(records, number) is not { } row
        || !row.TryGet(field, out AttributeValue held)
        || held.Has(value);

    /// <summary>The channels a game may put a line in, as the script names them.
    ///
    /// <para>🔴 <b>Its own, plus system.</b> Core’s other four are routing for things a game must not be
    /// able to forge: speech a player typed, a whisper between two people, a guild’s private line, an
    /// administrators’ line. A game says what its own events are, never who appeared to have said
    /// something.</para></summary>
    private const string Feeds =
        "Either \"system\" - what just happened, said by the world rather than a person - or a channel "
        + "this game declared with Builder.Channel. A player filters these apart and hides the noisy "
        + "ones, so a game that puts every line in one channel has taken that choice away from them. A "
        + "name that is neither lands in system, and says so in the log.";

    /// <summary>The channel id a line travels on, from the word a script wrote.</summary>
    private string Feed(string named)
    {
        string word = named.Trim();
        if (string.Equals(word, "system", StringComparison.OrdinalIgnoreCase)) return ChatChannels.System;
        if (_declared.Find(word) is not null) return word;

        // ⚠ Said out loud rather than quietly defaulted. A channel nobody declared is a line the player
        // cannot find, and a typo here is otherwise invisible for the life of the world.
        Log.Warning("Scripts: '{Feed}' is not a channel this game declared; the line went to system.", named);
        return ChatChannels.System;
    }

    // What Configure declared, kept so Feed can check a name against it. Set when the builder freezes;
    // empty until then, which is before any line can be sent.
    private ChatChannelSet _declared = ChatChannelSet.Empty;

    private object? Ask(string handler, params object?[] arguments)
    {
        ScriptOutcome outcome = _loaded!.Call(Rules, handler, Fitting(handler, arguments));

        if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());
        if (outcome.Fault is null) return outcome.Value;

        Log.Error("Scripts: {Handler} failed - {Fault}", handler, outcome.Fault);
        if (Repeats(outcome.Fault.Kind)) Disable(handler);

        return null;
    }

    /// <summary>The arguments this world's own version of that handler takes.
    ///
    /// <para>Trimmed rather than padded, because a handler grows by GAINING arguments on the end: the
    /// older signature is a prefix of the newer one, so a world that wrote the older one is handed
    /// exactly what it asked for and never sees the rest.</para></summary>
    private object?[] Fitting(string handler, object?[] arguments)
    {
        if (!_takes.TryGetValue(handler, out int arity) || arity >= arguments.Length) return arguments;

        return arguments[..arity];
    }

    /// <summary>How many arguments each offered handler was written to take. One entry per handler this
    /// world actually wrote.</summary>
    private readonly Dictionary<string, int> _takes = new(StringComparer.Ordinal);

    /// <summary>Whether a fault is the kind that will happen again on the next call with the same
    /// rules — which is all of them except a script raising something for a reason of its own.</summary>
    private static bool Repeats(ScriptFaultKind kind) =>
        kind is ScriptFaultKind.NoSuchHandler or ScriptFaultKind.EngineFailed;

    private void Disable(string handler)
    {
        _offered.Remove(handler);
        Remember();

        Log.Error("Scripts: {Handler} is not being called again.", handler);
    }

    // ── What a script may name ────────────────────────────────────────────────

    /// <summary>
    /// The engine, as a script sees it.
    ///
    /// <para>A <c>Player</c> is opaque: a script holds one, asks it questions, and can never make one or
    /// look inside it. That is what lets the engine change what a player IS without breaking every world
    /// built on it — the handle a binding unwraps is the engine's business and nothing a script wrote
    /// depends on it.</para>
    ///
    /// <para>Everything on it is something <see cref="IWorld"/> already offered. A script reaches exactly
    /// this and nothing else: there is no way to name a type that is not registered, and the language's
    /// own way out — the filesystem, the clock — is refused at compile time.</para>
    ///
    /// <para><c>Builder</c> is the other half: what a script may ADD to the engine, which is a subset of
    /// what <see cref="ICoreBuilder"/> offers a compiled module. It is only usable while the script's own
    /// <c>Configure</c> is running, for the same reason its C# counterpart is.</para>
    /// </summary>
    public ScriptCatalog Catalog() => ScriptCatalog.Declare(c =>
    {
        var player = c.Type("Player",
            "A handle to somebody in the world. It reads the world live, so a handle held across a "
            + "tick still answers about the person it named. One arrives as a handler's first argument "
            + "or from Builder.Find; a script cannot construct one. Check IsHere before using one you "
            + "have held on to.");

        var game = c.Type("Builder",
            "Declares what the game is. Handed to Configure, which runs once before the world exists, "
            + "so nothing here can refer to a player. Calls that hand something back let you configure "
            + "that thing on the lines below.");

        var records = c.Type("Records",
            "A kind of record this game authors, handed to a model's Describe. The model's fields "
            + "become the editor form; this adds captions, bounds and the folder the files live in. "
            + "Describe must be shared: it configures the record type, not one record.");

        var verb = c.Type("Verb",
            "An action the player can take, handed back by Builder.Action. Set where it is offered, "
            + "what key reaches it and what it requires on separate lines, in any order. Picking it "
            + "calls OnAction with the verb's id.");

        var panel = c.Type("Panel",
            "A screen of this game's own, handed back by Builder.Panel. Add its rows, buttons and forms "
            + "on the lines below. A stock client draws it with no build of its own: rows read live off "
            + "the player, and Asks turns a model into a form the player can fill in and send.");

        var npc = c.Type("Npc",
            "A handle to a creature, the counterpart of Player. Get one from World.NpcAt; a script "
            + "cannot construct one. A handle identifies a creature by its spawn post, not its current "
            + "tile, so it stays valid when the creature walks onto another map. Check IsHere before "
            + "using one you have held on to.");

        var here = c.Shared("World",
            "The live world. Reached by name from any handler; unavailable in Configure, which runs "
            + "before the world exists. Where a handle tells you about a body, World goes the other "
            + "way: give it a square and it tells you who is standing there.");

        var values = c.Type("Values",
            "The fields a message carried, handed to OnMessage. Read like a player's own keys: an "
            + "omitted field is absent, not zero, and Has distinguishes the two. Only fields the "
            + "message's model declared appear here.");

        var spot = c.Type("Spot",
            "A square as a single value: map, tile and plane together. Handed back by World.SpreadOver. "
            + "Use it to pass a location around or hold a set of them, which loose coordinates cannot "
            + "do.");

        var mark = c.Type("Marker",
            "Something drawn on a square rather than over a body, handed back by World.Marker. Set its "
            + "ring, label, meter and audience on the lines below; anything you leave unset is not "
            + "drawn. Use it for a flag on a capture point, a ring around a hazard, or a name over a "
            + "doorway.");

        var spoils = c.Type("Spoils",
            "One line of a dead creature's drop table, handed to OnLoot before it is rolled. Read what "
            + "the world authored and write back what this kill should make of it. A line you do not "
            + "change drops exactly as authored, free to whoever reaches it.");

        // What a message carried, read the way a player's own keys are read: a field the line left out
        // is absent rather than zero, and Has is what tells the two apart.
        values
            .Function("Has", ScriptType.Truth, [ScriptType.Text.Named("field")],
                (v, a) => Sent(v).Has(a.AsText(0)),
                "Whether the message carried that field at all, so absence is told from zero.")
            .Function("Number", ScriptType.Integer, [ScriptType.Text.Named("field")],
                (v, a) => Sent(v).TryGet(a.AsText(0), out var n) ? n.AsLong() : 0L,
                "What it carried under that name, or zero where it carried nothing.")
            .Function("Text", ScriptType.Text, [ScriptType.Text.Named("field")],
                (v, a) => Sent(v).TryGet(a.AsText(0), out var t) ? t.AsText() : string.Empty,
                "The same, as text, or empty where it carried nothing. An enumeration field arrives as "
                + "the member's own name.")
            .Function("Truth", ScriptType.Truth, [ScriptType.Text.Named("field")],
                (v, a) => Sent(v).TryGet(a.AsText(0), out var f) && f.AsBool(),
                "The same, as a yes or no. False where it carried nothing.");

        spot
            .Value("Map", ScriptType.Integer, (s, _) => (long)Square(s).Map, "Which map it is on.")
            .Value("X", ScriptType.Integer, (s, _) => (long)Square(s).X, "How far across.")
            .Value("Y", ScriptType.Integer, (s, _) => (long)Square(s).Y, "And how far down.")
            .Value("IsRaised", ScriptType.Truth,
                (s, _) => Square(s).Layer == WorldLayer.Fringe,
                "Whether it is on the raised surface - a bridge, a ledge, a gantry - or on the ground. "
                + "A bridge and the water beneath it share their coordinates, so a rule that acts on a "
                + "square needs this to tell the two apart.");

        // What a mark draws. Every one of these puts the whole mark down again, so a script says as much
        // or as little as it likes, in any order, and what is drawn is whatever it has said.
        mark
            .Action("Label", [ScriptType.Text.Named("text")],
                (m, a) => Marked(m).Label(a.AsText(0)),
                "Writes that over it. Left unsaid, a mark carries no label.")
            .Action("Color",
                [ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"),
                 ScriptType.Integer.Named("blue")],
                (m, a) => Marked(m).Color(a.AsInteger(0), a.AsInteger(1), a.AsInteger(2)),
                "What color it is drawn in. The pennant, the ring and the label all take it, so a side's "
                + "own color is one line.")
            .Action("Ring", [ScriptType.Integer.Named("tiles")],
                (m, a) => Marked(m).Ring(a.AsInteger(0)),
                "Draws the ground within that many tiles of it. Drawn as the staircase of tiles "
                + "actually inside, never as a circle - so the line on the screen and World.InsideMark "
                + "are one statement. Nothing at or below zero draws no ring.")
            .Action("Meter",
                [ScriptType.Integer.Named("value"), ScriptType.Integer.Named("ceiling")],
                (m, a) => Marked(m).Meter(a.AsInteger(0), a.AsInteger(1)),
                "A bar over the mark, that full out of that. Set the ceiling to zero for a mark that "
                + "is only a pin, with no bar at all.")
            .Action("SeenBy", [ScriptType.SetOf(player.AsType).Named("them")],
                (m, a) => Marked(m).SeenBy(Everyone(a, 0)),
                "Makes the mark private to those bodies. Leave it unset and everybody who can see the "
                + "square sees it. Calling it again adds to the list, so a mark for several sides is one "
                + "call per side. The list is a snapshot: somebody who joins after it is placed will not "
                + "see the mark until it is placed again.");

        // Read what the world authored; write what this kill makes of it. Three writers rather than a
        // yielded answer, because a handler with an opinion about one of the three would otherwise have
        // to restate the other two, and the one it restated wrongly would be silent.
        spoils
            .Value("Item", ScriptType.Integer, (s, _) => (long)Dropping(s).ItemNum,
                "What this line drops, by item number.")
            .Value("Many", ScriptType.Integer, (s, _) => (long)Dropping(s).Quantity,
                "How many of it. Only an item that stacks reads this; anything else lands as one "
                + "however large the number is.")
            .Value("Chance", ScriptType.Integer, (s, _) => (long)Dropping(s).ChancePercent,
                "How often the line lands, as a plain percent - one in a hundred at 1, every time at "
                + "100 or more, never at nothing.")
            .Value("Kind", ScriptType.Integer, (s, _) => (long)Dropping(s).Kind,
                "Which creature this was a copy of. What a rule about a kind of creature keys on, and "
                + "it outlives the body - which is about to stop being there.")
            .Value("From", npc.AsType.OrNothing(),
                (s, _) => Dropping(s).Body is { IsSet: true } body ? body : null,
                "The body itself, still standing on the tile it fell on. For this call only: the "
                + "slot is cleared the moment the table finishes rolling, and a handle kept past that "
                + "answers IsHere with no.")
            .Value("Killer", player.AsType.OrNothing(),
                (s, _) => Dropping(s).Killer is { IsPlayer: true } who ? who : null,
                "Who killed it, or nothing when the world itself did.")
            .Action("Rolls", [ScriptType.Integer.Named("chance")],
                (s, a) => { Dropping(s).ChancePercent = (int)a.AsInteger(0); return null; },
                "Sets how often the line lands, as a percentage. Zero or less skips the line without "
                + "rolling it, so this kill owes this player nothing.")
            .Action("Yields", [ScriptType.Integer.Named("many")],
                (s, a) => { Dropping(s).Quantity = (int)a.AsInteger(0); return null; },
                "Makes it that many instead - a doubled purse, a halved one. Read only for an item "
                + "that stacks, as above.")
            .Action("ClaimedBy",
                [player.AsType.Named("who"), ScriptType.Integer.Named("seconds")],
                (s, a) =>
                {
                    var drop = Dropping(s);
                    drop.ClaimedBy = Who(a.As<object>(0));
                    drop.ClaimSeconds = (int)a.AsInteger(1);
                    return null;
                },
                "Holds the drop for that player for that long: nobody else may pick it up until the "
                + "time runs out, and the client shows them whose it is. What stops the person who did "
                + "the work watching somebody else walk off with it. Left unsaid, a drop is free to "
                + "whoever reaches it first.");

        // The reverse of a handle. OnAction hands a game the SQUARE a verb was used on, so
        // without these a game can say what happened and cannot say who it happened to.
        here
            .Function("NpcAt", npc.AsType.OrNothing(),
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y")],
                (_, a) => Standing(a, wanted: EntitySort.Npc),
                "The creature standing on that square, or nothing. A verb declared OnNpc arrives at "
                + "OnAction with the square it was used on, and this is what turns that into the body.")
            // A game is only ever handed ONE body - the one a verb was used on, the one that reached
            // somebody. A rule about the bodies AROUND an event has nothing to start from without this.
            .Function("NpcsNear", ScriptType.SetOf(npc.AsType),
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"),
                 ScriptType.Integer.Named("y"), ScriptType.Integer.Named("tiles")],
                (_, a) => ScriptValue.Set(
                    World.NpcsNear(new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                                   (int)a.AsInteger(3))
                         .Select(h => (object?)h)),
                "Every creature standing within that many tiles of the square, nearest first - which is "
                + "what a rule about the bodies around something starts from: guards answering a call, a "
                + "herd that scatters when one of them is startled. On that map only, so a body one "
                + "tile over a border is close and is not in the answer; ask for each map to reach "
                + "those.")
            .Function("NpcsOn", ScriptType.SetOf(npc.AsType), [ScriptType.Integer.Named("map")],
                (_, a) => ScriptValue.Set(World.NpcsOn((int)a.AsInteger(0)).Select(h => (object?)h)),
                "Every creature standing on that map. Use it for a sweep NpcsNear cannot answer: "
                + "telling every creature that night fell, counting what is still alive, clearing "
                + "something a spell left behind. Visitors on the map are included and natives away "
                + "chasing elsewhere are not, so walking every map hands you each creature once.")
            .Function("PlayersNear", ScriptType.SetOf(player.AsType),
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"),
                 ScriptType.Integer.Named("y"), ScriptType.Integer.Named("tiles")],
                (_, a) => ScriptValue.Set(
                    World.PlayersNear(new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                                      (int)a.AsInteger(3))
                         .Select(h => (object?)h)),
                "Every player standing within that many tiles of the square, nearest first - the mirror "
                + "of NpcsNear, and what a rule about the people around something starts from: who shared "
                + "a kill, who heard a shout, who was standing too close. On that map only, so "
                + "somebody one tile over a border is close and is not in the answer.")
            .Function("PlayerAt", player.AsType.OrNothing(),
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y")],
                (_, a) => Standing(a, wanted: EntitySort.Player),
                "The player standing on that square, or nothing. Answered before a creature when both "
                + "somehow occupy one tile.")
            // Who HEARS something is not who is standing on a tile. The world scrolls
            // contiguously, so somebody on the next map along is looking at this one; an announcement
            // scoped to occupants would let them watch an event happen in silence.
            .Action("Tell", [ScriptType.Text.Named("line")],
                (_, a) => { World.TellEveryone(a.AsText(0)); return null; },
                "Says a line to everybody in the world. For the handful of things that are genuinely "
                + "everyone's business - a season turning, somebody finishing what only one person "
                + "can finish. A game that announces ordinary events this way has an unreadable chat "
                + "log.")
            .Action("Tell", [ScriptType.Text.Named("line"), ScriptType.Text.Named("feed")],
                (_, a) => { World.TellEveryone(a.AsText(0), Feed(a.AsText(1))); return null; },
                "The same, in the feed you name. " + Feeds)
            .Action("TellOn", [ScriptType.Integer.Named("map"), ScriptType.Text.Named("line")],
                (_, a) => { World.TellEveryoneOn((int)a.AsInteger(0), a.AsText(1)); return null; },
                "Says a line to everybody who can see that map - the nearest thing a seamless world "
                + "has to a room. That is wider than the people standing on it: somebody on the next "
                + "map along is looking at this one too.")
            .Action("TellOn",
                [ScriptType.Integer.Named("map"), ScriptType.Text.Named("line"), ScriptType.Text.Named("feed")],
                (_, a) => { World.TellEveryoneOn((int)a.AsInteger(0), a.AsText(1), Feed(a.AsText(2))); return null; },
                "The same, in the feed you name. " + Feeds)
            .Action("TellNear",
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y"),
                 ScriptType.Text.Named("line")],
                (_, a) =>
                {
                    World.TellEveryoneNear(
                        new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                        a.AsText(3));
                    return null;
                },
                "Says a line to everybody within earshot of a square. A tighter audience than TellOn: "
                + "who can hear speech, not who can see the region.")
            .Action("TellNear",
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y"),
                 ScriptType.Text.Named("line"), ScriptType.Text.Named("feed")],
                (_, a) =>
                {
                    World.TellEveryoneNear(
                        new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                        a.AsText(3), Feed(a.AsText(4)));
                    return null;
                },
                "The same, in the feed you name. " + Feeds)
            .Action("Stain",
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y"),
                 ScriptType.Integer.Named("size"), ScriptType.Integer.Named("amount")],
                (_, a) =>
                {
                    World.Stain(
                        new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                        (int)a.AsInteger(3), WorldLayer.Ground, (float)a.AsInteger(4) / 100f);
                    return null;
                },
                "Marks the ground. The stain dries on its own and is drawn to everyone who can see "
                + "the tile; amount is 0 to 100. Unlike a burst it persists, so it is still there when "
                + "somebody walks back. The color is the world's, set once for the whole world.")
            // The audience the other three cannot express. Those are all about PLACE - one body,
            // a region, an earshot - and a guild is not a place.
            .Action("TellThese",
                [ScriptType.SetOf(player.AsType).Named("them"), ScriptType.Text.Named("line")],
                (_, a) =>
                {
                    World.TellThese(Bodies(a, 0), a.AsText(1));
                    return null;
                },
                "Says a line to a set of players, wherever they are. Anybody in the set who has since "
                + "left the world is skipped, not refused, because a set gathered a moment ago may "
                + "already be out of date.")
            .Action("TellThese",
                [ScriptType.SetOf(player.AsType).Named("them"), ScriptType.Text.Named("line"),
                 ScriptType.Text.Named("feed")],
                (_, a) =>
                {
                    World.TellThese(Bodies(a, 0), a.AsText(1), Feed(a.AsText(2)));
                    return null;
                },
                "The same, in the feed you name. " + Feeds)
            .Function("Guildmates", ScriptType.SetOf(player.AsType), [player.AsType.Named("who")],
                (_, a) => ScriptValue.Set(World.GuildmatesOf(Who(a.As<object>(0)))
                                               .Select(h => (object?)h)),
                "Everybody in the world who shares their guild, including them. Empty for somebody in "
                + "no guild - which is not the same as a guild with nobody online, and World.Guild is "
                + "what tells those apart.")
            .Function("Party", ScriptType.SetOf(player.AsType), [player.AsType.Named("who")],
                (_, a) => ScriptValue.Set(World.PartyOf(Who(a.As<object>(0)))
                                               .Select(h => (object?)h)),
                "Everybody in their party, including them. Empty for somebody in no party.")
            // A game is given squares constantly - a verb was used on one, a body is standing on one -
            // and could learn nothing about what is there. Each of these is the engine answering a
            // question it already answers for itself a hundred times a tick.
            .Function("TileAt", ScriptType.Text,
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y")],
                (_, a) => World.TileAt(Where(a)),
                "What kind of ground is there: 'walkable', 'blocked', 'warp', 'item', 'npcavoid', "
                + "'door', 'plate', or 'ramp'. Empty for a square that is not on a real map. The "
                + "ground layer - a bridge deck is a different answer at the same coordinates.")
            .Function("CanSee", ScriptType.Truth,
                [ScriptType.Integer.Named("fromMap"), ScriptType.Integer.Named("fromX"), ScriptType.Integer.Named("fromY"),
                 ScriptType.Integer.Named("toMap"), ScriptType.Integer.Named("toX"), ScriptType.Integer.Named("toY")],
                (_, a) => World.CanSee(Where(a), Where(a, 3)),
                "Whether a straight line between two squares crosses anything that stops sight. This "
                + "is the same trace the client colors its target arrow with, so a rule gating on it "
                + "agrees exactly with what the player was shown. A wall stops sight only if it was "
                + "authored to: a railing blocks walking and not seeing.")
            .Function("Distance", ScriptType.Integer,
                [ScriptType.Integer.Named("fromMap"), ScriptType.Integer.Named("fromX"), ScriptType.Integer.Named("fromY"),
                 ScriptType.Integer.Named("toMap"), ScriptType.Integer.Named("toX"), ScriptType.Integer.Named("toY")],
                (_, a) => (long)World.Distance(Where(a), Where(a, 3)),
                "How far apart two squares are, in tiles, counting across map borders. The world "
                + "scrolls contiguously, so a body one tile over a border is one tile away; subtracting "
                + "coordinates would call it another map and unreachable. Use this for any range rule. "
                + "-1 when the two are too far apart to compare.")
            .Function("TimeOfDay", ScriptType.Text, [],
                (_, _) => World.TimeOfDay(),
                "What time of day it is: 'day', 'dusk', 'night' or 'dawn'. The engine runs the cycle "
                + "and the client paints it. Read it for anything that changes after dark - creatures "
                + "that hunt at night, a shop that shuts, a spell that needs a moon. One answer for the "
                + "whole world, unlike the weather, which is per map.")
            .Function("Weather", ScriptType.Text, [ScriptType.Integer.Named("map")],
                (_, a) => World.WeatherOn((int)a.AsInteger(0)),
                "What the sky is doing over that map: 'clear', 'rain', 'snow', 'heatwave', or "
                + "'heavywind'. Empty for a map that is not there.")

            // The engine already RUNS a guild - founding, membership, ranks, applications, a vault,
            // and the ledger of who paid in. What it has no opinion about is what a guild DOES.
            .Function("GuildOf", ScriptType.Integer, [player.AsType.Named("who")],
                (_, a) => (long)World.GuildNumber(Who(a.As<object>(0))),
                "Which guild they belong to, as its number, or zero for none. Rules key on the number "
                + "because a guild is a record, not a body: it has no place, nothing walks it, and it "
                + "outlives every member. Player.Guild gives the name, which is for showing people.")
            .Function("GuildNamed", ScriptType.Integer, [ScriptType.Text.Named("name")],
                (_, a) => (long)World.GuildNamed(a.AsText(0)),
                "The guild with that name, or zero. Case-insensitive, the way the engine's own founding "
                + "check compares - so a rule acting on a name a player typed asks the same question "
                + "the engine did when it refused a second guild by that name.")
            .Function("GuildName", ScriptType.Text, [ScriptType.Integer.Named("guild")],
                (_, a) => World.GuildName((int)a.AsInteger(0)),
                "What that guild is called, or empty for a number naming none.")
            .Function("Access", ScriptType.Text, [player.AsType.Named("who")],
                (_, a) => World.AccessOf(Who(a.As<object>(0))),
                "What their account may do to the world: 'player', 'monitor', 'mapper', 'developer' "
                + "or 'creator'. Whoever runs a world usually wants its staff outside its rules - no "
                + "fighting, no loot, no place on a ladder - and which rules that means is yours to "
                + "decide. Empty for a body that is not here.")
            .Function("GuildRank", ScriptType.Text, [player.AsType.Named("who")],
                (_, a) => World.GuildRankOf(Who(a.As<object>(0))),
                "What rank they hold: 'leader', 'officer', 'member', or empty for somebody in no "
                + "guild. The engine keeps the rank and moves it; what a rank may do is yours.")
            .Function("GuildNumber", ScriptType.Integer,
                [ScriptType.Integer.Named("guild"), ScriptType.Text.Named("key")],
                (_, a) => World.GuildValues((int)a.AsInteger(0)) is { } bag
                          && bag.TryGet(a.AsText(1), out AttributeValue held) ? held.AsLong() : 0L,
                "One of the values your game hangs on a guild - a war, a level, a season score. Zero "
                + "for a key it does not carry, and for a number naming no guild.")
            .Function("GuildText", ScriptType.Text,
                [ScriptType.Integer.Named("guild"), ScriptType.Text.Named("key")],
                (_, a) => World.GuildValues((int)a.AsInteger(0)) is { } bag
                          && bag.TryGet(a.AsText(1), out AttributeValue held) ? held.AsText() : string.Empty,
                "The same, as text.")
            .Action("SetGuildNumber",
                [ScriptType.Integer.Named("guild"), ScriptType.Text.Named("key"), ScriptType.Integer.Named("amount")],
                (_, a) =>
                {
                    World.SetGuildValue((int)a.AsInteger(0), a.AsText(1), AttributeValue.From(a.AsInteger(2)));
                    return null;
                },
                "Writes one of them, and gets the guild onto disk. Saved on every write, because a "
                + "guild is not a body: nothing logs it out, so there is no later moment where its "
                + "values would be written anyway.")
            .Action("SetGuildText",
                [ScriptType.Integer.Named("guild"), ScriptType.Text.Named("key"), ScriptType.Text.Named("value")],
                (_, a) =>
                {
                    World.SetGuildValue((int)a.AsInteger(0), a.AsText(1), AttributeValue.From(a.AsText(2)));
                    return null;
                },
                "The same, with text.")
            .Function("GuildMembers", ScriptType.SetOf(player.AsType), [ScriptType.Integer.Named("guild")],
                (_, a) => ScriptValue.Set(World.MembersOf((int)a.AsInteger(0)).Select(h => (object?)h)),
                "Everybody in the world who belongs to that guild. Empty for one with nobody online, "
                + "which is not the same as a guild that is not there.")
            .Function("Guilds", ScriptType.SetOf(ScriptType.Integer), [],
                (_, _) => ScriptValue.Set(World.Guilds().Select(g => (object?)(long)g)),
                "Every guild there is, by number. what anything ranked starts from: every other guild "
                + "call takes a number you already had, off a body or off a name, and a standing, a "
                + "league table or a sweep over all of them has none. Counting upward and hoping does "
                + "not work either - a guild that disbanded leaves a hole in the numbering.")
            .Function("WhoIs", player.AsType.OrNothing(), [ScriptType.Text.Named("account")],
                (_, a) => World.WhoIs(a.AsText(0)) is { IsSet: true } who ? who : null,
                "Whoever is signed in to that account right now, or nothing. Use it to reach the "
                + "person again after writing an account down; nothing means they are offline, so post "
                + "to them instead of telling them.")
            .Function("AccountsIn", ScriptType.SetOf(ScriptType.Text),
                [ScriptType.Integer.Named("guild")],
                (_, a) => ScriptValue.Set(World.AccountsIn((int)a.AsInteger(0)).Select(s => (object?)s)),
                "Every account in a guild, signed in or not. World.GuildMembers answers only with the "
                + "members currently online; this is the whole roster, which outlives their sessions. "
                + "Use it for anything about the guild itself: a dividend, a census, a rule about who "
                + "has stopped turning up.")
            .Function("IsActiveIn", ScriptType.Truth,
                [ScriptType.Integer.Named("guild"), ScriptType.Text.Named("account")],
                (_, a) => World.IsActiveIn((int)a.AsInteger(0), a.AsText(1)),
                "Whether that account is an active member of the guild or just a name on its roster: "
                + "signed in for long enough, recently enough, by the engine's own measure. Ask it "
                + "before counting somebody - who votes, who makes a quorum, how big a guild really "
                + "is.")
            .Function("MailTo", ScriptType.Truth,
                [ScriptType.Text.Named("account"), ScriptType.Text.Named("subject"),
                 ScriptType.Text.Named("body")],
                (_, a) => World.MailTo(a.AsText(0), a.AsText(1), a.AsText(2)),
                "Sends an account a letter, whether or not anybody is signed in to it. What World.Mail "
                + "cannot do: reach somebody who is not here. A rule that wrote an account down when it "
                + "had the person settles up afterwards, and they find it waiting.")
            .Function("MailItemTo", ScriptType.Truth,
                [ScriptType.Text.Named("account"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("many"), ScriptType.Text.Named("subject"),
                 ScriptType.Text.Named("body")],
                (_, a) => World.MailTo(a.AsText(0), a.AsText(3), a.AsText(4),
                                       (int)a.AsInteger(1), (int)a.AsInteger(2)),
                "The same, with something attached. A sale settled, a refund, a prize drawn while they "
                + "were away.")
            .Function("Mail", ScriptType.Truth,
                [player.AsType.Named("who"), ScriptType.Text.Named("subject"),
                 ScriptType.Text.Named("body")],
                (_, a) => World.Mail(Who(a.As<object>(0)), a.AsText(1), a.AsText(2)),
                "Sends them a letter. the one thing A rule can say that outlives the moment: a line of "
                + "chat is gone when they log out, and a letter waits - through a logout, a restart, and "
                + "a server that was down for a week.")
            .Function("MailItem", ScriptType.Truth,
                [player.AsType.Named("who"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("many"), ScriptType.Text.Named("subject"),
                 ScriptType.Text.Named("body")],
                (_, a) => World.Mail(Who(a.As<object>(0)), a.AsText(3), a.AsText(4),
                                     (int)a.AsInteger(1), (int)a.AsInteger(2)),
                "The same, with an item attached, which waits in the message until it is collected. "
                + "Use it for a refund, a prize, a delivery, or a payout too big for a bag. Player.Give "
                + "is the alternative and needs room in the bag right now.")
            .Function("MailMembers", ScriptType.Integer,
                [ScriptType.Integer.Named("guild"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("many"), ScriptType.Text.Named("subject"),
                 ScriptType.Text.Named("body"), ScriptType.Truth.Named("onlyActive")],
                (_, a) => (long)World.MailMembers((int)a.AsInteger(0), (int)a.AsInteger(1),
                                                  (int)a.AsInteger(2), a.AsText(3), a.AsText(4),
                                                  a.AsTruth(5)),
                "Sends every member of a guild that item, and reaches the ones who are not here. The only "
                + "way to pay somebody offline: everything else reaches a body in the world, and what a "
                + "group earned is owed to its members whether or not they happened to be logged in. It "
                + "arrives as mail, so it waits for them. 'onlyActive' narrows it to members who have "
                + "really been playing, by the engine's own measure of a live roster - a payout split "
                + "among a hundred names nobody has used is a payout nobody feels. Yields how many it "
                + "reached.")
            .Function("GuildGold", ScriptType.Integer, [ScriptType.Integer.Named("guild")],
                (_, a) => World.GuildGold((int)a.AsInteger(0)),
                "What is in that guild's vault.")
            .Action("GiveGuildGold",
                [ScriptType.Integer.Named("guild"), ScriptType.Integer.Named("amount")],
                (_, a) =>
                {
                    World.GiveGuildGold((int)a.AsInteger(0), a.AsInteger(1));
                    return null;
                },
                "Puts gold into a vault. The counterpart to SpendGuildGold: a game holding gold aside - "
                + "an escrow, a stake, a bond - has to be able to give it back.")
            // Core spends durability on its own - a swing wears a weapon, a shop restores it - and a
            // game that puts a cost on dying could reach none of it.
            .Function("WornBy", ScriptType.SetOf(ScriptType.Integer), [player.AsType.Named("who")],
                (_, a) => ScriptValue.Set(World.WornBy(Who(a.As<object>(0))).Select(n => (object?)(long)n)),
                "What they are wearing, as item numbers, in slot order. Empty for somebody wearing "
                + "nothing.")
            .Function("BagOf", ScriptType.SetOf(ScriptType.Integer), [player.AsType.Named("who")],
                (_, a) => ScriptValue.Set(World.BagOf(Who(a.As<object>(0))).Select(n => (object?)(long)n)),
                "Which bag slots they have something in, in slot order. Carrying asks about an item; "
                + "this asks about slots, which is what a rule about the bag itself needs - two copies "
                + "of one sword are two slots with their own wear.")
            .Function("ItemInSlot", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Integer.Named("slot")],
                (_, a) => (long)World.InSlot(Who(a.As<object>(0)), (int)a.AsInteger(1)).ItemNum,
                "What is in that bag slot, by item number. Zero for a slot holding nothing.")
            .Function("CountInSlot", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Integer.Named("slot")],
                (_, a) => (long)World.InSlot(Who(a.As<object>(0)), (int)a.AsInteger(1)).Quantity,
                "How many that bag slot holds: the stack size for something that stacks, and one for "
                + "anything else. Zero for a slot holding nothing.")
            .Function("SlotIsWorn", ScriptType.Truth,
                [player.AsType.Named("who"), ScriptType.Integer.Named("slot")],
                (_, a) => World.InSlot(Who(a.As<object>(0)), (int)a.AsInteger(1)).Worn,
                "Whether that bag slot holds the copy they are wearing. A rule about what a death "
                + "scatters asks this, because worn gear and carried gear are dropped by different "
                + "rules in most games.")
            .Function("DropSlot", ScriptType.Truth,
                [player.AsType.Named("who"), ScriptType.Integer.Named("slot"), ScriptType.Integer.Named("many")],
                (_, a) => World.DropFrom(Who(a.As<object>(0)), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                "Puts what is in that bag slot on the ground where they are standing. 'many' takes "
                + "part of a stack; zero takes the whole slot. It lands as a player drop, so anyone "
                + "may pick it up and the world's own limit on litter applies. False for an empty slot.")
            .Function("WornIn", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Text.Named("slot")],
                (_, a) => (long)World.WornIn(Who(a.As<object>(0)), a.AsText(1)),
                "What they are wearing in one slot, by item number. Zero for an empty slot, and for a "
                + "slot this world does not declare. Ask it instead of walking the whole list when you "
                + "care about one place on the body - whether a shield is up, whether a hand is free.")
            .Function("DurabilityLeft", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Integer.Named("item")],
                (_, a) => (long)World.DurabilityOf(Who(a.As<object>(0)), (int)a.AsInteger(1)).Left,
                "How much wear is left in the copy they are wearing. Zero when they are not wearing "
                + "one - two copies in a bag are two different amounts of wear, and this means the one "
                + "that was on them.")
            .Function("DurabilityFull", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Integer.Named("item")],
                (_, a) => (long)World.DurabilityOf(Who(a.As<object>(0)), (int)a.AsInteger(1)).Full,
                "How much that item holds when new. Zero for one with no durability at all, which is "
                + "an ordinary thing for an item to be.")
            .Function("WearOut", ScriptType.Integer,
                [player.AsType.Named("who"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("points")],
                (_, a) => (long)World.Wear(Who(a.As<object>(0)), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                "Wears out that many points of the copy they are wearing, never past nothing. Yields "
                + "how many were actually taken, which is fewer than asked for when it was nearly worn "
                + "out. An item worn to nothing is not destroyed: it stays in the bag, unusable, "
                + "until it is repaired.")
            .Function("RepairCost", ScriptType.Integer,
                [ScriptType.Integer.Named("item"), ScriptType.Integer.Named("points")],
                (_, a) => (long)World.RepairCost((int)a.AsInteger(0), (int)a.AsInteger(1)),
                "What repairing that many points of that item costs, by the engine's own repair rate - "
                + "the same rate a repair shop charges, so a game pricing wear agrees with the shop.")
            .Function("DropAt", ScriptType.Truth,
                [spot.AsType.Named("where"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("many")],
                (_, a) => World.DropAt(Square(a.As<object>(0)), (int)a.AsInteger(1), (int)a.AsInteger(2)),
                "Puts an item on that square out of nowhere, free to whoever reaches it first. Not out "
                + "of anybody's bag - a chest that opens, a reward left where a quest ended, a hoard a "
                + "rule rolled for itself. Player.Drop is the other one, and it moves something that "
                + "already exists.")
            .Function("DropClaimed", ScriptType.Truth,
                [spot.AsType.Named("where"), ScriptType.Integer.Named("item"),
                 ScriptType.Integer.Named("many"), player.AsType.Named("who"),
                 ScriptType.Integer.Named("seconds")],
                (_, a) => World.DropAt(Square(a.As<object>(0)), (int)a.AsInteger(1), (int)a.AsInteger(2),
                                       Who(a.As<object>(3)), (int)a.AsInteger(4)),
                "The same, held for one player for that many seconds: nobody else may pick it up until "
                + "the time runs out, and the client shows them whose it is. What stops the person who "
                + "did the work watching somebody else walk off with it.")
            .Function("RepairRate", ScriptType.Real, [ScriptType.Integer.Named("tier")],
                (_, a) => ScriptValue.Real(World.RepairRateAt((int)a.AsInteger(0))),
                "What one point of durability costs in gold at that tier, priced off a reference "
                + "piece rather than off any particular item. Fractional on purpose: near the bottom of "
                + "the ladder a point is worth less than a coin. Price other kinds of upkeep - a "
                + "reagent, a charge, a ration - against this, and they will track the repair shop "
                + "instead of drifting away from it.")
            .Function("RegionOf", ScriptType.Integer, [ScriptType.Integer.Named("map")],
                (_, a) => (long)World.MapGroupOf((int)a.AsInteger(0)),
                "Which map group that map belongs to, or zero. A group is the engine's idea of a "
                + "region: several maps sharing a name and some settings. A game that owns regions "
                + "asks this to turn where somebody is standing into which region it is. Read what "
                + "your game hangs on one with World.Record(\"MapGroups\", ...).")
            .Function("ExitMap", ScriptType.Integer, [ScriptType.Integer.Named("map")],
                (_, a) => (long)World.ExitFrom((int)a.AsInteger(0)).Map,
                "Where this map puts somebody who leaves it other than by walking, as a map number, "
                + "or zero for one that names none. The map author's say over where leaving this "
                + "place lands you; the engine consults it for nothing on its own, so a game reads it "
                + "and outranks it as it sees fit. Its region answers for a map that says nothing.")
            .Function("ExitX", ScriptType.Integer, [ScriptType.Integer.Named("map")],
                (_, a) => (long)World.ExitFrom((int)a.AsInteger(0)).X,
                "How far across that exit is.")
            .Function("ExitY", ScriptType.Integer, [ScriptType.Integer.Named("map")],
                (_, a) => (long)World.ExitFrom((int)a.AsInteger(0)).Y,
                "And how far down it.")
            .Function("MapNumber", ScriptType.Integer,
                [ScriptType.Integer.Named("map"), ScriptType.Text.Named("field")],
                (_, a) => World.MapValue((int)a.AsInteger(0), a.AsText(1)) is { } held ? held.AsLong() : 0L,
                "One of your own fields on a map, as a whole number - a field you added with "
                + "Records.Extend(\"Maps\"). Answers with the map's own value, falling back to its "
                + "region's where the map leaves it unset, the same way every inherited map property "
                + "works. Zero when neither carries it.")
            .Function("MapText", ScriptType.Text,
                [ScriptType.Integer.Named("map"), ScriptType.Text.Named("field")],
                (_, a) => World.MapValue((int)a.AsInteger(0), a.AsText(1)) is { } held ? held.AsText() : string.Empty,
                "The same, as text.")
            .Function("MapTruth", ScriptType.Truth,
                [ScriptType.Integer.Named("map"), ScriptType.Text.Named("field")],
                (_, a) => World.MapValue((int)a.AsInteger(0), a.AsText(1)) is { } held && held.AsBool(),
                "The same, as a yes or no. An unticked box and an absent one are the same answer.")
            .Action("SetRecordNumber",
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"),
                 ScriptType.Text.Named("field"), ScriptType.Integer.Named("amount")],
                (_, a) =>
                {
                    World.SetRecordValue(a.AsText(0), (int)a.AsInteger(1), a.AsText(2),
                                         AttributeValue.From(a.AsInteger(3)));
                    return null;
                },
                "Writes one of your own fields on a record, and saves it. Where a game keeps what "
                + "belongs to no body and no guild: the last day it settled accounts, a season number, "
                + "who holds a territory. Your own fields only - the engine's properties are written "
                + "through their own paths, which normalize what they are given.")
            .Action("SetRecordText",
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"),
                 ScriptType.Text.Named("field"), ScriptType.Text.Named("value")],
                (_, a) =>
                {
                    World.SetRecordValue(a.AsText(0), (int)a.AsInteger(1), a.AsText(2),
                                         AttributeValue.From(a.AsText(3)));
                    return null;
                },
                "The same, with text.")
            .Function("Now", ScriptType.Integer, [],
                (_, _) => World.Now(),
                "The time now, in seconds since 1970, UTC. What anything dated needs: a cooldown that "
                + "has to survive a restart, a window that stays open for an hour, a daily reset. "
                + "Counting ticks answers a different question, since ticks stop when the server does.")
            .Function("Spot", spot.AsType,
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"),
                 ScriptType.Integer.Named("y")],
                (_, a) => Where(a),
                "That square on the ground, as a value - so a rule can carry it around, keep a set of "
                + "them, and hand it to anything that puts something somewhere.")
            .Function("SpotRaised", spot.AsType,
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"),
                 ScriptType.Integer.Named("y")],
                (_, a) => Where(a) with { Layer = WorldLayer.Fringe },
                "The same square on the raised surface - a bridge, a ledge, a gantry - instead of the "
                + "ground. One tile, two places.")
            .Function("SpreadOver", ScriptType.SetOf(spot.AsType),
                [ScriptType.Integer.Named("region"), ScriptType.Integer.Named("count"),
                 ScriptType.Text.Named("onlyWhere")],
                (_, a) => ScriptValue.Set(
                    World.SpreadOver((int)a.AsInteger(0), (int)a.AsInteger(1), a.AsText(2))
                         .Select(p => (object?)p)),
                "That many squares spread across a region, every one reachable on foot from every other. "
                + "Measured by walking, across the region's seams - not in a straight line, which is a "
                + "lie wherever a wall or water stands between two tiles that are near on paper, and not "
                + "by map number, which piles everything into whichever corner was drawn first. "
                + "'onlyWhere' names one of your own truth fields on Maps and a square goes only on a map "
                + "carrying it; blank puts one anywhere in the region. The walk crosses the whole region "
                + "either way, so a town in the middle of one is walked through. Fewer than asked for "
                + "means there was nowhere else to put one.")
            .Function("Empty", ScriptType.Truth, [ScriptType.Integer.Named("map")],
                (_, a) => World.Empty((int)a.AsInteger(0)),
                "Takes every creature off that map and keeps it that way. For ground that has to stop "
                + "being ordinary for a while: a war fought over it, a ritual nobody should interrupt, an "
                + "arena cleared for a duel. Nothing comes back until World.Wake - which is what "
                + "separates this from clearing a map and watching it refill a minute later.")
            .Function("Refill", ScriptType.Truth, [ScriptType.Integer.Named("map")],
                (_, a) => World.Refill((int)a.AsInteger(0)),
                "Lets the map hold creatures again and spawns its own back immediately, instead of "
                + "leaving it bare until each slot's respawn clock comes round.")
            .Function("IsEmptied", ScriptType.Truth, [ScriptType.Integer.Named("map")],
                (_, a) => World.IsEmptied((int)a.AsInteger(0)),
                "Whether that map is being kept empty of creatures.")
            .Function("Marker", mark.AsType,
                [ScriptType.Text.Named("id"), spot.AsType.Named("where")],
                (_, a) =>
                {
                    var marking = new Marking(World, a.AsText(0), Square(a.As<object>(1)));
                    marking.Put();
                    return marking;
                },
                "Puts a mark on that square and hands it back, so you can set its ring, label, meter "
                + "and audience on the lines below. The equivalent of an overhead bar, for a place. "
                + "Marking again under a name already in use replaces what is there, which is how a "
                + "mark moves and how its meter advances - one call, not a remove and a place.")
            .Function("Unmark", ScriptType.Truth, [ScriptType.Text.Named("id")],
                (_, a) => World.Unmark(a.AsText(0)),
                "Takes one away by name. False when nothing was under it, which is an ordinary answer for "
                + "a rule clearing up after something that ended on its own.")
            .Function("InsideMark", ScriptType.Truth,
                [ScriptType.Text.Named("id"), spot.AsType.Named("where")],
                (_, a) => World.InsideMark(a.AsText(0), Square(a.As<object>(1))),
                "Whether that square is inside the mark's ring. Ask it instead of doing the "
                + "arithmetic yourself, so the ring the player sees and the ring the rule scores stay "
                + "the same one - if they drift apart, the line on screen sits somewhere other than the "
                + "line that counts, and nothing reports it. False for a mark with no ring, and for a "
                + "square on another map.")
            .Function("LocalOffset", ScriptType.Integer, [],
                (_, _) => (long)World.LocalOffset(),
                "How far the server's own civil day is from UTC right now, in seconds - east of it "
                + "positive, west of it negative. anything that turns over at midnight wants this: a "
                + "daily reset, a weekly tax, a season all mean the operator's own midnight, and "
                + "dividing World.Now by a day gives the wrong one everywhere but Greenwich. Add it "
                + "before dividing. Read fresh, so a place that keeps summer time answers differently "
                + "in July than in January - which is what keeps a boundary at midnight all year.")
            .Function("Number", ScriptType.Integer, [ScriptType.Text.Named("key")],
                (_, a) => World.WorldValues().TryGet(a.AsText(0), out var held) ? held.AsLong() : 0L,
                "One of your own values about the world itself, as a whole number, or zero for one "
                + "never written. The place for what belongs to no body and no record - which season "
                + "it is, whether an event is running, how many times something has happened. Read "
                + "back as it was left when the server starts again.")
            .Function("Text", ScriptType.Text, [ScriptType.Text.Named("key")],
                (_, a) => World.WorldValues().TryGet(a.AsText(0), out var held) ? held.AsText() : string.Empty,
                "The same, as text. Empty for one never written.")
            .Function("Truth", ScriptType.Truth, [ScriptType.Text.Named("key")],
                (_, a) => World.WorldValues().TryGet(a.AsText(0), out var held) && held.AsBool(),
                "And as a yes or no. False for one never written.")
            .Action("SetNumber", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("amount")],
                (_, a) => { World.SetWorldValue(a.AsText(0), AttributeValue.From(a.AsInteger(1))); return null; },
                "Writes one. Kept until the world is next written, which the engine does on its own "
                + "cadence and at shutdown.")
            .Action("SetText", [ScriptType.Text.Named("key"), ScriptType.Text.Named("value")],
                (_, a) => { World.SetWorldValue(a.AsText(0), AttributeValue.From(a.AsText(1))); return null; },
                "The same, as text.")
            .Action("SetTruth", [ScriptType.Text.Named("key"), ScriptType.Truth.Named("value")],
                (_, a) => { World.SetWorldValue(a.AsText(0), AttributeValue.From(a.AsTruth(1))); return null; },
                "And as a yes or no.")
            .Function("SpendGuildGold", ScriptType.Truth,
                [ScriptType.Integer.Named("guild"), ScriptType.Integer.Named("amount"),
                 player.AsType.Named("by")],
                (_, a) => World.SpendGuildGold((int)a.AsInteger(0), a.AsInteger(1), Who(a.As<object>(2))),
                "Takes gold out of a vault, recording who spent it. Through the engine's own ledger rather "
                + "than by writing the number: a vault that went down with nothing in the spending log "
                + "is money a guild cannot account for. False when the vault does not hold that much, "
                + "so this is the check as well as the payment.")
            .Function("ShareOf", ScriptType.Integer,
                [ScriptType.Integer.Named("total"), ScriptType.Integer.Named("among"),
                 ScriptType.Integer.Named("which")],
                (_, a) => (long)CurrencySplit.ShareOf((int)a.AsInteger(0), (int)a.AsInteger(1),
                                                      (int)a.AsInteger(2)),
                "What one recipient gets when a purse is split between that many of them, counting from one. "
                + "An even share each, then the leftover one apiece to whoever comes "
                + "first - so the whole purse is handed out and none of it is invented, which a share "
                + "worked out with division and rounding cannot promise. Walk it with 'loop for i = 1 to "
                + "many'. Zero for a position outside the group or a purse with nothing in it. Who stands "
                + "first is YOURS: the odd unit goes to them, so order the party deliberately rather than "
                + "by whoever happens to be nearest.")
            .Function("WalkMs", ScriptType.Integer, [], (_, _) => (long)World.WalkMs,
                "How long one tile takes at a walk, in thousandths of a second. The same for "
                + "everybody, because a walk is a walk - it is Player.RunMs that a body's Pace moves. "
                + "Divide this by theirs to say how much faster running is for them.")
            .Function("KeptNumber", ScriptType.Integer,
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"), ScriptType.Text.Named("field")],
                (_, a) => World.Kept(a.AsText(0), a.AsText(1), a.AsText(2)) is { } held ? held.AsLong() : 0L,
                "One field out of one key of one of your game's own stores. Zero for a store, a key or a "
                + "field that is not there. A store is a set of named bags: World.Number holds what "
                + "there is one of, and this holds what there are many of - a row per player on a "
                + "ladder, a tally per region, whatever your rules pile up while the world runs.")
            .Function("KeptText", ScriptType.Text,
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"), ScriptType.Text.Named("field")],
                (_, a) => World.Kept(a.AsText(0), a.AsText(1), a.AsText(2)) is { } held
                          ? held.AsText() : string.Empty,
                "The same, as text. Empty for one that is not there.")
            .Function("KeptTruth", ScriptType.Truth,
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"), ScriptType.Text.Named("field")],
                (_, a) => World.Kept(a.AsText(0), a.AsText(1), a.AsText(2)) is { } held && held.AsBool(),
                "The same, as a yes or no. False for one that is not there.")
            .Action("SetKeptNumber",
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"),
                 ScriptType.Text.Named("field"), ScriptType.Integer.Named("amount")],
                (_, a) =>
                {
                    World.SetKept(a.AsText(0), a.AsText(1), a.AsText(2), AttributeValue.From(a.AsInteger(3)));
                    return null;
                },
                "Writes one, making the store and the key the first time each is used. Nothing is "
                + "declared and there is no slot count: a store is as big as what you have put in it. "
                + "Kept with the world's own values, so it reaches disk on the world's save beat rather "
                + "than on this call - what has to survive the instant it happens belongs on a character "
                + "or a guild, which save on write.")
            .Action("SetKeptText",
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"),
                 ScriptType.Text.Named("field"), ScriptType.Text.Named("value")],
                (_, a) =>
                {
                    World.SetKept(a.AsText(0), a.AsText(1), a.AsText(2), AttributeValue.From(a.AsText(3)));
                    return null;
                },
                "The same, with text.")
            .Action("SetKeptTruth",
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key"),
                 ScriptType.Text.Named("field"), ScriptType.Truth.Named("value")],
                (_, a) =>
                {
                    World.SetKept(a.AsText(0), a.AsText(1), a.AsText(2), AttributeValue.From(a.AsTruth(3)));
                    return null;
                },
                "The same, with a yes or no.")
            .Function("HasKept", ScriptType.Truth,
                [ScriptType.Text.Named("store"), ScriptType.Text.Named("key")],
                (_, a) => World.HasKept(a.AsText(0), a.AsText(1)),
                "Whether that store holds anything under that key at all - the question to ask before "
                + "counting a zero as a score somebody earned rather than a row that was never written.")
            .Action("Forget", [ScriptType.Text.Named("store"), ScriptType.Text.Named("key")],
                (_, a) =>
                {
                    World.Forget(a.AsText(0), a.AsText(1));
                    return null;
                },
                "Drops that key and every field under it. A store with nothing left in it goes too.")
            .Function("KeptCount", ScriptType.Integer, [ScriptType.Text.Named("store")],
                (_, a) => (long)World.KeptCount(a.AsText(0)),
                "How many keys that store holds. Zero for one nothing was ever put in.")
            .Function("KeptKeyAt", ScriptType.Text,
                [ScriptType.Text.Named("store"), ScriptType.Integer.Named("index")],
                (_, a) => World.KeptKeyAt(a.AsText(0), (int)a.AsInteger(1)),
                "The index-th key of that store, counting from one, or empty past the end. Walk a store "
                + "with 'loop for i = 1 to World.KeptCount(store)'. Ordered by the key itself rather "
                + "than by when it arrived, so a pass reads the same way twice running and the same way "
                + "after a restart.")
            .Function("Records", ScriptType.Integer, [ScriptType.Text.Named("records")],
                (_, a) => (long)World.RecordCount(a.AsText(0)),
                "How many records of that kind this world holds, counting blank slots. Zero for a kind "
                + "nobody declared.")
            .Function("Record", ScriptType.Text,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"), ScriptType.Text.Named("field")],
                (_, a) => World.RecordAt(a.AsText(0), (int)a.AsInteger(1)) is { } row
                          && row.TryGet(a.AsText(2), out AttributeValue held)
                          ? held.AsText() : string.Empty,
                "One field of one record, as text, or empty where the slot or the field is not there. "
                + "What a game reads at run time out of the records its own editor authored.")
            .Function("RecordNumber", ScriptType.Integer,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"), ScriptType.Text.Named("field")],
                (_, a) => World.RecordAt(a.AsText(0), (int)a.AsInteger(1)) is { } row
                          && row.TryGet(a.AsText(2), out AttributeValue held)
                          ? held.AsLong() : 0L,
                "The same, as a whole number. Zero where the slot or the field is not there.")
            .Function("RecordName", ScriptType.Text,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number")],
                (_, a) => World.RecordName(a.AsText(0), (int)a.AsInteger(1)),
                "What a record is called - an item's name, a creature's, a map's. The one engine "
                + "property a record can be asked for, so that a rule choosing between records can say "
                + "which it means. Blank for a slot nobody authored. For your own records this is their "
                + "name field.")
            .Function("RecordAllows", ScriptType.Truth,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"),
                 ScriptType.Text.Named("field"), ScriptType.Integer.Named("value")],
                (_, a) => Allowed(a.AsText(0), (int)a.AsInteger(1), a.AsText(2), a.AsInteger(3)),
                "Whether a field listing several numbers permits that one - and TRUE when the field is "
                + "not there at all, which is how a restriction that was never written means 'anybody'. "
                + "The shape a gate wants: an item naming the classes that may wield it, a spell naming "
                + "the classes that may learn it. One field on the thing itself rather than a second "
                + "family of one row per pair, which is a table to author, a limit to raise, and a walk "
                + "of every row for every question.")
            .Function("RecordHolds", ScriptType.Truth,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"),
                 ScriptType.Text.Named("field"), ScriptType.Integer.Named("value")],
                (_, a) => World.RecordAt(a.AsText(0), (int)a.AsInteger(1)) is { } row
                          && row.TryGet(a.AsText(2), out AttributeValue held)
                          && held.Has(a.AsInteger(3)),
                "The same question asked strictly: whether the field is there AND lists that number. A "
                + "field nobody wrote answers false here, so this is what to use where absent means "
                + "nothing rather than everything.")
            .Function("RecordTruth", ScriptType.Truth,
                [ScriptType.Text.Named("records"), ScriptType.Integer.Named("number"), ScriptType.Text.Named("field")],
                (_, a) => World.RecordAt(a.AsText(0), (int)a.AsInteger(1)) is { } row
                          && row.TryGet(a.AsText(2), out AttributeValue held)
                          && held.AsBool(),
                "The same, as a yes or no, which is what a checkbox on the editor's form writes. False "
                + "where the slot or the field is not there, and an unticked box gives the same answer "
                + "as an absent one.");

        npc
            .Value("Name", ScriptType.Text, (it, _) => World.NameOf(Who(it)),
                "What it is called - the name on its record, trimmed. Blank once the body has left.")
            .Value("IsHere", ScriptType.Truth, (it, _) => World.IsInWorld(Who(it)),
                "Whether the body is still in the world. A handle outlives what it names.")
            .Value("Map", ScriptType.Integer, (it, _) => (long)World.PlaceOf(Who(it)).Map,
                "Which map it is standing on, or zero when it is nowhere.")
            .Value("X", ScriptType.Integer, (it, _) => (long)World.PlaceOf(Who(it)).X,
                "How far across that map it is.")
            .Value("Where", ScriptType.Of("Spot"), (it, _) => World.PlaceOf(Who(it)),
                "The square it is standing on, as a value - map, tile and plane together. What to hand "
                + "anything that puts something where a body is, because a bridge and the water under it "
                + "are the same three numbers and two different places.")
            .Value("Y", ScriptType.Integer, (it, _) => (long)World.PlaceOf(Who(it)).Y,
                "How far down it.")
            .Function("Has", ScriptType.Truth, [ScriptType.Text.Named("key")],
                (it, a) => World.AttributesOf(Who(it))?.Has(a.AsText(0)) ?? false,
                "Whether it carries that key at all, so absence is told from zero.")
            .Function("Number", ScriptType.Integer, [ScriptType.Text.Named("key")],
                (it, a) => Attribute(it, a.AsText(0)) is { } v ? v.AsLong() : 0L,
                "What it carries under that key, or zero where it carries nothing.")
            .Function("Text", ScriptType.Text, [ScriptType.Text.Named("key")],
                (it, a) => Attribute(it, a.AsText(0))?.AsText() ?? string.Empty,
                "The same, as text, or empty where it carries nothing.")
            .Action("SetNumber", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("amount")], (it, a) =>
            {
                World.SetAttribute(Who(it), a.AsText(0), AttributeValue.From(a.AsInteger(1)));
                return null;
            }, "Writes that key, and ships it to everyone entitled to see it.")
            .Action("SetText", [ScriptType.Text.Named("key"), ScriptType.Text.Named("value")], (it, a) =>
            {
                World.SetAttribute(Who(it), a.AsText(0), AttributeValue.From(a.AsText(1)));
                return null;
            }, "The same, with text.")
            .Action("Float",
                [ScriptType.Text.Named("line"), ScriptType.Integer.Named("red"),
                 ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (it, a) =>
                {
                    World.Float(Who(it), a.AsText(0), Packed(a, 1));
                    return null;
                },
                "Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. The one place a script asks the client to draw: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state.")
            .Value("IsEngaged", ScriptType.Truth, (it, _) => World.IsEngaged(Who(it)),
                "Whether they are in a fight right now. What being in one means is yours - holding regen"
                + "through it, refusing a warp out of it - and this is the clock you set, read back.")
            .Value("IsMarked", ScriptType.Truth, (it, _) => World.IsMarked(Who(it)),
                "Whether they carry a mark right now.")
            .Value("IsAggressor", ScriptType.Truth, (it, _) => World.IsAggressor(Who(it)),
                "Whether they are flagged as having started it.")
            .Value("IsWaiting", ScriptType.Truth, (it, _) => World.IsWaiting(Who(it)),
                "Whether they are still held off acting. Measured forward from when the cooldown "
                + "started, matching the direction the bar drawing it fills.")

            // Four of the five. `Down` is a body lying there waiting to get up, and a creature has
            // no such state: one that runs out of health despawns and its slot counts down to a
            // respawn, which Kill and the spawn clock already own.
            .Action("Engage", [ScriptType.Integer.Named("seconds")],
                (it, a) => { World.SetEngaged(Who(it), (int)a.AsInteger(0)); return null; },
                "Marks it as in a fight for that many seconds, which brings up its overhead bars. What "
                + "being in a fight means is the game's; the engine keeps the clock.")
            .Action("Mark", [ScriptType.Integer.Named("seconds")],
                (it, a) => { World.SetMarked(Who(it), (int)a.AsInteger(0)); return null; },
                "Marks it for that many seconds - a flag a game puts on a body and reads back later, "
                + "which is how a kill gets claimed by whoever earned it.")
            .Action("Flag", [ScriptType.Integer.Named("seconds")],
                (it, a) => { World.SetAggressor(Who(it), (int)a.AsInteger(0)); return null; },
                "Marks it as the one that started it, for that many seconds.")
            .Action("Wait", [ScriptType.Integer.Named("seconds")],
                (it, a) => { World.SetActionCooldown(Who(it), (int)a.AsInteger(0)); return null; },
                "Holds it off acting again for that many seconds.")
            // The only draws a game may call. Everything else it does sets state and lets the
            // client decide what that looks like - and a swing is not state, it is a thing that
            // happened once with nothing to derive it from.
            .Action("Sweep", [ScriptType.Truth.Named("connected")],
                (it, a) => { World.Sweep(Who(it), a.AsTruth(0)); return null; },
                "Sweeps a crescent over them, in the direction they are facing. Pass true to fling "
                + "sparks with it, so the swing reads as connecting instead of passing through air. "
                + "What the crescent depicts is up to you: a sword, a claw, a thrown net.")
            .Action("ThrowAtPlayer",
                [player.AsType.Named("at"), ScriptType.Text.Named("look"),
                 ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (it, a) =>
                {
                    World.Throw(Who(it), Who(a.As<object>(0)), Looks(a.AsText(1)), Packed(a, 2));
                    return null;
                },
                "Throws something at a player: 'bolt', 'glitter' or 'parcel', and a color. A "
                + "number floated at the same target waits until it lands, so the hit and the damage "
                + "read as one event - which is most of why this is worth using over a bare burst.")
            .Action("ThrowAtNpc",
                [npc.AsType.Named("at"), ScriptType.Text.Named("look"),
                 ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (it, a) =>
                {
                    World.Throw(Who(it), Who(a.As<object>(0)), Looks(a.AsText(1)), Packed(a, 2));
                    return null;
                },
                "Throws something at another creature: 'bolt', 'glitter' or 'parcel', and a color. A "
                + "number floated at the same target waits until it lands, so the hit and the damage "
                + "read as one event - which is most of why this is worth using over a bare burst.")
            .Action("Burst",
                [ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue"),
                 ScriptType.Integer.Named("power")],
                (it, a) =>
                {
                    World.Burst(Who(it), Packed(a, 0), (float)a.AsInteger(3) / 100f);
                    return null;
                },
                "Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, "
                + "sparks off an anvil, water and dust are one burst with a different color. "
                + "Nothing here lasts; something still there a minute later is World.Stain.")
            .Function("Kill", ScriptType.Truth, [ScriptType.Text.Named("cause")],
                (it, a) => World.Kill(Who(it), EntityHandle.None, a.AsText(0)),
                "Takes it out of the world, with a cause the death policy can read. False for a body "
                + "that was not there, or that something refused to let die.")

            // What KIND of creature this is. Name is what a player reads; this is what a rule keys on.
            .Value("Kind", ScriptType.Integer, (it, _) => (long)World.KindOf(Who(it)),
                "Which creature it is a copy of - the number of its record in this world's creatures. "
                + "What a rule about a species keys on: a bounty per creature, a drop table, which "
                + "bodies a quest counts. Name answers with what a player reads, and two records may "
                + "share a name, so a rule written against one silently follows an editor rename. Zero "
                + "for a body that has left the world.")

            // Pointing a body at somebody. What a creature does ON ITS OWN is authored on its record
            // - hold, amble, close on what it notices, open the gap - and that vocabulary is about
            // walking, deliberately. WHY is the game's, and these two are how it says so.
            .Function("Chase", ScriptType.Truth, [player.AsType.Named("who")],
                (it, a) => World.Provoke(Who(it), Who(a.As<object>(0))),
                "Sends it after a player, whether or not it would have noticed them on its own. This "
                + "is how you write a creature that only fights back: author it to amble, then chase "
                + "whoever hits it. It runs the approach rather than walking in, because it was sent "
                + "rather than tempted. It overrides the noticing and not the legs, so one authored to "
                + "hold its tile still holds it and one authored to open the gap runs away instead. "
                + "False when either body has left the world.")
            .Function("ChaseNpc", ScriptType.Truth, [npc.AsType.Named("other")],
                (it, a) => World.Provoke(Who(it), Who(a.As<object>(0))),
                "The same, at another creature - a guard sent at whatever wandered in, a beast set on "
                + "one it would have ignored. False for a creature sent after itself.")
            .Function("Forget", ScriptType.Truth, [],
                (it, _) => World.Forget(Who(it)),
                "Lets go of whatever it was chasing, leaving it to its record's own behavior again. "
                + "Harmless on a body that was chasing nothing.")

            // What an AUTHOR wrote on the record, as against what this one body is carrying. A rule
            // that treats a chaser differently from an ambler had no way to tell them apart.
            .Value("Behavior", ScriptType.Text, (it, _) => World.BehaviorOf(Who(it)),
                "How it moves on its own: 'stationary', 'wander', 'pursue', 'flee', 'scavenge', or "
                + "'shadow' - which closes to a distance and keeps it. Six ways of walking and "
                + "deliberately nothing about why - a reason is a property of the game, and 'hostile' "
                + "means nothing in a world with no fighting in it. Empty once the body has left.")
            .Value("Group", ScriptType.Integer, (it, _) => (long)World.GroupOf(Who(it)),
                "Which pack it keeps to, or zero for one in none. Two creatures sharing a group never "
                + "notice each other, on top of never noticing their own kind, so a pack does not "
                + "fight itself.")
            .Value("Range", ScriptType.Integer, (it, _) => (long)World.RangeOf(Who(it)),
                "How far it notices anything, in tiles, as its record was authored. Zero for a body "
                + "that notices nobody.")
            .Value("Standoff", ScriptType.Integer, (it, _) => (long)World.StandoffOf(Who(it)),
                "How many tiles back it holds, for a body that keeps its distance: the authored "
                + "number, or what the engine derives from its reach when the record names none. Zero "
                + "for every other behavior, none of which keeps a distance. Use this to aim rather "
                + "than a number of your own, because the body stops where the engine says it stops.")
            .Value("IsChasing", ScriptType.Truth, (it, _) => World.IsChasing(Who(it)),
                "Whether it is after somebody right now - one it noticed, or one you sent it after. The "
                + "other half of Chase and Forget, which write and never read: without this a rule "
                + "cannot tell a creature already in a fight from one standing idle.")
            .Value("Target", player.AsType.OrNothing(),
                (it, _) => World.TargetOf(Who(it)) is { IsPlayer: true } who ? who : null,
                "The person it is after, or nothing - which is also the answer when what it is after "
                + "is another creature. The other half of IsChasing: whether a body is after somebody "
                + "was askable and who was not, and a bolt has to be aimed at something.")
            .Value("TargetNpc", npc.AsType.OrNothing(),
                (it, _) => World.TargetOf(Who(it)) is { IsNpc: true } other ? other : null,
                "And the creature it is after, or nothing. Split in two for the same reason there are "
                + "two contact handlers: a rule handed the wrong kind of body runs and returns "
                + "nonsense, where one that is never called at least returns nothing.");

        verb
            .Action("OnTile", [], (v, _) => Verbal(v).OnTile(),
                "Offer it in the menu of a square. The default, and where most verbs belong.")
            .Action("OnPlayer", [], (v, _) => Verbal(v).OnPlayer(),
                "Offer it in the menu of another player, who arrives as 'on' in OnAction.")
            .Action("OnNpc", [], (v, _) => Verbal(v).OnNpc(),
                "Offer it in the menu of a creature. Declaring one is what gives a plain creature a "
                + "menu at all.")
            .Action("OnHud", [], (v, _) => Verbal(v).OnHud(),
                "Offer it as a button on the HUD, for a verb about the player rather than about "
                + "something they are pointing at.")
            .Action("Aimed", [], (v, _) => Verbal(v).Aimed(),
                "Act on whatever they have TARGETED, when they used it without pointing at anything. "
                + "Targeting is the engine's: Tab picks the next body, Ctrl+Tab picks themselves, and a "
                + "click picks whoever was clicked. So a verb that can go either way - a spell that "
                + "heals or harms - is ONE verb, and aiming it inward needs nothing of yours. Leave it "
                + "off for a verb about a place.")
            .Action("Nowhere", [], (v, _) => Verbal(v).Nowhere(),
                "Offer it nowhere on its own: YOU say where it appears. A button on one of your own "
                + "panels, a choice in one of your conversations, or a key you bound. What a verb "
                + "that belongs to a screen wants - a guild's vault, a training hall - so that "
                + "right-clicking a passing shopkeeper is not how a player reaches it.")
            .Action("Key", [ScriptType.Text.Named("key")], (v, a) => Verbal(v).Key(a.AsText(0)),
                "A key that reaches it without the menu: " + GameKey.Listed + ". The key "
                + "acts on the square the player faces.")
            .Action("Icon", [ScriptType.Text.Named("glyph")], (v, a) => Verbal(v).Icon(a.AsText(0)),
                "The glyph beside it. One of: " + GameIcon.Listed + ". A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section.")
            .Action("Interacts", [], (v, _) => Verbal(v).Interacts(),
                "Picking it also does what the engine's own reach key would have done - a shop, a "
                + "conversation, or the body's own line. A game that binds E takes that key "
                + "outright, so this is how it hands interaction back. Only meaningful on a creature.")
            .Action("Opens", [ScriptType.Text.Named("panel")], (v, a) => Verbal(v).Opens(a.AsText(0)),
                "The panel it opens, by the id given to game.Panel. A panel that was never declared "
                + "is refused by name, so you get an error instead of a button that does nothing.")
            .Action("Hidden", [],
                (v, a) => Verbal(v).Hidden(),
                "Not drawn at all while its condition does not hold, rather than drawn dim. What a verb "
                + "about something a player may never have wants - a guild hall, a mount - since a dim "
                + "entry that never lights up is one they read past every time. On the HUD the buttons "
                + "below it close the gap.")
            .Action("Grayed", [],
                (v, a) => Verbal(v).Grayed(),
                "Drawn dim and unclickable while its condition does not hold, which is what happens "
                + "anyway unless Hidden is asked for. Worth saying out loud on a verb whose condition a "
                + "player can go and satisfy, since the dim entry is how they learn it is there.")
            .Action("NeedsAtLeast", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("least")],
                (v, a) => Verbal(v).NeedsAtLeast(a.AsText(0), a.AsInteger(1)),
                "Offered only to a body carrying at least that much under that key. Below it the verb "
                + "is drawn dim, or not at all if Hidden was asked for.")
            .Action("NeedsCarrying", [ScriptType.Text.Named("key")],
                (v, a) => Verbal(v).NeedsCarrying(a.AsText(0)),
                "Offered only to a body that carries that key at all.")
            .Action("NeedsNothing", [ScriptType.Text.Named("key")],
                (v, a) => Verbal(v).NeedsNothing(a.AsText(0)),
                "Offered only to a body that does not carry that key.");

        panel
            .Action("Key", [ScriptType.Text.Named("key")], (p, a) => Screen(p).Key(a.AsText(0)),
                "A key that opens it: " + GameKey.Listed + ".")
            .Action("Button", [ScriptType.Text.Named("caption"), ScriptType.Text.Named("verb")],
                (p, a) => Screen(p).Button(a.AsText(0), a.AsText(1)),
                "A button along its bottom: a caption, and the id of a verb it calls.")
            .Action("Smallest", [ScriptType.Integer.Named("wide"), ScriptType.Integer.Named("tall")],
                (p, a) => Screen(p).Smallest(a.AsInteger(0), a.AsInteger(1)),
                "How small the player may drag it. Every panel resizes; this is the floor, and it "
                + "stops a drag turning yours into a title bar with nothing under it. Say nothing and "
                + "the engine's own floor applies, which knows nothing about what you put on it.")
            .Action("OnlyWhile", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("least")],
                (p, a) => Screen(p).OnlyWhile(a.AsText(0), a.AsInteger(1)),
                "Only lets it open while that key reads at least that much, and closes it if that stops "
                + "being true while it is up. Its key and any verb that opens it are refused too. What a "
                + "screen about something a player might not HAVE wants - a guild, a mount, a house - "
                + "since otherwise it opens onto blank rows.")
            .Action("HeldWhile", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("least")],
                (p, a) => Screen(p).HeldWhile(a.AsText(0), a.AsInteger(1)),
                "Puts it up while that key reads at least that much, and takes it down when it stops - "
                + "with no close button, because the player did not open it. What a readout wants: a "
                + "score during a fight, the wait over a body that cannot act, the state of ground "
                + "being fought over. It has a slot of its own, so it never pushes aside a window "
                + "somebody opened. Give it a button for the way OUT of whatever it is about - leaving, "
                + "getting up - because closing the window and leaving the thing are not the same act.")
            .Action("Row", [ScriptType.Text.Named("caption"), ScriptType.Text.Named("id")],
                (p, a) => Screen(p).Row(a.AsText(0), a.AsText(1)),
                "One line of a list the player picks ONE of. Both are attribute keys read off the "
                + "player: the first is what the line reads as, the second what it IS - a line saying "
                + "'Ironhelm, at war since Tuesday' and carrying the guild number that names. Whatever "
                + "is picked arrives as 'picked' in OnAction, on whichever button they press next. A "
                + "line whose caption is blank is not drawn, so declare as many as the thing behind "
                + "them can hold and fill the ones that are real. Pass an empty id to let the caption "
                + "be its own. One list to a panel.")
            .Action("Icon", [ScriptType.Text.Named("glyph")],
                (p, a) => Screen(p).Icon(a.AsText(0)),
                "The glyph beside it. One of: " + GameIcon.Listed + ". A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section.")
            .Action("Asks", [ScriptType.Text.Named("modelName"), ScriptType.Text.Named("caption")],
                (p, a) => Screen(p).Asks(a.AsText(0), a.AsText(1)),
                "Asks the player to fill one of this world's own models in, and send it. Every field "
                + "of the model becomes a control — a number a spinner, a truth a checkbox, an "
                + "enumeration a drop-down over its members — and the caption names the button under "
                + "them. What they send arrives at OnMessage under the model's name. A panel asks for "
                + "one message.")
            .Action("Heading", [ScriptType.Text.Named("caption")], (p, a) => Screen(p).Heading(a.AsText(0)),
                "A heading on this panel, separating the rows under it.")
            .Action("Field", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (p, a) => Screen(p).Field(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A row on this panel: a key read live off the player, a caption, and a color as red, "
                + "green, and blue. All three zero leaves the color to the client.")
            .Action("Badge", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (p, a) => Screen(p).Badge(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "The same, drawn as a small colored tag. A key holding a WORD puts the word in the tag "
                + "and the caption beside it; a key holding a yes or no puts the caption itself in the "
                + "tag when it is yes, and draws nothing at all when it is no.")
            .Action("Meter", [ScriptType.Text.Named("key"), ScriptType.Text.Named("outOf"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (p, a) => Screen(p).Meter(a.AsText(0), a.AsText(1), a.AsText(2),
                    (int)a.AsInteger(3), (int)a.AsInteger(4), (int)a.AsInteger(5)),
                "A bar on this panel, filled by one key against another.");

        records
            .Action("Are", [ScriptType.Text.Named("plural"), ScriptType.Text.Named("singular"), ScriptType.Integer.Named("limit")],
                (r, a) => Shape(r).Are(a.AsText(0), a.AsText(1), a.AsInteger(2)),
                "What these records are called in the editor — the plural, then the singular — and how "
                + "many there may be. A caption left blank keeps the model's own name.")
            .Action("Icon", [ScriptType.Text.Named("glyph")],
                (r, a) => Shape(r).Icon(a.AsText(0)),
                "The glyph beside it. One of: " + GameIcon.Listed + ". A name that is not one of those is refused, because a glyph nobody drew is a section that looks like every other section.")
            .Action("Extend", [ScriptType.Text.Named("records")],
                (r, a) => Shape(r).Extend(a.AsText(0)),
                "Adds this model's fields to a family that already exists - the engine's 'Items' or "
                + "'NPCs', or another module's - instead of declaring one of its own. This is how a "
                + "game hangs its own facts on a record the engine owns: which classes may wield a "
                + "sword, which spell is written on a scroll. The engine's own item properties are a "
                + "closed set, because Core cannot act on one it has never heard of; yours are open, "
                + "and they belong on the sword itself rather than in a second table keyed by item "
                + "number. The other family decides what these records are called, where they live and "
                + "how many there may be, so Are and Stored do nothing here.")
            .Action("Stored", [ScriptType.Text.Named("folder"), ScriptType.Text.Named("prefix")],
                (r, a) => Shape(r).Stored(a.AsText(0), a.AsText(1)),
                "Where these records live: the folder under the world, and what each file is called "
                + "before its number. Only needed for records already on disk. A new game leaves it "
                + "out, and the model's name decides.")
            .Action("Caption", [ScriptType.Text.Named("field"), ScriptType.Text.Named("caption")],
                (r, a) => Shape(r).Caption(a.AsText(0), a.AsText(1)),
                "What one field is called on the form. Only needed where the field's own name is not "
                + "the words an author should read.")
            .Action("Range", [ScriptType.Text.Named("field"), ScriptType.Integer.Named("least"), ScriptType.Integer.Named("greatest")],
                (r, a) => Shape(r).Range(a.AsText(0), a.AsInteger(1), a.AsInteger(2)),
                "The bounds of a whole-number field. Equal bounds mean unbounded.")
            .Action("Length", [ScriptType.Text.Named("field"), ScriptType.Integer.Named("characters")],
                (r, a) => Shape(r).Length(a.AsText(0), a.AsInteger(1)),
                "How long a text field may be. Zero means no limit.")
            .Action("Points", [ScriptType.Text.Named("field"), ScriptType.Text.Named("records")],
                (r, a) => Shape(r).Points(a.AsText(0), a.AsText(1)),
                "Makes a whole-number field a picker over another kind of record, listing them by name. "
                + "Use it to point at the engine's own records - 'Items', 'NPCs', 'Maps', 'Shops', "
                + "'Conversations' - which have no model to type a field as. A field typed as one of "
                + "your own models already does this and needs nothing here. The number is still "
                + "what is stored: this changes what the form draws and nothing about what a rule "
                + "reads back.");

        player
            .Action("Message", [ScriptType.Text.Named("line")], (who, a) =>
            {
                World.Tell(Who(who), a.AsText(0));
                return null;
            }, "Sends a line of text to this player, and to nobody else. It lands in the system feed, "
             + "which is where what just happened to somebody belongs; say a feed by name to put it "
             + "anywhere else.")
            .Action("Message", [ScriptType.Text.Named("line"), ScriptType.Text.Named("feed")], (who, a) =>
            {
                World.Tell(Who(who), a.AsText(0), Feed(a.AsText(1)));
                return null;
            }, "The same, in the feed you name. " + Feeds)
            .Value("Name", ScriptType.Text, (who, _) => World.NameOf(Who(who)),
                "Their character's name, trimmed. Blank once the body has left - the same answer a "
                + "creature's name gives, because it is the same question.")
            .Value("IsHere", ScriptType.Truth, (who, _) => World.IsInWorld(Who(who)),
                "Whether they are still in the world. A handle outlives the body it names.")
            .Value("Guild", ScriptType.Text, (who, _) => World.GuildOf(Who(who)),
                "The name of the guild their account belongs to, or empty for none.")
            .Value("Account", ScriptType.Text, (who, _) => World.AccountOf(Who(who)),
                "The account behind them - the one name here that outlives a session. A handle stops "
                + "meaning anything the moment they log out and a character can be deleted; this is what "
                + "the engine files mail and guild membership under, so it is what to write down when "
                + "the thing you are promising will be settled later. not a character name and not for "
                + "showing to players: it is how they sign in. Print Name in anything anybody reads.")
            .Value("Map", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).Map,
                "Which map they are standing on, or zero when they are nowhere.")
            .Value("X", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).X,
                "How far across that map they are.")
            .Value("Where", ScriptType.Of("Spot"), (who, _) => World.PlaceOf(Who(who)),
                "The square they are standing on, as a value - map, tile and plane together. What to hand "
                + "anything that puts something where somebody is, because a bridge and the water under it "
                + "are the same three numbers and two different places.")
            .Value("Y", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).Y,
                "How far down it.")

            // The attribute bag, which is where everything a GAME counts lives. A key a body does not
            // have reads as zero or as empty text, with Has for the question that tells them apart —
            // a rule asking "how much stamina" wants a number, not a decision about absence.
            .Function("Has", ScriptType.Truth, [ScriptType.Text.Named("key")],
                (who, a) => World.AttributesOf(Who(who))?.Has(a.AsText(0)) ?? false,
                "Whether they carry that key at all, so absence is told from zero.")
            .Function("Number", ScriptType.Integer, [ScriptType.Text.Named("key")],
                (who, a) => Attribute(who, a.AsText(0)) is { } v ? v.AsLong() : 0L,
                "What they carry under that key, or zero where they carry nothing.")
            .Function("Text", ScriptType.Text, [ScriptType.Text.Named("key")],
                (who, a) => Attribute(who, a.AsText(0))?.AsText() ?? string.Empty,
                "The same, as text, or empty where they carry nothing.")
            .Action("SetNumber", [ScriptType.Text.Named("key"), ScriptType.Integer.Named("amount")], (who, a) =>
            {
                World.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsInteger(1)));
                return null;
            }, "Writes that key, and ships it to everyone entitled to see it.")
            .Action("SetText", [ScriptType.Text.Named("key"), ScriptType.Text.Named("value")], (who, a) =>
            {
                World.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsText(1)));
                return null;
            }, "The same, with text.")

            .Function("WarpTo", ScriptType.Truth, [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y")],
                (who, a) => World.Warp(Who(who), new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2))),
                "Puts them on that map, x and y. False for a square that is not a real tile.")
            .Action("Give", [ScriptType.Integer.Named("item"), ScriptType.Integer.Named("many")], (who, a) =>
            {
                World.Give(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            }, "Puts that many of an item in their bag.")
            .Action("Take", [ScriptType.Integer.Named("item"), ScriptType.Integer.Named("many")], (who, a) =>
            {
                World.Take(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            }, "Takes that many out of it, worn ones included.")
            .Function("Carrying", ScriptType.Integer, [ScriptType.Integer.Named("item")],
                (who, a) => World.Carrying(Who(who), (int)a.AsInteger(0)),
                "How many of that item they are carrying, worn ones and every stack counted together. "
                + "The read that goes with Give and Take: a rule charging somebody in a currency of "
                + "its own asks this first, because taking more than they have and taking what they "
                + "have look the same afterwards.")
            .Function("Wear", ScriptType.Truth, [ScriptType.Integer.Named("item")],
                (who, a) => World.Wear(Who(who), (int)a.AsInteger(0)),
                "Puts something they are carrying on, taking off whatever was in its slot. A game that "
                + "hands somebody a sword has no other way to put it in their hand - an opening kit, a "
                + "quest reward, a curse that arms them against their will. None of the refusals a "
                + "player pressing the button meets apply: a rule arming somebody mid-fight meant to. "
                + "False for a body not carrying it, and for a piece naming a slot this world does not "
                + "declare.")
            .Function("TakeOff", ScriptType.Truth, [ScriptType.Integer.Named("item")],
                (who, a) => World.Remove(Who(who), (int)a.AsInteger(0)),
                "Takes something off. It stays in the bag. The other half of Wear, and what a rule "
                + "about ruined gear needs: a piece worn down to nothing comes off the body it broke "
                + "on. False for a body not wearing it.")
            .Value("IsRunning", ScriptType.Truth, (who, _) => World.IsRunning(Who(who)),
                "Whether they are running or walking right now. What running costs is up to you; the "
                + "engine moves the body, and this is how a rule finds out.")
            .Value("Pace", ScriptType.Integer, (who, _) => (long)World.PaceOf(Who(who)),
                "How quick this body is. Zero is the baseline everybody starts at and higher is "
                + "faster, with diminishing returns and a ceiling the engine picks - so a game with a "
                + "speed stat writes the stat here and never has to know the curve. It buys a faster "
                + "RUN; a walk is a walk.")
            .Action("SetPace", [ScriptType.Integer.Named("pace")],
                (who, a) => { World.SetPace(Who(who), (int)a.AsInteger(0)); return null; },
                "Says how quick they are. A game with a speed stat has no other way to make it mean "
                + "anything: how far a body gets per second is the engine's, and this is what it "
                + "reads. Write it whenever the stat behind it moves.")
            .Value("RunMs", ScriptType.Integer, (who, _) => (long)World.RunMsOf(Who(who)),
                "How long one tile takes them at a run, in thousandths of a second - what their Pace "
                + "bought. Divide World.WalkMs by it to say how much faster running is.")

            // The five the engine already keeps, and it keeps them for PLAYERS. An NPC's engaged
            // state has nowhere to live yet, so these are here and not on Npc.
            .Action("Engage", [ScriptType.Integer.Named("seconds")],
                (who, a) => { World.SetEngaged(Who(who), (int)a.AsInteger(0)); return null; },
                "Marks them as in a fight for that many seconds. What being in a fight means is the "
                + "game's; the engine keeps the clock and the client shows it.")
            .Action("Down", [ScriptType.Integer.Named("seconds")],
                (who, a) => { World.SetDowned(Who(who), (int)a.AsInteger(0)); return null; },
                "Marks them as out of the fight for that many seconds.")
            .Action("Mark", [ScriptType.Integer.Named("seconds")],
                (who, a) => { World.SetMarked(Who(who), (int)a.AsInteger(0)); return null; },
                "Marks them for that many seconds - a target somebody else's rule put a flag on.")
            .Action("Flag", [ScriptType.Integer.Named("seconds")],
                (who, a) => { World.SetAggressor(Who(who), (int)a.AsInteger(0)); return null; },
                "Marks them as the one who started it, for that many seconds. What that costs them is "
                + "the game's to decide.")
            .Action("Wait", [ScriptType.Integer.Named("seconds")],
                (who, a) => { World.SetActionCooldown(Who(who), (int)a.AsInteger(0)); return null; },
                "Holds them off acting again for that many seconds.")
            .Action("Float",
                [ScriptType.Text.Named("line"), ScriptType.Integer.Named("red"),
                 ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (who, a) =>
                {
                    World.Float(Who(who), a.AsText(0), Packed(a, 1));
                    return null;
                },
                "Floats a line off them, to everybody who can see it happen - a number, a word, a name. The color is red, green and blue, each 0 to 255. The one place a script asks the client to draw: everything else it does sets state and lets the client decide what that looks like, and a number that happened once is not state.")
            .Value("IsEngaged", ScriptType.Truth, (who, _) => World.IsEngaged(Who(who)),
                "Whether they are in a fight right now. What being in one means is yours - holding regen"
                + "through it, refusing a warp out of it - and this is the clock you set, read back.")
            .Value("IsDowned", ScriptType.Truth, (who, _) => World.IsDowned(Who(who)),
                "Whether they are out of action right now.")
            .Value("IsMarked", ScriptType.Truth, (who, _) => World.IsMarked(Who(who)),
                "Whether they carry a mark right now.")
            .Value("IsAggressor", ScriptType.Truth, (who, _) => World.IsAggressor(Who(who)),
                "Whether they are flagged as having started it.")
            .Value("IsWaiting", ScriptType.Truth, (who, _) => World.IsWaiting(Who(who)),
                "Whether they are still held off acting. Measured forward from when the cooldown "
                + "started, matching the direction the bar drawing it fills.")

            // The only draws a game may call. Everything else it does sets state and lets the
            // client decide what that looks like - and a swing is not state, it is a thing that
            // happened once with nothing to derive it from.
            .Action("Sweep", [ScriptType.Truth.Named("connected")],
                (who, a) => { World.Sweep(Who(who), a.AsTruth(0)); return null; },
                "Sweeps a crescent over them, in the direction they are facing. Pass true to fling "
                + "sparks with it, so the swing reads as connecting instead of passing through air. "
                + "What the crescent depicts is up to you: a sword, a claw, a thrown net.")
            .Action("ThrowAtNpc",
                [npc.AsType.Named("at"), ScriptType.Text.Named("look"),
                 ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (who, a) =>
                {
                    World.Throw(Who(who), Who(a.As<object>(0)), Looks(a.AsText(1)), Packed(a, 2));
                    return null;
                },
                "Throws something at a creature: 'bolt', 'glitter' or 'parcel', and a color. A "
                + "number floated at the same target waits until it lands, so the hit and the damage "
                + "read as one event - which is most of why this is worth using over a bare burst.")
            .Action("ThrowAtPlayer",
                [player.AsType.Named("at"), ScriptType.Text.Named("look"),
                 ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (who, a) =>
                {
                    World.Throw(Who(who), Who(a.As<object>(0)), Looks(a.AsText(1)), Packed(a, 2));
                    return null;
                },
                "Throws something at another player: 'bolt', 'glitter' or 'parcel', and a color. A "
                + "number floated at the same target waits until it lands, so the hit and the damage "
                + "read as one event - which is most of why this is worth using over a bare burst.")
            .Action("Burst",
                [ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue"),
                 ScriptType.Integer.Named("power")],
                (who, a) =>
                {
                    World.Burst(Who(who), Packed(a, 0), (float)a.AsInteger(3) / 100f);
                    return null;
                },
                "Bursts droplets from them - power is 0 to 100. Deliberately color-blind: blood, "
                + "sparks off an anvil, water and dust are one burst with a different color. "
                + "Nothing here lasts; something still there a minute later is World.Stain.")
            .Function("Kill", ScriptType.Truth, [ScriptType.Text.Named("cause")],
                (who, a) => World.Kill(Who(who), EntityHandle.None, a.AsText(0)),
                "Takes them out of the world, with a cause OnMayDie can read. False when something "
                + "refused the death, which a death policy is allowed to do.")
            .Action("RespawnAt",
                [ScriptType.Integer.Named("map"), ScriptType.Integer.Named("x"), ScriptType.Integer.Named("y")],
                (who, a) =>
                {
                    Rises(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2));
                    return null;
                },
                "Says where this body comes back. Only from inside OnDied, and only about the "
                + "body that died - it is read the moment that handler returns, and anywhere else it "
                + "does nothing. Say nothing and they come back where the world puts them.")

            // The other half of a verb used on somebody. OnAction carries the target as a name,
            // because the boundary cannot say "somebody, or nobody" in an argument. It can say it in
            // a result, so the lookup is a function rather than a parameter.
            .Function("Find", player.AsType.OrNothing(), [ScriptType.Text.Named("name")],
                (_, a) => Somebody(a.AsText(0)),
                "The body behind a name, or nothing if no one is using it. OnAction hands you a "
                + "name; this turns it into a player you can read and write.");

        game
            .Action("Attribute", [ScriptType.Text.Named("key"), ScriptType.Text.Named("seenBy")],
                (b, a) => Build(b).Attribute(a.AsText(0), a.AsText(1)),
                "Declares a key this game counts, and who may see it: none, owner, or viewport.")
            .Action("Message", [ScriptType.Text.Named("modelName")],
                (b, a) => Build(b).Message(a.AsText(0)),
                "A message a client may send this game, taking its fields from one of this world's "
                + "own models. It arrives at OnMessage with those fields as values. A stock client "
                + "cannot compose one, so this is for a client, a tool, or a bot that knows it.")
            .Action("TickEvery", [ScriptType.Integer.Named("ticks")],
                (b, a) => Build(b).TickEvery(a.AsInteger(0)),
                "How many ticks between calls to OnTick and OnPlayerTick. A tick is 100ms, so the "
                + "default of 1 calls them ten times a second, 10 calls them once a second, and 600 "
                + "calls them once a minute. This sets the rate for the whole module.")
            .Action("EquipSlot", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption")],
                (b, a) => Build(b).EquipSlot(a.AsText(0), a.AsText(1)),
                "A place on a body something can be worn. A caption left blank becomes the key, as "
                + "words.")
            .Action("Heading", [ScriptType.Text.Named("caption")],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Heading,
                    string.Empty, string.Empty, a.AsText(0), 0, 0, 0),
                "A heading on the sidebar, separating the rows under it.")
            .Action("Field", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Text,
                    a.AsText(0), string.Empty, a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A sidebar row: a key read live off the player, a caption, and a color as red, green, "
                + "and blue. All three zero leaves the color to the client.")
            .Action("Badge", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Badge,
                    a.AsText(0), string.Empty, a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "The same, drawn as a small tag with no caption.")
            .Action("Meter", [ScriptType.Text.Named("key"), ScriptType.Text.Named("outOf"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Meter,
                    a.AsText(0), a.AsText(1), a.AsText(2),
                    (int)a.AsInteger(3), (int)a.AsInteger(4), (int)a.AsInteger(5)),
                "A sidebar bar, filled by one key against another.")
            .Action("Slot", [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (b, a) => Build(b).Row(DisplaySurfaces.CharacterSelect, DisplayStyle.Text,
                    a.AsText(0), string.Empty, a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A row on the CHARACTER SELECT screen, beside a saved character's name. The only place "
                + "you can say anything about a body the engine is not running - a level, a class, a "
                + "guild - and without one the screen where somebody picks between three characters "
                + "offers three names and a sprite. Read off the saved character, so anything you want "
                + "shown here has to be an attribute you keep on them.")
            .Action("Bar",
                [ScriptType.Text.Named("key"), ScriptType.Text.Named("outOf"), ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"), ScriptType.Integer.Named("blue")],
                (b, a) => Build(b).Bar(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A bar over every body's head, in a color given as red, green, and blue, each 0 to 255.")
            .Function("Action", verb.AsType, [ScriptType.Text.Named("id"), ScriptType.Text.Named("caption"), ScriptType.Text.Named("heading")],
                (b, a) => Build(b).Action(a.AsText(0), a.AsText(1), a.AsText(2)),
                "A verb this game offers, under a heading of its own. Picking it calls OnAction. Offered "
                + "in a square's menu until the verb says otherwise, and handed back so where "
                + "it is offered, what key reaches it, and what it needs are each their own line.")
            .Action("AskAtCreation",
                [ScriptType.Text.Named("key"), ScriptType.Text.Named("caption"),
                 ScriptType.Text.Named("records")],
                (b, a) => Build(b).AskAtCreation(a.AsText(0), a.AsText(1), a.AsText(2)),
                "Something to ask before a character exists - a class, a bloodline, a starting town. "
                + "The one question a game cannot ask any other way: everything else it wants to know it "
                + "asks of a body already in the world, and what a character is has to be settled "
                + "before there is one. The player picks from the records you name, listed by their own "
                + "names, and the number they picked is written onto them under your key before "
                + "OnPlayerJoined runs, so you read it the ordinary way and need no handler. "
                + "A blank record is not offered, because a list of unnamed slots is a screen "
                + "nobody can use.")
            .Function("Panel", panel.AsType,
                [ScriptType.Text.Named("id"), ScriptType.Text.Named("title"), ScriptType.Integer.Named("width"), ScriptType.Integer.Named("height")],
                (b, a) => Build(b).Panel(a.AsText(0), a.AsText(1), a.AsInteger(2), a.AsInteger(3)),
                "A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, "
                + "so its rows and its buttons are written underneath it.")
            .Action("Channel",
                [ScriptType.Text.Named("id"), ScriptType.Text.Named("caption"),
                 ScriptType.Integer.Named("red"), ScriptType.Integer.Named("green"),
                 ScriptType.Integer.Named("blue"), ScriptType.Text.Named("tab")],
                (b, a) =>
                {
                    Build(b).Channel(a.AsText(0), a.AsText(1),
                        (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4), a.AsText(5));
                    return null;
                },
                "A kind of line this game produces that a player can read apart from everything else, "
                + "and hide when they do not want it, in a color given as red, green, and blue, each 0 "
                + "to 255. Name it when you send: Message(who, text, id). "
                + "Give a tab caption and a fresh account gets a tab of that name carrying this channel "
                + "instead of the main one, which is what a noisy feed wants; channels naming the same "
                + "tab share it. Leave the tab blank and it reads in the main tab. "
                + "Declare none and everything this game says lands in system, which is the honest "
                + "answer for a world whose events are all one kind. "
                + "The id may not be one of Core's own - Global, System, Tell, Guild, Admin, or Always.");
    });

    /// <summary>The handle of whoever is answering to that name, or null.
    ///
    /// <para>Every slot the protocol allows, asked of the world rather than of a roster this module
    /// keeps: a list beside the world is a list that drifts from it, and the answer here has to be the
    /// body that is standing there now.</para></summary>
    private object? Somebody(string name)
    {
        if (name.Length == 0) return null;

        for (int i = 1; i <= Constants.MaxPlayers; i++)
        {
            var who = EntityHandle.ForPlayer(i);

            if (World.IsInWorld(who) && string.Equals(World.NameOf(who), name, StringComparison.Ordinal))
            {
                return who;
            }
        }

        return null;
    }

    private IWorld World =>
        _world ?? throw new InvalidOperationException(
            "The world does not exist yet. Configure runs before it, so it can only declare things; "
            + "anything that acts on a player belongs in a handler.");

    private AttributeValue? Attribute(object? who, string key) =>
        World.AttributesOf(Who(who)) is { } bag && bag.TryGet(key, out AttributeValue value) ? value : null;

    /// <summary>
    /// Whoever is standing on the square three arguments name, when they are the kind asked for.
    ///
    /// <para>Null for an empty square, for a body of the other kind, and for a square that is not a
    /// real tile — all three are the same answer to a script, which is "there is nobody there".</para>
    /// </summary>
    private object? Standing(IReadOnlyList<object?> arguments, EntitySort wanted)
    {
        EntityHandle found = World.At(Where(arguments));
        return found.Sort == wanted ? found : null;
    }

    /// <summary>The square three consecutive arguments name, starting at <paramref name="first"/>. Every
    /// call taking a place spells it as map, x and y, so this is what reads one back.</summary>
    private static WorldPlace Where(IReadOnlyList<object?> arguments, int first = 0) =>
        new((int)arguments.AsInteger(first),
            (int)arguments.AsInteger(first + 1),
            (int)arguments.AsInteger(first + 2));

    /// <summary>
    /// What a thrown thing looks like, named as TEXT because Compass has no type values.
    ///
    /// <para>An unknown name falls back to a bolt rather than refusing. A projectile is decoration:
    /// a game that misspells one should throw something visible and read its own typo on screen,
    /// rather than have the hit it belongs to silently not happen.</para>
    /// </summary>
    private static ProjectileStyle Looks(string name) => name.ToLowerInvariant() switch
    {
        "glitter" => ProjectileStyle.Glitter,
        "parcel" => ProjectileStyle.Parcel,
        _ => ProjectileStyle.Bolt,
    };

    /// <summary>
    /// A set a script passed, as handles.
    ///
    /// <para>Anything in it that is not one of the engine's own bodies is dropped rather than
    /// refused. A script cannot construct a Player, so the only way a stray value gets in is a set
    /// built from two sources, and dropping it is what the receiving end does with an absent body
    /// anyway.</para>
    /// </summary>
    private static List<EntityHandle> Bodies(IReadOnlyList<object?> arguments, int at)
    {
        var found = new List<EntityHandle>();

        if (arguments[at] is System.Collections.IEnumerable given and not string)
        {
            foreach (object? one in given)
            {
                if (one is EntityHandle handle && handle.IsSet) found.Add(handle);
            }
        }

        return found;
    }

    /// <summary>Three channels a script wrote as separate numbers, packed the way the wire carries
    /// one. Each is clamped rather than refused: a game doing arithmetic on a color should get a color
    /// out of it, not a refusal.</summary>
    private static uint Packed(IReadOnlyList<object?> arguments, int at)
    {
        static uint Channel(long value) => (uint)Math.Clamp(value, 0, 255);

        return (Channel(arguments.AsInteger(at)) << 16)
             | (Channel(arguments.AsInteger(at + 1)) << 8)
             | Channel(arguments.AsInteger(at + 2));
    }

    /// <summary>The handle behind a script's <c>Player</c>. Nothing a script can write reaches this.</summary>
    private static EntityHandle Who(object? value) => value is EntityHandle handle ? handle : EntityHandle.None;

    /// <summary>The bodies in a set argument. A set is read by position, so this is the one place that
    /// walks one rather than every caller doing it.</summary>
    private static IEnumerable<EntityHandle> Everyone(IReadOnlyList<object?> arguments, int at)
    {
        if (arguments[at] is not Compass.Runtime.ICompassSet set) yield break;

        for (int i = 0; i < set.Count; i++)
        {
            var who = Who(set.GetElement(i));
            if (who.IsSet) yield return who;
        }
    }

    private static WorldPlace Square(object? value) =>
        value is WorldPlace place ? place : WorldPlace.Nowhere;

    private static Marking Marked(object? value) =>
        value as Marking ?? throw new InvalidOperationException("This is not a marker.");

    /// <summary>
    /// One mark on the ground, handed back so its ring, its label, its meter and its audience are each
    /// their own line.
    ///
    /// <para>Every call puts the whole mark down again under the same name, so a script may say as much
    /// or as little as it likes in any order and the drawn mark is whatever it has said so far. That is
    /// what lets a mark be placed on one line and its meter moved on another a tick later.</para>
    /// </summary>
    private sealed class Marking(IWorld world, string id, WorldPlace at)
    {
        private string _label = string.Empty;
        private int _rgb;
        private int _radius;
        private long _value;
        private long _ceiling;
        private readonly HashSet<EntityHandle> _seenBy = [];

        public string Id => id;

        public object? Label(string text)
        {
            _label = text ?? string.Empty;
            return Put();
        }

        public object? Color(long r, long g, long b)
        {
            static int Channel(long value) => (int)Math.Clamp(value, 0, 255);

            _rgb = (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
            return Put();
        }

        public object? Ring(long tiles)
        {
            _radius = (int)Math.Clamp(tiles, 0, 64);
            return Put();
        }

        public object? Meter(long value, long ceiling)
        {
            _value = value;
            _ceiling = ceiling;
            return Put();
        }

        /// <summary>Adds bodies to the audience. Adds rather than replaces, because a mark for several
        /// sides is otherwise unsayable: a script cannot build one set out of two.</summary>
        public object? SeenBy(IEnumerable<EntityHandle> them)
        {
            foreach (var who in them) _seenBy.Add(who);
            return Put();
        }

        /// <summary>Puts the mark down as it currently reads. Called by every setter, so the mark on the
        /// ground is never a half-written one.</summary>
        public object? Put()
        {
            world.Mark(new WorldMarker
            {
                Id = id,
                At = at,
                Label = _label,
                Rgb = _rgb,
                Radius = _radius,
                Value = _value,
                Ceiling = _ceiling,
                SeenBy = [.. _seenBy],
            });

            return null;
        }
    }

    private static Declaring Build(object? value) =>
        value as Declaring ?? throw new InvalidOperationException("This is not a builder.");

    private static Describing Shape(object? value) =>
        value as Describing ?? throw new InvalidOperationException("These are not records.");

    private static Verb Verbal(object? value) =>
        value as Verb ?? throw new InvalidOperationException("This is not a verb.");

    private static Panel Screen(object? value) =>
        value as Panel ?? throw new InvalidOperationException("This is not a panel.");

    private static AttributeBag Sent(object? value) =>
        value as AttributeBag ?? throw new InvalidOperationException("These are not values.");

    private static Spoil Dropping(object? value) =>
        value as Spoil ?? throw new InvalidOperationException("These are not spoils.");

    /// <summary>
    /// One verb, handed back so what it is offered on, what reaches it, and what it needs are each a
    /// line of their own.
    ///
    /// <para>A surface is chosen by CALLING one rather than by naming one, so a word that is not a
    /// surface cannot be written. The default is the square, which is where most verbs belong.</para>
    /// </summary>
    private sealed class Verb(Declaring declaring, Declaring.PendingVerb verb)
    {
        public object? Icon(string icon)
        {
            if (declaring.Glyph($"the verb '{verb.Id}'", icon)) verb.Icon = icon;
            return null;
        }

        public object? Interacts()
        {
            verb.Interacts = true;
            return null;
        }

        public object? OnTile() => Offered(ActionSurface.Tile);

        public object? OnPlayer() => Offered(ActionSurface.Player);

        public object? OnNpc() => Offered(ActionSurface.Npc);

        public object? OnHud() => Offered(ActionSurface.Hud);

        public object? Nowhere() => Offered(ActionSurface.None);

        public object? Aimed()
        {
            verb.Aimed = true;
            return null;
        }

        public object? Key(string key)
        {
            verb.Key = key;
            return null;
        }

        public object? Opens(string panelId)
        {
            verb.Opens = panelId;
            return null;
        }

        /// <summary>What a body must be carrying for this to be offered rather than grayed.
        ///
        /// <para>Both halves are this one condition: the client grays the entry and the server refuses
        /// the call, and neither is told separately. A verb a player can see but cannot use yet reads as
        /// a game with more in it than a verb that is simply missing.</para></summary>
        public object? Hidden()
        {
            verb.Unmet = ActionUnmet.Hide;
            return null;
        }

        public object? Grayed()
        {
            verb.Unmet = ActionUnmet.Gray;
            return null;
        }

        public object? NeedsAtLeast(string key, long howMany)
        {
            verb.When = ActionCondition.AtLeast(key, howMany);
            return null;
        }

        public object? NeedsCarrying(string key)
        {
            verb.When = ActionCondition.Carrying(key);
            return null;
        }

        public object? NeedsNothing(string key)
        {
            verb.When = ActionCondition.NotCarrying(key);
            return null;
        }

        private object? Offered(ActionSurface surface)
        {
            verb.Surface = surface;
            return null;
        }
    }

    /// <summary>
    /// One screen of a game's own, handed back so its rows and its buttons are written underneath it.
    ///
    /// <para>Its rows go on a surface derived from its id, so a row cannot land on a surface nothing
    /// draws — which would be a declaration that takes, renders nowhere, and says nothing.</para>
    /// </summary>
    private sealed class Panel(Declaring declaring, Declaring.PendingPanel panel)
    {
        public object? Key(string key)
        {
            panel.Key = key;
            return null;
        }

        public object? Icon(string icon)
        {
            if (declaring.Glyph($"the panel '{panel.Id}'", icon)) panel.Icon = icon;
            return null;
        }

        public object? Button(string label, string actionId)
        {
            panel.Buttons.Add(new PanelButton(label, actionId));
            return null;
        }

        public object? Smallest(long wide, long tall)
        {
            panel.MinWidth = (int)Math.Clamp(wide, 0, int.MaxValue);
            panel.MinHeight = (int)Math.Clamp(tall, 0, int.MaxValue);
            return null;
        }

        public object? HeldWhile(string key, long least)
        {
            panel.Held = true;
            panel.While = ActionCondition.AtLeast(key, least);
            return null;
        }

        public object? OnlyWhile(string key, long least)
        {
            panel.While = ActionCondition.AtLeast(key, least);
            return null;
        }

        public object? Row(string labelKey, string idKey)
        {
            panel.Rows.Add(new PanelRow(labelKey, idKey));
            return null;
        }

        /// <summary>
        /// This panel asks the player to fill one of this world's own models in, and send it.
        ///
        /// <para><b>Both halves at once, because either one alone is silent.</b> Inputs with no
        /// message collect values nothing sends; a message with no inputs is one a stock client still
        /// cannot compose. One line declares the message, the controls, and the button.</para>
        ///
        /// <para>One message to a panel. A second is refused by name rather than quietly adding a
        /// row group whose button nobody could tell from the first one's.</para>
        /// </summary>
        public object? Asks(string modelName, string label)
        {
            if (panel.Asks.Length > 0)
            {
                declaring.Refuse($"the message '{modelName}'",
                    $"the panel '{panel.Id}' already asks for '{panel.Asks}' - "
                    + "a panel asks for one message, because one button sends what one panel holds");
                return null;
            }

            return declaring.Asks(panel, modelName, label);
        }

        public object? Heading(string label) =>
            Row(DisplayStyle.Heading, string.Empty, string.Empty, label, 0, 0, 0);

        public object? Field(string valueKey, string label, int red, int green, int blue) =>
            Row(DisplayStyle.Text, valueKey, string.Empty, label, red, green, blue);

        public object? Badge(string valueKey, string label, int red, int green, int blue) =>
            Row(DisplayStyle.Badge, valueKey, string.Empty, label, red, green, blue);

        public object? Meter(string valueKey, string maxKey, string label, int red, int green, int blue) =>
            Row(DisplayStyle.Meter, valueKey, maxKey, label, red, green, blue);

        private object? Row(
            DisplayStyle style, string valueKey, string maxKey, string label,
            int red, int green, int blue) =>
            declaring.Row(Declaring.SurfaceOf(panel.Id), style, valueKey, maxKey, label,
                          red, green, blue);
    }

    /// <summary>
    /// One kind of record, handed back by <c>game.Records</c> so the rest can be said about it.
    ///
    /// <para>It reaches exactly the records it came from, so a field is named on its own and a reader
    /// sees which records a line is about from the value it is called on. A script holds it in a local
    /// and the lines about one kind of record group themselves.</para>
    ///
    /// <para>A field is still named as TEXT, because nothing in Compass carries a field's identity as
    /// a value. A name that is not there is refused at load, naming the model and the field.</para>
    ///
    /// <para>Records that were REFUSED still hand one of these back rather than nothing, because a
    /// script calling a method on nothing would fail where a refusal has already been recorded. It does
    /// nothing, quietly: the one line saying what went wrong is the useful one.</para>
    /// </summary>
    private sealed class Describing(Declaring declaring, string model, bool declared)
    {
        public object? Are(string label, string singular, long limit)
        {
            if (declared) declaring.Are(model, label, singular, limit);
            return null;
        }

        public object? Icon(string icon)
        {
            if (declared) declaring.Icon(model, icon);
            return null;
        }

        public object? Stored(string folder, string prefix)
        {
            if (declared) declaring.Stored(model, folder, prefix);
            return null;
        }

        public object? Extend(string family)
        {
            if (declared) declaring.Extend(model, family);
            return null;
        }

        public object? Caption(string field, string label)
        {
            if (declared) declaring.Caption($"{model}.{field}", label);
            return null;
        }

        public object? Range(string field, long min, long max)
        {
            if (declared) declaring.Range($"{model}.{field}", min, max);
            return null;
        }

        public object? Length(string field, long maxLength)
        {
            if (declared) declaring.Length($"{model}.{field}", maxLength);
            return null;
        }

        public object? Points(string field, string family)
        {
            if (declared) declaring.Points($"{model}.{field}", family);
            return null;
        }
    }

    public void Dispose() => _loaded?.Dispose();

    /// <summary>
    /// What a script's <c>Configure</c> writes into.
    ///
    /// <para>Not valid after that function returns, the same way <see cref="ICoreBuilder"/> is not — a
    /// script keeping the reference and declaring from a handler is told so rather than declaring into
    /// an engine that has already been built around it.</para>
    /// </summary>
    private sealed class Declaring(
        ICoreBuilder builder,
        List<string> actions,
        List<string> messages,
        IReadOnlyList<ScriptModelInfo> models,
        System.Action<int> everyTicks)
    {
        /// <summary>
        /// Where a world's own rows and verbs start counting.
        ///
        /// <para>An ordinal is global to its surface, so two modules both numbering from zero interleave.
        /// A heading is a row like any other, so one module's heading lands in the middle of another's
        /// rows and the sidebar reads as nonsense. A world's own declarations therefore sit after
        /// whatever the compiled game declared — which is also the order somebody reading the sidebar
        /// expects: the game, and then this world.</para>
        /// </summary>
        private const int After = 1000;

        private readonly List<string> _refused = [];
        private int _fields, _bars, _slots, _order;
        private bool _closed;

        // What has been described so far, and the choice sets the enumerations produced. Held
        // rather than handed over line by line, because a refinement may still arrive for any of them
        // and the engine freezes what it is given. Lists, because declaration order is the order the
        // editor draws them in.
        private readonly List<PendingRecords> _records = [];
        private readonly List<ChoiceSet> _choices = [];

        // Refinements are queued rather than applied, so one may be written before the records it
        // refines. A line that had to follow its Records call would reintroduce the ordering this
        // shape exists to remove.
        private readonly List<Refinement> _refinements = [];

        // The rest of what a script declares, held for the same reason: each is handed back so more can
        // be said about it, and the engine freezes what it is given.
        private readonly List<PendingRow> _rows = [];
        private readonly List<PendingVerb> _verbs = [];
        private readonly List<PendingPanel> _panels = [];
        private readonly List<ChatChannelSpec> _channels = [];

        /// <summary>The chat channels this world declared, for the module to check a script’s feed name
        /// against once loading is done.</summary>
        internal IReadOnlyList<ChatChannelSpec> Channels => _channels;

        public IReadOnlyList<string> Refused => _refused;

        /// <summary>Declaring is over: hand over everything described, refinements included.
        ///
        /// <para>Refinements first, so one may be written before the records it refines. Then the
        /// choice sets, because records naming a set the engine has not been told about yet would draw
        /// a drop-down with nothing in it — which reads as a broken control rather than as an order
        /// nobody could have known about.</para></summary>
        public void Close()
        {
            foreach (Refinement refinement in _refinements) Apply(refinement);

            // A field may point at records that do not exist, and the picker would then list nothing
            // at all - which reads as a game with no records in it rather than as a model somebody
            // forgot to declare. So the field is dropped and named.
            //
            // The ENGINE'S own families count: Items, NPCs, Maps, Shops and Conversations are records a
            // world holds like any other, and a kit line naming a sword by name rather than by number is
            // the ordinary thing to want.
            foreach (PendingRecords records in _records)
            {
                records.Fields.RemoveAll(f =>
                {
                    if (f.Kind is not FieldKind.RecordRef || f.RecordFamilyId is not { } target
                        || Described(target) is not null || CoreRecordFamilies.Find(target) is not null)
                    {
                        return false;
                    }

                    Refuse($"the field '{records.Id}.{f.Key}'",
                           $"it points at '{f.RecordFamilyId}', which is neither one of this game's "
                           + "own kinds of record nor one of the engine's");
                    return true;
                });
            }

            foreach (ChoiceSet set in _choices)
            {
                Guard($"the choices '{set.Id}'", () => builder.AddChoiceSet(set));
            }

            // Extensions AFTER the new families, so a module may extend one another module declared
            // without the two having to be loaded in a particular order.
            foreach (PendingRecords records in _records.Where(r => r.Extends.Length == 0))
            {
                Guard($"the records '{records.Id}'", () => builder.AddFamily(new RecordFamily
                {
                    Id = records.Id,
                    Directory = records.Folder,
                    FilePrefix = records.Prefix,
                    LabelKey = records.Label,
                    Icon = records.Icon,
                    SingularLabelKey = records.Singular.Length > 0 ? records.Singular : records.Label,
                    DefaultLimit = records.Limit > 0
                        ? (int)Math.Min(records.Limit, int.MaxValue)
                        : 1000,
                    Fields = records.Fields,
                }));
            }

            foreach (PendingRecords records in _records.Where(r => r.Extends.Length > 0))
            {
                Guard($"the fields '{records.Id}' adds to '{records.Extends}'",
                      () => builder.ExtendFamily(records.Extends, records.Fields));
            }

            foreach (ChatChannelSpec channel in _channels)
                Guard($"the chat channel '{channel.Id}'", () => builder.AddChatChannel(channel));

            // The panels before the verbs that open them, so a verb naming one can be checked against
            // what was actually declared rather than against what is about to be.
            foreach (PendingPanel panel in _panels)
            {
                Guard($"the panel '{panel.Id}'", () => builder.AddPanel(new GamePanel
                {
                    Id = panel.Id,
                    TitleKey = panel.Title,
                    Surface = SurfaceOf(panel.Id),
                    Width = panel.Width,
                    Height = panel.Height,
                    Key = panel.Key,
                    Icon = panel.Icon,
                    Buttons = [.. panel.Buttons],
                    Rows = [.. panel.Rows],
                    MinWidth = panel.MinWidth,
                    MinHeight = panel.MinHeight,
                    Held = panel.Held,
                    While = panel.While,
                    Asks = panel.Asks,
                    SendLabelKey = panel.SendLabel,
                    Inputs = [.. panel.Inputs],
                }));
            }

            foreach (PendingVerb verb in _verbs) Hand(verb);

            foreach (PendingRow row in _rows)
            {
                Guard($"the {row.Style.ToString().ToLowerInvariant()} '{row.Label}'",
                      () => builder.AddDisplayField(new DisplayField
                      {
                          Surface = row.Surface,
                          Style = row.Style,
                          ValueKey = row.ValueKey,
                          MaxKey = row.MaxKey,
                          LabelKey = row.Label,
                          Ordinal = row.Ordinal,
                          Rgb = row.Rgb,
                      }));
            }

            _closed = true;
        }

        /// <summary>One verb, once everything that could be said about it has been.
        ///
        /// <para>A verb opening a panel nobody declared is the silent half here: the button draws,
        /// the player presses it, and nothing happens. It is refused by name instead — the verb still
        /// lands, because a menu entry that does nothing is better than a menu entry that is missing
        /// along with everything declared after it.</para></summary>
        private void Hand(PendingVerb verb)
        {
            string opens = verb.Opens;

            if (opens.Length > 0 && !_panels.Any(p => string.Equals(p.Id, opens, StringComparison.Ordinal)))
            {
                Refuse($"the verb '{verb.Id}'", $"it opens '{opens}', which no game.Panel call declared");
                opens = string.Empty;
            }

            Guard($"the verb '{verb.Id}'", () => builder.AddAction(new GameAction
            {
                Id = verb.Id,
                LabelKey = verb.Label,
                GroupKey = verb.Group,
                Surface = verb.Surface,
                Ordinal = verb.Ordinal,
                Key = verb.Key,
                Icon = verb.Icon,
                Interacts = verb.Interacts,
                Aimed = verb.Aimed,
                OpensPanel = opens,
                When = verb.When,
                Unmet = verb.Unmet,
            }));
        }

        // ── A game's own records ──────────────────────────────────────────
        //
        // Nothing a game authors is declared here. A model describes ITSELF — see Describing — and
        // these are what that leaves behind: the records being built, and the changes still to make to
        // them. Both are held until Close(), so no line of a script depends on the one above it.

        /// <summary>Declare a kind of record from a model this script already wrote, and hand the
        /// records back so the rest can be said about them BY NAME.
        ///
        /// <para><b>The model IS the declaration.</b> It says which fields there are and what each
        /// one holds, so nothing repeats that: a field typed as an ENUMERATION becomes a drop-down over
        /// that enumeration's members, and one typed as ANOTHER MODEL becomes a picker listing that
        /// model's records by name. A field the engine has no equivalent for — a set, an optional, a
        /// function — is left out and named, rather than quietly missing from the form.</para>
        ///
        /// <para>The model's own name is the id, unchanged. A game's records sit beside the engine's
        /// <c>Items</c> and <c>Npcs</c> in one list and in one folder each, so a model named for
        /// something already there has to read as the collision it is.</para>
        ///
        /// </summary>
        public Describing? Begin(ScriptModelInfo shape)
        {
            if (Described(shape.Name) is not null)
            {
                Refuse($"the records '{shape.Name}'", "they are already declared");
                return null;
            }

            var fields = new List<FieldDescriptor>();
            foreach (ScriptModelField field in shape.Fields)
            {
                if (Describe(shape.Name, field) is { } descriptor) fields.Add(descriptor);
            }

            if (fields.Count == 0)
            {
                Refuse($"the records '{shape.Name}'", "none of its fields can be authored");
                return null;
            }

            _records.Add(new PendingRecords(shape.Name) { Fields = fields });
            return new Describing(this, shape.Name, declared: true);
        }

        /// <summary>What these records are called, and how many there may be. A caption left blank
        /// keeps the model's own name, and a limit of zero keeps the default.</summary>
        public void Are(string model, string label, string singular, long limit)
        {
            if (Described(model) is not { } records) return;

            if (label.Length > 0) records.Label = label;
            if (singular.Length > 0) records.Singular = singular;
            if (limit > 0) records.Limit = limit;
        }

        /// <summary>One model field as a row on a form, or null for one nothing could edit.</summary>
        private FieldDescriptor? Describe(string modelName, ScriptModelField field)
        {
            FieldKind? kind = field.Shape switch
            {
                ScriptFieldShape.Text => FieldKind.Text,
                ScriptFieldShape.Whole => FieldKind.Integer,
                ScriptFieldShape.Fraction => FieldKind.Real,
                ScriptFieldShape.Truth => FieldKind.Flag,
                ScriptFieldShape.Choice => FieldKind.Choice,
                ScriptFieldShape.Reference => FieldKind.RecordRef,
                _ => null,
            };

            if (kind is null)
            {
                Refuse($"the field '{modelName}.{field.Name}'",
                       $"a {field.TypeName} is not something a form can edit");
                return null;
            }

            if (field.Shape is ScriptFieldShape.Reference)
            {
                return new FieldDescriptor
                {
                    Key = field.Name, LabelKey = WordsFor(field.Name),
                    Kind = FieldKind.RecordRef, RecordFamilyId = field.TypeName,
                };
            }

            if (field.Shape is not ScriptFieldShape.Choice)
            {
                return new FieldDescriptor
                {
                    Key = field.Name, LabelKey = WordsFor(field.Name), Kind = kind.Value,
                };
            }

            // The enumeration's members ARE the set, so it is declared here rather than by the script:
            // writing the enumeration was already the declaration.
            string setId = $"{modelName}.{field.Name}";
            _choices.Add(new ChoiceSet
            {
                Id = setId,
                Members = [.. field.Choices.Select(
                    c => new KindDescriptor { Id = c, LabelKey = WordsFor(c) })],
            });

            return new FieldDescriptor
            {
                Key = field.Name, LabelKey = WordsFor(field.Name),
                Kind = FieldKind.Choice, ChoiceSetId = setId,
            };
        }

        /// <summary>A name as something to read: "siteName" becomes "Site name".
        ///
        /// <para>A default, not a decision. A game wanting other words says so with <c>Caption</c>, and
        /// only for the fields where this is wrong.</para></summary>
        private static string WordsFor(string name)
        {
            if (name.Length == 0) return name;

            var words = new System.Text.StringBuilder();
            words.Append(char.ToUpperInvariant(name[0]));

            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    words.Append(' ').Append(char.ToLowerInvariant(name[i]));
                }
                else
                {
                    words.Append(name[i]);
                }
            }

            return words.ToString();
        }

        // ── Saying more about one field ───────────────────────────────────────

        /// <summary>Where these records live on disk: the folder under the world, and what each file is
        /// called before its number.
        ///
        /// <para>Only for records that ALREADY EXIST. Left unsaid, the folder is the model's name
        /// lowercased and the file name is that without a trailing "s" — which is right for
        /// <c>sites/site1.json</c> and wrong for <c>species/species1.json</c>, because English is not a
        /// rule. A new game should say nothing here and let the default name the files.</para></summary>
        /// <summary>The glyph these records wear in the editor's rail.</summary>
        public void Icon(string model, string icon)
        {
            if (Described(model) is not { } records) return;
            if (Glyph($"the records '{model}'", icon)) records.Icon = icon;
        }

        public void Stored(string model, string folder, string prefix)
        {
            if (Described(model) is not { } records)
            {
                Refuse($"where '{model}' is stored", $"no records called '{model}' were declared");
                return;
            }

            records.Folder = folder;
            records.Prefix = prefix;
        }

        /// <summary>
        /// These fields are ADDED to a family that already exists, rather than being a family of their
        /// own.
        ///
        /// <para><b>How a game extends a record the ENGINE owns.</b> An item's own properties are the
        /// ones Core acts on and they are a closed set; a game's are open, and they belong on the same
        /// record rather than in a table beside it. A class gate is a fact about the sword.</para>
        ///
        /// <para>Everything else a model says about itself still applies — a caption, a range, a picker
        /// — because these are rows on a form either way. What does not apply is where the records live,
        /// how many there may be, and what they are called: the family being extended already answers
        /// all three.</para>
        /// </summary>
        public void Extend(string model, string family)
        {
            if (Described(model) is not { } records)
            {
                Refuse($"extending '{family}'", $"no records called '{model}' were declared");
                return;
            }

            records.Extends = family;
        }

        /// <summary>What a field is called on the form, when its own name is not the words wanted.</summary>
        public void Caption(string field, string label) =>
            Refine(field, "the caption", d => d with { LabelKey = label });

        /// <summary>The bounds of a whole-number field. Equal bounds mean unbounded.</summary>
        public void Range(string field, long min, long max) =>
            Refine(field, "the range", d => d with
            {
                Min = (int)Math.Clamp(min, int.MinValue, int.MaxValue),
                Max = (int)Math.Clamp(max, int.MinValue, int.MaxValue),
            });

        /// <summary>
        /// Retype a whole-number field as a slot in another kind of record, so the editor draws a picker
        /// listing them by name.
        ///
        /// <para><b>The escape hatch for the engine's own records.</b> A field typed as a MODEL
        /// already points at that model's records — but <c>Items</c>, <c>NPCs</c>, <c>Maps</c>,
        /// <c>Shops</c> and <c>Conversations</c> have no model a script could name, and authoring a kit
        /// line by typing 214 when the answer is "Iron Key" is the thing a picker exists to
        /// stop.</para>
        ///
        /// <para>The number is still what is stored. This changes what the form draws and nothing
        /// about what a rule reads back, so the field is written as a plain whole number.</para>
        /// </summary>
        public void Points(string field, string family) =>
            Refine(field, $"pointing at '{family}'", d => d with
            {
                Kind = FieldKind.RecordRef,
                RecordFamilyId = family,
            });

        /// <summary>How long a text field may be. Zero means no limit.</summary>
        public void Length(string field, long maxLength) =>
            Refine(field, "the length", d => d with
            {
                MaxLength = maxLength > 0 ? (int)Math.Min(maxLength, int.MaxValue) : 0,
            });

        /// <summary>Remember a change to one field, addressed as "Model.field".</summary>
        private void Refine(string field, string what, Func<FieldDescriptor, FieldDescriptor> change)
            => _refinements.Add(new Refinement(field, what, change));

        /// <summary>Make one remembered change, or say why it went nowhere.</summary>
        private void Apply(Refinement refinement)
        {
            var (field, what, change) = refinement;

            int dot = field.LastIndexOf('.');
            string id = field[..dot], key = field[(dot + 1)..];

            if (Described(id) is not { } records)
            {
                Refuse($"{what} for '{field}'", $"no records called '{id}' were declared");
                return;
            }

            int at = records.Fields.FindIndex(f => string.Equals(f.Key, key, StringComparison.Ordinal));
            if (at < 0)
            {
                Refuse($"{what} for '{field}'", $"'{id}' has no field called '{key}'");
                return;
            }

            records.Fields[at] = change(records.Fields[at]);
        }

        /// <summary>One field of one kind of records, and what to do to it.</summary>
        private sealed record Refinement(
            string Field, string What, Func<FieldDescriptor, FieldDescriptor> Change);

        /// <summary>The records declared under that name, or null.</summary>
        private PendingRecords? Described(string id)
            => _records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

        /// <summary>Records described but not yet handed over, so a change can still land on them.</summary>
        private sealed class PendingRecords(string id)
        {
            public string Id { get; } = id;

            public string Folder { get; set; } = string.Empty;

            public string Prefix { get; set; } = string.Empty;

            public string Label { get; set; } = WordsFor(id);

            public string Singular { get; set; } = WordsFor(id);

            public long Limit { get; set; } = 1000;

            public string Icon { get; set; } = string.Empty;

            /// <summary>The family these fields are ADDED to, rather than a family of their own. Empty
            /// for the ordinary case, which is a model describing a kind of record nobody had.</summary>
            public string Extends { get; set; } = string.Empty;

            public List<FieldDescriptor> Fields { get; init; } = [];
        }

        /// <summary>Something to ask before a character exists.
        ///
        /// <para>The key is declared as an ATTRIBUTE too, seen by its owner. The answer is written onto
        /// the character and a game reads it back as an ordinary key, so a key nothing declared would be
        /// one the engine never syncs — written, saved, and invisible to the body carrying it.</para>
        /// </summary>
        public object? AskAtCreation(string key, string caption, string records)
            => Guard($"the creation choice '{key}'", () =>
            {
                builder.Attributes.Declare(key, AttributeVisibility.Owner);
                builder.AddCreationChoice(new CreationChoice
                {
                    Key = key,
                    LabelKey = caption,
                    FamilyId = records,
                });
            });

        public object? Attribute(string key, string visibility) => Guard($"the attribute '{key}'", () =>
            builder.Attributes.Declare(key, Visibility(visibility)));

        /// <summary>A message a client may send this game, taking its fields from one of this world's
        /// own models — the same way records do, and for the same reason: the model already says what
        /// the fields are called and what they hold.
        ///
        /// <para><b>A typed packet is not what a message needs.</b> The registry takes a parse
        /// delegate, so the line is read into the shapes the model declared and handed over as values.
        /// Nothing is compiled, and a field the model did not name is dropped rather than carried.</para>
        ///
        /// <para>A stock client cannot COMPOSE one — it only originates verbs, which carry an action
        /// id and a square and no values of their own. This is for a client, a tool, or a bot that
        /// knows the message, which is the same audience a compiled module's packet has.</para></summary>
        public object? Message(string modelName)
        {
            if (Carried(modelName) is not { } carried) return null;

            // A panel that asks for it has already put it on the wire, and a second row for one
            // command is refused by the registry rather than shadowing the first.
            if (Declared(modelName)) return null;

            Register(modelName, carried);
            return null;
        }

        /// <summary>
        /// A panel's inputs, the message they compose, and the button that sends it.
        ///
        /// <para>The message is declared here if nothing has declared it yet, so a panel that asks
        /// for one is all a game writes. A game that also wants a bot or a tool to send the same
        /// message writes <c>game.Message</c> as well, in either order.</para>
        /// </summary>
        public object? Asks(PendingPanel panel, string modelName, string label)
        {
            if (Carried(modelName) is not { } carried) return null;

            var inputs = new List<PanelInput>();
            foreach (ScriptModelField field in carried)
            {
                inputs.Add(new PanelInput
                {
                    Field = field.Name,
                    LabelKey = WordsFor(field.Name),
                    Kind = field.Shape switch
                    {
                        ScriptFieldShape.Whole => FieldKind.Integer,
                        ScriptFieldShape.Fraction => FieldKind.Real,
                        ScriptFieldShape.Truth => FieldKind.Flag,
                        ScriptFieldShape.Choice => FieldKind.Choice,
                        _ => FieldKind.Text,
                    },
                    Choices = [.. field.Choices],
                });
            }

            if (!Declared(modelName)) Register(modelName, carried);

            panel.Asks = modelName;
            panel.SendLabel = label.Length > 0 ? label : WordsFor(modelName);
            panel.Inputs.AddRange(inputs);

            return null;
        }

        /// <summary>
        /// Whether this is a glyph the engine offers, refusing it BY NAME when it is not.
        ///
        /// <para><b>Refused here, tolerated when drawn.</b> A typo is otherwise a section that looks
        /// like every other section, which reads as an engine that ignores the line rather than as a
        /// misspelled word. The renderers fall back instead, so a client older than the game it joined
        /// still draws something.</para>
        /// </summary>
        public bool Glyph(string what, string icon)
        {
            if (GameIcon.IsOffered(icon)) return true;

            Refuse(what, $"'{icon}' is not a glyph this engine draws - one of: {GameIcon.Listed}");
            return false;
        }

        /// <summary>Whether a message of this name is already on the wire.</summary>
        private bool Declared(string modelName) =>
            messages.Contains(modelName, StringComparer.Ordinal);

        /// <summary>Reads this command into the shapes the model declared.</summary>
        private void Register(string modelName, List<ScriptModelField> carried) =>
            Guard($"the message '{modelName}'", () =>
            {
                builder.Packets.Register(
                    modelName, (json, _) => ScriptedPacket.Read(modelName, json, carried));

                messages.Add(modelName);
            });

        /// <summary>
        /// The fields of a model that can travel, or null where the model or its fields cannot.
        ///
        /// <para>The model is named as TEXT, because Compass has no type values — so a typo cannot
        /// be a compile error and has to be a refusal that lists the models which do exist.</para>
        /// </summary>
        private List<ScriptModelField>? Carried(string modelName)
        {
            ScriptModelInfo? shape = models.FirstOrDefault(
                m => string.Equals(m.Name, modelName, StringComparison.Ordinal));

            if (shape is null)
            {
                string known = string.Join(", ", models.Select(m => m.Name));
                Refuse($"the message '{modelName}'",
                       known.Length > 0
                           ? $"no model of that name is declared - this world has: {known}"
                           : "this world declares no models at all");
                return null;
            }

            var carried = new List<ScriptModelField>();
            foreach (ScriptModelField field in shape.Fields)
            {
                if (field.Shape is ScriptFieldShape.Unsupported or ScriptFieldShape.Reference)
                {
                    Refuse($"the field '{modelName}.{field.Name}'",
                           $"a {field.TypeName} is not something a message can carry");
                    continue;
                }

                carried.Add(field);
            }

            if (carried.Count == 0)
            {
                Refuse($"the message '{modelName}'", "none of its fields can travel");
                return null;
            }

            return carried;
        }

        /// <summary>How often this world's tick comes round, in ticks. Below one is one.</summary>
        public object? TickEvery(long ticks)
        {
            everyTicks((int)Math.Clamp(ticks, 1, int.MaxValue));
            return null;
        }

        /// <summary>A place on a body something can be worn. Nothing is said about one afterwards, so
        /// it is handed over where it is written.</summary>
        public object? EquipSlot(string key, string label) => Guard($"the equip slot '{key}'", () =>
            builder.AddEquipSlot(new EquipSlot
            {
                Key = key,
                LabelKey = label.Length > 0 ? label : WordsFor(key),
                Ordinal = After + _slots++,
            }));

        /// <summary>A bar over every body's head, in a color given one channel at a time, which is how a
        /// person picks one.
        ///
        /// <para>Out-of-range channels are clamped rather than refused, for the same reason a bar past
        /// its own maximum draws full: losing a whole row over a mistyped digit is a worse answer than
        /// drawing it slightly wrong.</para></summary>
        public object? Bar(string valueKey, string maxKey, int red, int green, int blue) =>
            Guard($"the bar on '{valueKey}'", () => builder.AddOverheadBar(new OverheadBar
            {
                ValueKey = valueKey,
                MaxKey = maxKey,
                Rgb = Rgb(red, green, blue),
                Ordinal = After + _bars++,
            }));

        internal static int Rgb(int red, int green, int blue)
            => (Channel(red) << 16) | (Channel(green) << 8) | Channel(blue);

        private static int Channel(int value) => Math.Clamp(value, 0, 255);

        // ── Rows, on whichever surface ──────────────────────────────────────────

        /// <summary>One row. The surface decides where it is drawn — the sidebar, or a panel of this
        /// game's own — and a color of zero leaves it to the client.</summary>
        public object? Row(
            string surface, DisplayStyle style, string valueKey, string maxKey, string label,
            int red, int green, int blue)
        {
            _rows.Add(new PendingRow
            {
                Surface = surface,
                Style = style,
                ValueKey = valueKey,
                MaxKey = maxKey,
                Label = label,
                Ordinal = After + _fields++,
                Rgb = Rgb(red, green, blue),
            });

            return null;
        }

        // ── Verbs ─────────────────────────────────────────────────────────

        /// <summary>A verb, handed back so what it is offered on, what reaches it, and what it needs can
        /// each be said on a line of their own. Offered on a square until something says otherwise,
        /// which is where most verbs belong.</summary>
        public Verb Action(string id, string label, string group)
        {
            var verb = new PendingVerb
            {
                Id = id,
                Label = label,
                Group = group,
                Ordinal = After + _order++,
            };

            _verbs.Add(verb);
            actions.Add(id);
            return new Verb(this, verb);
        }

        // ── Panels ────────────────────────────────────────────────────────

        /// <summary>A screen of this game's own, handed back so its rows and its buttons can be written
        /// underneath it.
        ///
        /// <para>Its SURFACE is derived from its id rather than named, for the same reason a choice set's
        /// is: a surface written twice is two names to keep in step, and a row landing on a surface
        /// nothing draws is invisible.</para></summary>
        public Panel Panel(string id, string title, long width, long height)
        {
            var panel = new PendingPanel
            {
                Id = id,
                Title = title.Length > 0 ? title : WordsFor(id),
                Width = (int)Math.Clamp(width, 0, 4000),
                Height = (int)Math.Clamp(height, 0, 4000),
            };

            _panels.Add(panel);
            return new Panel(this, panel);
        }

        internal static string SurfaceOf(string panelId) => $"panel.{panelId}";

        // ── Chat channels ───────────────────────────────────

        /// <summary>A kind of line this game produces, that a player can read apart from everything else.
        /// Nothing is handed back: a channel is a name, a caption, a color and where it starts, and there
        /// is nothing to write underneath it.</summary>
        public void Channel(string id, string caption, int red, int green, int blue, string tab)
        {
            _channels.Add(new ChatChannelSpec
            {
                Id = id.Trim(),
                LabelKey = caption.Length > 0 ? caption : WordsFor(id),
                Rgb = GameColor.Pack(red, green, blue),
                OwnTabKey = tab.Trim(),
            });
        }

        /// <summary>
        /// A visibility, by the word a script writes. An enum cannot cross the boundary — a script may
        /// name a registered TYPE and nothing else — so the words are the vocabulary, and one that is
        /// not a word is refused with the list rather than falling back to a default nobody chose.
        /// </summary>
        private static AttributeVisibility Visibility(string word) =>
            Enum.TryParse(word, ignoreCase: true, out AttributeVisibility v)
                ? v
                : throw new ArgumentException(
                    $"'{word}' is not a visibility. Write one of: "
                    + string.Join(", ", Enum.GetNames<AttributeVisibility>().Select(n => n.ToLowerInvariant())) + ".");

        private object? Guard(string what, System.Action declare)
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "Configure has already returned, so there is nothing left to declare into. Everything "
                    + "a script adds to the engine is said there, because what is declared decides what "
                    + "gets built.");
            }

            // A collision with a compiled module, or a declaration the engine refuses, is refused on its
            // own. Letting it out would stop the server over a world's content.
            try { declare(); }
            catch (Exception ex) when (ex is CoreModuleException or ArgumentException)
            {
                _refused.Add($"{what} was refused - {ex.Message}");
            }

            return null;
        }

        /// <summary>Turn down a declaration the engine never sees, for a reason of this seam's own — a
        /// verb opening a panel nobody declared, a field nothing could edit. Logged exactly like a
        /// collision is, because from the author's side it is the same event: something they wrote did
        /// not take.</summary>
        public void Refuse(string what, string why) => _refused.Add($"{what} was refused - {why}");

        // ── What is being described, until Close ───────────────────────────────────

        internal sealed class PendingRow
        {
            public string Surface { get; init; } = DisplaySurfaces.Hud;
            public DisplayStyle Style { get; init; }
            public string ValueKey { get; init; } = string.Empty;
            public string MaxKey { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public int Ordinal { get; init; }
            public int Rgb { get; init; }
        }

        internal sealed class PendingVerb
        {
            public string Id { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public string Group { get; init; } = string.Empty;
            public int Ordinal { get; init; }
            public ActionSurface Surface { get; set; } = ActionSurface.Tile;
            public string Key { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public bool Interacts { get; set; }
            public bool Aimed { get; set; }
            public string Opens { get; set; } = string.Empty;
            public ActionCondition When { get; set; } = ActionCondition.Always;
            public ActionUnmet Unmet { get; set; } = ActionUnmet.Gray;
        }

        internal sealed class PendingPanel
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public int Width { get; init; }
            public int Height { get; init; }
            public string Key { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public List<PanelButton> Buttons { get; } = [];
            public List<PanelRow> Rows { get; } = [];
            public int MinWidth { get; set; }
            public int MinHeight { get; set; }
            public bool Held { get; set; }
            public ActionCondition While { get; set; } = ActionCondition.Always;
            public string Asks { get; set; } = string.Empty;
            public string SendLabel { get; set; } = string.Empty;
            public List<PanelInput> Inputs { get; } = [];
        }
    }
}

/// <summary>One function a world's rules may write, and when the engine calls it.</summary>
/// <param name="Name">What the function is called.</param>
/// <param name="Arity">How many arguments it takes, which is part of what the engine asks for.</param>
/// <param name="Signature">How it is written, without the <c>public</c> in front. A handler that
/// yields something carries its type there, because that is where Compass writes one.</param>
/// <param name="When">What has just happened when it is called.</param>
/// <param name="Was">What it used to take, for a handler that has since gained an argument. Compass
/// matches a function by name AND count, so without this a world written against the older signature
/// stops being called with no error anywhere — its verbs simply do nothing.
///
/// <para>🔴 A handler may only ever GAIN arguments on the END. The older call is the newer one
/// cut short, so anything else here would hand a world its arguments in the wrong order.</para></param>
public sealed record ScriptHandler(string Name, int Arity, string Signature, string When, int Was = 0)
{
    /// <summary>The counts to try, newest first.</summary>
    public IEnumerable<int> Arities => Was > 0 ? [Arity, Was] : [Arity];
}
