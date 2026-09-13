using Mirage.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Serilog;

namespace Mirage.Server.Host.Scripting;

/// <summary>
/// The module that makes a world's own scripts part of the game.
///
/// <para>🔴 <b>This is the route the engine exists for.</b> Every other seam is reached by writing C#,
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
    : ICoreModule, IWorldObserver, ITickWork, IActionHandler, IDeathPolicy, ILingerPolicy, IDisposable
{
    /// <summary>The folder inside a world that holds its rules.</summary>
    public const string ScriptsFolder = "scripts";

    /// <summary>The shared model the engine looks for its handlers on.</summary>
    public const string Rules = "Rules";


    /// <summary>
    /// Every handler a module may write, and when each is called.
    ///
    /// <para>🔴 <b>One table, read by everything.</b> What the engine asks the module for, what the
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
        new("OnAction", 6,
            "function OnAction(Player who, string action, string on, integer map, integer x, integer y)",
            "the player picked one of this module's own verbs; 'on' names the body it was used on, "
            + "or is blank for a verb offered on a square or on the HUD"),
        new("OnTick", 0, "function OnTick()",
            "the module's tick came round, however often game.TickEvery asked for"),
        new("OnPlayerTick", 1, "function OnPlayerTick(Player who)",
            "the same tick, once for each player in the world — which is the list a script has no "
            + "other way to walk"),
        new("OnMayDie", 2, "string function OnMayDie(Player who, string cause)",
            "somebody is about to die; yield a reason to stop it, or blank to let it happen"),
        new("OnLinger", 1, "integer function OnLinger(Player who)",
            "their connection dropped; yield how many seconds the body stays in the world"),
    ];

    private readonly string _folder;
    private readonly List<string> _actions = [];
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private IWorld? _world;
    private LoadedScript? _loaded;
    private bool _onJoined, _onLeft, _onMoved, _onTick, _onPlayerTick, _onMayDie, _onLinger;
    private bool _onAction;

    // How often the tick comes round, which a script sets while it declares. One, until it says
    // otherwise: a module asked more often than it needs is work the loop does for nothing.
    private int _everyTicks = 1;

    /// <param name="worldDir">The world folder; its <c>scripts/</c> subfolder is the module.</param>
    public ScriptedWorldModule(string worldDir)
    {
        ArgumentNullException.ThrowIfNull(worldDir);
        _folder = Path.Combine(worldDir, ScriptsFolder);
    }

    public string Name => "Scripts";

    /// <summary>What the module said when it was read, whether or not it loaded.</summary>
    public IReadOnlyList<ScriptProblem> Problems { get; private set; } = [];

    /// <summary>Whether a module was found, compiled, and loaded.</summary>
    public bool IsLoaded => _loaded is not null;

    /// <summary>The handlers this world's rules actually offered, by name.
    ///
    /// <para>🔴 A handler is matched by name AND arity, so a function whose signature drifts from
    /// <see cref="Handlers"/> is simply not here — it compiles, it loads, and it is never called. That
    /// is invisible from inside the module, which is why this is exposed: a test can hold what the
    /// script wrote against what the engine took.</para></summary>
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

        // Registered whether or not a world has scripts. Both are no-ops with nothing loaded.
        builder.AddObserver(this);
        builder.AddTickWork(this);

        if (!Directory.Exists(_folder)) return;

        var (module, problems) = ScriptCompiler.CompileFolder(_folder, Catalog());
        Problems = problems;

        foreach (ScriptProblem problem in problems)
        {
            if (problem.Severity == ScriptSeverity.Error) Log.Error("Scripts: {Problem}", problem);
            else Log.Warning("Scripts: {Problem}", problem);
        }

        if (module is null)
        {
            if (problems.Count > 0) Log.Error("Scripts: this world's rules did not load; it runs unscripted.");
            return;
        }

        _loaded = LoadedScript.Load(module);

        foreach (ScriptHandler handler in Handlers)
        {
            if (_loaded.Offers(Rules, handler.Name, handler.Arity)) _offered.Add(handler.Name);
        }

        Remember();
        Declare(builder);

        // Only when the script actually declared something to handle. A handler claiming no ids is one
        // the engine would keep in a list and never reach.
        if (_actions.Count > 0) builder.AddActionHandler(this);

        // A policy that always allows and never lingers is what the engine already does, so registering
        // one a script did not write would put a call into the death path for no answer.
        if (_onMayDie) builder.AddDeathPolicy(this);
        if (_onLinger) builder.AddLingerPolicy(this);

        Log.Information("Scripts: loaded {Module} ({Handlers} handler(s), {Actions} action(s)).",
                        module.Name, _offered.Count(h => h != "Configure"), _actions.Count);
    }

    /// <summary>
    /// Lets the script declare, with each declaration guarded on its own.
    ///
    /// <para>🔴 <b>A collision cannot be allowed to stop the server.</b> Two modules claiming one
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
        if (!_offered.Contains("Configure")) return;

        var declaring = new Declaring(builder, _actions, _loaded!.Models, n => _everyTicks = n);
        ScriptOutcome outcome = _loaded.Call(Rules, "Configure", declaring);
        declaring.Close();

        if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());

        if (outcome.Fault is not null)
        {
            Log.Error("Scripts: Configure failed - {Fault}", outcome.Fault);
        }

        foreach (string refused in declaring.Refused)
        {
            Log.Error("Scripts: {Refused}", refused);
        }

        Problems = [.. Problems, .. declaring.Refused.Select(
            r => new ScriptProblem("MS0004", ScriptSeverity.Warning, r, _loaded.Name, 0, 0))];
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
        _onLinger = _offered.Contains("OnLinger");
    }

    /// <summary>Hands the module the world. Everything a binding does goes through this.</summary>
    public void Start(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    // ── What the world tells it ───────────────────────────────────────────────

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
    /// a neighbouring map — everything within the seamless view is pointable, and a handler given only
    /// x and y would act on the wrong tile the moment somebody stood near a border.</para>
    ///
    /// <para><b>How far a verb reaches is the game's question.</b> The engine re-checks that the square is
    /// one the player could see; whether they are close enough to do that particular thing is not
    /// something the engine could know.</para>
    /// </summary>
    public void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at)
    {
        // The target reaches a script as its NAME rather than as a Player, because the boundary has no
        // way to carry "somebody, or nobody" — a registered type has no optional form. Blank is the
        // answer for a verb offered on a square or on the HUD, which is most of them.
        if (_onAction)
            Run("OnAction", from, actionId, World.NameOf(on), (long)at.Map, (long)at.X, (long)at.Y);
    }

    // ── What it does on the tick ──────────────────────────────────────────────

    /// <summary>How often the loop comes back, as the rules asked. Read after they have declared, so
    /// <c>game.TickEvery</c> is what decides it.</summary>
    public int EveryTicks => _everyTicks;

    /// <summary>
    /// The module's tick, and then each player on it.
    ///
    /// <para>🔴 <b>The per-player handler is the only way a script can walk the roster.</b> Nothing
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

    // ── What it decides ────────────────────────────────────────────────

    /// <summary>Whether somebody dies, asked of the rules.
    ///
    /// <para>🔴 <b>A handler that fails allows the death.</b> The alternative is a world where nobody
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

    /// <summary>How long a dropped player's body stays, asked of the rules. Nought takes them out at
    /// once, which is what the engine does with no policy at all.</summary>
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
    /// <para>🔴 <b>A handler that fails is logged and the world carries on.</b> This runs in the middle
    /// of a join, a step, or a tick — so throwing from here would mean one game's mistake ending the
    /// event for everybody, and on the tick it would mean ending the world.</para>
    /// </summary>
    private void Run(string handler, params object?[] arguments)
    {
        ScriptOutcome outcome = _loaded!.Call(Rules, handler, arguments);

        if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());
        if (outcome.Fault is null) return;

        Log.Error("Scripts: {Handler} failed - {Fault}", handler, outcome.Fault);

        // A handler that fails every time would otherwise fill a log with the same line at the tick
        // rate. What it did once it will do again, so it is asked once more and then left alone.
        if (Repeats(outcome.Fault.Kind)) Disable(handler);
    }

    /// <summary>The same, for a handler whose ANSWER is the point. Null where it failed, which every
    /// caller reads as "the engine's own default" rather than as a decision.</summary>
    private object? Ask(string handler, params object?[] arguments)
    {
        ScriptOutcome outcome = _loaded!.Call(Rules, handler, arguments);

        if (outcome.Output.Length > 0) Log.Information("Scripts: {Output}", outcome.Output.TrimEnd());
        if (outcome.Fault is null) return outcome.Value;

        Log.Error("Scripts: {Handler} failed - {Fault}", handler, outcome.Fault);
        if (Repeats(outcome.Fault.Kind)) Disable(handler);

        return null;
    }

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
        var player = c.Type("Player");
        var game = c.Type("Builder");
        var records = c.Type("Records");
        var verb = c.Type("Verb");
        var panel = c.Type("Panel");

        verb
            .Action("OnTile", [], (v, _) => Verbal(v).OnTile(),
                "Offer it in the menu of a square. The default, and where most verbs belong.")
            .Action("OnPlayer", [], (v, _) => Verbal(v).OnPlayer(),
                "Offer it in the menu of another player, who arrives as 'on' in OnAction.")
            .Action("OnNpc", [], (v, _) => Verbal(v).OnNpc(),
                "Offer it in the menu of a creature. Declaring one is what gives a plain creature a "
                + "menu at all.")
            .Action("OnHud", [], (v, _) => Verbal(v).OnHud(),
                "Offer it as a button on the HUD, which is about the player rather than about anything "
                + "they are pointing at.")
            .Action("Key", [ScriptType.Text], (v, a) => Verbal(v).Key(a.AsText(0)),
                "A key that reaches it without the menu: B, E, J, K, N, P, Q, R, T, U, Y, or Z. The key "
                + "acts on the square the player faces.")
            .Action("Opens", [ScriptType.Text], (v, a) => Verbal(v).Opens(a.AsText(0)),
                "The panel it opens, by the id given to game.Panel. One that was never declared is "
                + "refused by name rather than drawing a button that does nothing.")
            .Action("NeedsAtLeast", [ScriptType.Text, ScriptType.Integer],
                (v, a) => Verbal(v).NeedsAtLeast(a.AsText(0), a.AsInteger(1)),
                "Offered only to a body carrying at least that much under that key. Below it the entry "
                + "is greyed rather than missing, so a player can tell the verb exists.")
            .Action("NeedsCarrying", [ScriptType.Text],
                (v, a) => Verbal(v).NeedsCarrying(a.AsText(0)),
                "Offered only to a body that carries that key at all.")
            .Action("NeedsNothing", [ScriptType.Text],
                (v, a) => Verbal(v).NeedsNothing(a.AsText(0)),
                "Offered only to a body that does NOT carry that key.");

        panel
            .Action("Key", [ScriptType.Text], (p, a) => Screen(p).Key(a.AsText(0)),
                "A key that opens it: B, E, J, K, N, P, Q, R, T, U, Y, or Z.")
            .Action("Button", [ScriptType.Text, ScriptType.Text],
                (p, a) => Screen(p).Button(a.AsText(0), a.AsText(1)),
                "A button along its bottom: a caption, and the id of a verb it calls.")
            .Action("Heading", [ScriptType.Text], (p, a) => Screen(p).Heading(a.AsText(0)),
                "A heading on this panel, separating the rows under it.")
            .Action("Field", [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (p, a) => Screen(p).Field(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A row on this panel: a key read live off the player, a caption, and a color as red, "
                + "green, and blue. All three nought leaves the color to the client.")
            .Action("Badge", [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (p, a) => Screen(p).Badge(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "The same, drawn as a small tag with no caption.")
            .Action("Meter", [ScriptType.Text, ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (p, a) => Screen(p).Meter(a.AsText(0), a.AsText(1), a.AsText(2),
                    (int)a.AsInteger(3), (int)a.AsInteger(4), (int)a.AsInteger(5)),
                "A bar on this panel, filled by one key against another.");

        records
            .Action("Stored", [ScriptType.Text, ScriptType.Text],
                (r, a) => Shape(r).Stored(a.AsText(0), a.AsText(1)),
                "Where these records live: the folder under the world, and what each file is called "
                + "before its number. Only wanted for records that already exist on disk — a new game "
                + "says nothing and lets the model's name decide.")
            .Action("Caption", [ScriptType.Text, ScriptType.Text],
                (r, a) => Shape(r).Caption(a.AsText(0), a.AsText(1)),
                "What one field is called on the form. Only wanted where the field's own name is not "
                + "the words an author should read.")
            .Action("Range", [ScriptType.Text, ScriptType.Integer, ScriptType.Integer],
                (r, a) => Shape(r).Range(a.AsText(0), a.AsInteger(1), a.AsInteger(2)),
                "The bounds of a whole-number field. Equal bounds mean unbounded.")
            .Action("Length", [ScriptType.Text, ScriptType.Integer],
                (r, a) => Shape(r).Length(a.AsText(0), a.AsInteger(1)),
                "How long a text field may be. Zero means no limit.");

        player
            .Action("Message", [ScriptType.Text], (who, a) =>
            {
                World.Tell(Who(who), a.AsText(0));
                return null;
            }, "Sends a line of text to this player, and to nobody else.")
            .Value("IsHere", ScriptType.Truth, (who, _) => World.IsInWorld(Who(who)),
                "Whether they are still in the world. A handle outlives the body it names.")
            .Value("Map", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).Map,
                "Which map they are standing on, or zero when they are nowhere.")
            .Value("X", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).X,
                "How far across that map they are.")
            .Value("Y", ScriptType.Integer, (who, _) => (long)World.PlaceOf(Who(who)).Y,
                "How far down it.")

            // The attribute bag, which is where everything a GAME counts lives. A key a body does not
            // have reads as zero or as empty text, with Has for the question that tells them apart —
            // a rule asking "how much stamina" wants a number, not a decision about absence.
            .Function("Has", ScriptType.Truth, [ScriptType.Text],
                (who, a) => World.AttributesOf(Who(who))?.Has(a.AsText(0)) ?? false,
                "Whether they carry that key at all, which is what tells absence from zero.")
            .Function("Number", ScriptType.Integer, [ScriptType.Text],
                (who, a) => Attribute(who, a.AsText(0)) is { } v ? v.AsLong() : 0L,
                "What they carry under that key, or zero where they carry nothing.")
            .Function("Text", ScriptType.Text, [ScriptType.Text],
                (who, a) => Attribute(who, a.AsText(0))?.AsText() ?? string.Empty,
                "The same, as text, or empty where they carry nothing.")
            .Action("SetNumber", [ScriptType.Text, ScriptType.Integer], (who, a) =>
            {
                World.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsInteger(1)));
                return null;
            }, "Writes that key, and ships it to everyone entitled to see it.")
            .Action("SetText", [ScriptType.Text, ScriptType.Text], (who, a) =>
            {
                World.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsText(1)));
                return null;
            }, "The same, with text.")

            .Function("WarpTo", ScriptType.Truth, [ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (who, a) => World.Warp(Who(who), new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2))),
                "Puts them on that map, x and y. False for a square that is not a real tile.")
            .Action("Give", [ScriptType.Integer, ScriptType.Integer], (who, a) =>
            {
                World.Give(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            }, "Puts that many of an item in their bag.")
            .Action("Take", [ScriptType.Integer, ScriptType.Integer], (who, a) =>
            {
                World.Take(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            }, "Takes that many out of it, worn ones included.")

            // 🔴 The other half of a verb used ON somebody. OnAction carries the target as a NAME,
            // because the boundary has no way to say "somebody, or nobody" in an argument — but it can
            // say it in a RESULT, which is what makes this the shape that works.
            .Function("Find", player.AsType.OrNothing(), [ScriptType.Text],
                (_, a) => Somebody(a.AsText(0)),
                "The body behind a name, or nothing where nobody is answering to it. What OnAction's "
                + "'on' is for: a name is what arrives, and this is what reads and writes through it.");

        game
            .Function("Records", records.AsType,
                [ScriptType.Text, ScriptType.Text, ScriptType.Text, ScriptType.Integer],
                (b, a) => Build(b).Records(a.AsText(0), a.AsText(1), a.AsText(2), a.AsInteger(3)),
                "A kind of record this game authors, taken from one of this world's own models: the "
                + "model's name, a plural caption, a singular one, and how many there may be. Every "
                + "field of the model becomes a row on the form — an enumeration as a drop-down over "
                + "its members, another model as a picker over that model's records. Hands the records "
                + "back, so the rest can be said about them.")
            .Action("Attribute", [ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Attribute(a.AsText(0), a.AsText(1)),
                "Declares a key this game counts, and who may see it: none, owner, or viewport.")
            .Action("TickEvery", [ScriptType.Integer],
                (b, a) => Build(b).TickEvery(a.AsInteger(0)),
                "How often OnTick and OnPlayerTick come round, in ticks. One by default, which is every "
                + "tick — a rule about resting or the weather wants far less than that.")
            .Action("EquipSlot", [ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).EquipSlot(a.AsText(0), a.AsText(1)),
                "A place on a body something can be worn. A caption left blank becomes the key, as "
                + "words.")
            .Action("Heading", [ScriptType.Text],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Heading,
                    string.Empty, string.Empty, a.AsText(0), 0, 0, 0),
                "A heading on the sidebar, separating the rows under it.")
            .Action("Field", [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Text,
                    a.AsText(0), string.Empty, a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A sidebar row: a key read live off the player, a caption, and a color as red, green, "
                + "and blue. All three nought leaves the color to the client.")
            .Action("Badge", [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Badge,
                    a.AsText(0), string.Empty, a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "The same, drawn as a small tag with no caption.")
            .Action("Meter", [ScriptType.Text, ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Row(DisplaySurfaces.Hud, DisplayStyle.Meter,
                    a.AsText(0), a.AsText(1), a.AsText(2),
                    (int)a.AsInteger(3), (int)a.AsInteger(4), (int)a.AsInteger(5)),
                "A sidebar bar, filled by one key against another.")
            .Action("Bar",
                [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Bar(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A bar over every body's head, in a color given as red, green, and blue, each 0 to 255.")
            .Function("Action", verb.AsType, [ScriptType.Text, ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Action(a.AsText(0), a.AsText(1), a.AsText(2)),
                "A verb this game offers, under a heading of its own. Picking it calls OnAction. Offered "
                + "in a square's menu until the verb says otherwise, and handed back so what it is "
                + "offered on, what key reaches it, and what it needs are each a line of their own.")
            .Function("Panel", panel.AsType,
                [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Panel(a.AsText(0), a.AsText(1), a.AsInteger(2), a.AsInteger(3)),
                "A screen of this game's own: an id, a title, and how wide and tall it is. Handed back, "
                + "so its rows and its buttons are written underneath it.");
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
            "The world is not there yet. A script's Configure runs before the world does, so it declares "
            + "rather than acts; anything about a player belongs in a handler.");

    private AttributeValue? Attribute(object? who, string key) =>
        World.AttributesOf(Who(who)) is { } bag && bag.TryGet(key, out AttributeValue value) ? value : null;

    /// <summary>The handle behind a script's <c>Player</c>. Nothing a script can write reaches this.</summary>
    private static EntityHandle Who(object? value) => value is EntityHandle handle ? handle : EntityHandle.None;

    private static Declaring Build(object? value) =>
        value as Declaring ?? throw new InvalidOperationException("This is not a builder.");

    private static Describing Shape(object? value) =>
        value as Describing ?? throw new InvalidOperationException("These are not records.");

    private static Verb Verbal(object? value) =>
        value as Verb ?? throw new InvalidOperationException("This is not a verb.");

    private static Panel Screen(object? value) =>
        value as Panel ?? throw new InvalidOperationException("This is not a panel.");

    /// <summary>
    /// One verb, handed back so what it is offered on, what reaches it, and what it needs are each a
    /// line of their own.
    ///
    /// <para>A surface is chosen by CALLING one rather than by naming one, so a word that is not a
    /// surface cannot be written. The default is the square, which is where most verbs belong.</para>
    /// </summary>
    private sealed class Verb(Declaring.PendingVerb verb)
    {
        public object? OnTile() => Offered(ActionSurface.Tile);

        public object? OnPlayer() => Offered(ActionSurface.Player);

        public object? OnNpc() => Offered(ActionSurface.Npc);

        public object? OnHud() => Offered(ActionSurface.Hud);

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

        /// <summary>What a body must be carrying for this to be offered rather than greyed.
        ///
        /// <para>Both halves are this one condition: the client greys the entry and the server refuses
        /// the call, and neither is told separately. A verb a player can see but cannot use yet reads as
        /// a game with more in it than a verb that is simply missing.</para></summary>
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

        public object? Button(string label, string actionId)
        {
            panel.Buttons.Add(new PanelButton(label, actionId));
            return null;
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
    /// <para>⚠ A field is still named as TEXT, because nothing in Compass carries a field's identity as
    /// a value. A name that is not there is refused at load, naming the model and the field.</para>
    ///
    /// <para>⚠ Records that were REFUSED still hand one of these back rather than nothing, because a
    /// script calling a method on nothing would fail where a refusal has already been recorded. It does
    /// nothing, quietly: the one line saying what went wrong is the useful one.</para>
    /// </summary>
    private sealed class Describing(Declaring declaring, string model, bool declared)
    {
        public object? Stored(string folder, string prefix)
        {
            if (declared) declaring.Stored(model, folder, prefix);
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

        // Refinements are queued rather than applied, which is what lets one be written before the
        // records it refines. A line that had to follow its Records call would be the ordering this
        // shape exists to remove, back in a quieter form.
        private readonly List<Refinement> _refinements = [];

        // The rest of what a script declares, held for the same reason: each is handed back so more can
        // be said about it, and the engine freezes what it is given.
        private readonly List<PendingRow> _rows = [];
        private readonly List<PendingVerb> _verbs = [];
        private readonly List<PendingPanel> _panels = [];

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

            // 🔴 A field may be typed as a model that was never declared as records, and the picker
            // would then list nothing at all - which reads as a game with no records in it rather than
            // as a model somebody forgot to declare. So the field is dropped and named.
            foreach (PendingRecords records in _records)
            {
                records.Fields.RemoveAll(f =>
                {
                    if (f.Kind is not FieldKind.RecordRef || f.RecordFamilyId is not { } target
                        || Described(target) is not null)
                    {
                        return false;
                    }

                    Refuse($"the field '{records.Id}.{f.Key}'",
                           $"it points at '{f.RecordFamilyId}', which no game.Records call declared");
                    return true;
                });
            }

            foreach (ChoiceSet set in _choices)
            {
                Guard($"the choices '{set.Id}'", () => builder.AddChoiceSet(set));
            }

            foreach (PendingRecords records in _records)
            {
                Guard($"the records '{records.Id}'", () => builder.AddFamily(new RecordFamily
                {
                    Id = records.Id,
                    Directory = records.Folder,
                    FilePrefix = records.Prefix,
                    LabelKey = records.Label,
                    SingularLabelKey = records.Singular.Length > 0 ? records.Singular : records.Label,
                    DefaultLimit = records.Limit > 0
                        ? (int)Math.Min(records.Limit, int.MaxValue)
                        : 1000,
                    Fields = records.Fields,
                }));
            }

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
                    Buttons = [.. panel.Buttons],
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
        /// <para>🔴 A verb opening a panel nobody declared is the silent half here: the button draws,
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
                OpensPanel = opens,
                When = verb.When,
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
        /// <para>🔴 <b>The model IS the declaration.</b> It says which fields there are and what each
        /// one holds, so nothing repeats that: a field typed as an ENUMERATION becomes a drop-down over
        /// that enumeration's members, and one typed as ANOTHER MODEL becomes a picker listing that
        /// model's records by name. A field the engine has no equivalent for — a set, an optional, a
        /// function — is left out and named, rather than quietly missing from the form.</para>
        ///
        /// <para>The model's own name is the id, unchanged. A game's records sit beside the engine's
        /// <c>Items</c> and <c>Npcs</c> in one list and in one folder each, so a model named for
        /// something already there has to read as the collision it is.</para>
        ///
        /// <para>⚠ The model is named as TEXT, because Compass has no type values. A typo is a refusal
        /// at load rather than an error at compile, so the refusal lists the models that do exist.</para>
        /// </summary>
        public Describing Records(string modelName, string label, string singular, long limit)
        {
            ScriptModelInfo? shape = models.FirstOrDefault(
                m => string.Equals(m.Name, modelName, StringComparison.Ordinal));

            if (shape is null)
            {
                string known = string.Join(", ", models.Select(m => m.Name));
                Refuse($"the records '{modelName}'",
                       known.Length > 0
                           ? $"no model of that name is declared - this world has: {known}"
                           : "this world declares no models at all");
                return new Describing(this, modelName, declared: false);
            }

            if (Described(shape.Name) is not null)
            {
                Refuse($"the records '{shape.Name}'", "they are already declared");
                return new Describing(this, modelName, declared: false);
            }

            var fields = new List<FieldDescriptor>();
            foreach (ScriptModelField field in shape.Fields)
            {
                if (Describe(shape.Name, field) is { } descriptor) fields.Add(descriptor);
            }

            if (fields.Count == 0)
            {
                Refuse($"the records '{shape.Name}'", "none of its fields can be authored");
                return new Describing(this, modelName, declared: false);
            }

            var records = new PendingRecords(shape.Name) { Fields = fields };

            if (label.Length > 0) records.Label = label;
            if (singular.Length > 0) records.Singular = singular;
            if (limit > 0) records.Limit = limit;

            _records.Add(records);
            return new Describing(this, shape.Name, declared: true);
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
        /// <para>⚠ Only for records that ALREADY EXIST. Left unsaid, the folder is the model's name
        /// lowercased and the file name is that without a trailing "s" — which is right for
        /// <c>sites/site1.json</c> and wrong for <c>species/species1.json</c>, because English is not a
        /// rule. A new game should say nothing here and let the default name the files.</para></summary>
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

            public List<FieldDescriptor> Fields { get; init; } = [];
        }

        public object? Attribute(string key, string visibility) => Guard($"the attribute '{key}'", () =>
            builder.Attributes.Declare(key, Visibility(visibility)));

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
        /// game's own — and a color of nought leaves it to the client.</summary>
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
            return new Verb(verb);
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
        private void Refuse(string what, string why) => _refused.Add($"{what} was refused - {why}");

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
            public string Opens { get; set; } = string.Empty;
            public ActionCondition When { get; set; } = ActionCondition.Always;
        }

        internal sealed class PendingPanel
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public int Width { get; init; }
            public int Height { get; init; }
            public string Key { get; set; } = string.Empty;
            public List<PanelButton> Buttons { get; } = [];
        }
    }
}

/// <summary>One function a world's rules may write, and when the engine calls it.</summary>
/// <param name="Name">What the function is called.</param>
/// <param name="Arity">How many arguments it takes, which is part of what the engine asks for.</param>
/// <param name="Signature">How it is written, without the <c>public</c> in front. A handler that
/// yields something carries its type there, because that is where Compass writes one.</param>
/// <param name="When">What has just happened when it is called.</param>
public sealed record ScriptHandler(string Name, int Arity, string Signature, string When);
