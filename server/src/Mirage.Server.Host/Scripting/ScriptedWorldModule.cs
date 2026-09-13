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
///     public function OnAction(Player who, string action, integer map, integer x, integer y)
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
public sealed class ScriptedWorldModule : ICoreModule, IWorldObserver, ITickWork, IActionHandler, IDisposable
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
        new("Configure", 1, "Configure(Builder game)",
            "once, before the world exists, so a module can declare what it adds"),
        new("OnPlayerJoined", 1, "OnPlayerJoined(Player who)",
            "a player is in the world and has been sent everything they need"),
        new("OnPlayerLeft", 1, "OnPlayerLeft(Player who)",
            "they have left, while their record is still readable"),
        new("OnPlayerMoved", 3, "OnPlayerMoved(Player who, integer fromX, integer fromY)",
            "every accepted step, seam crossings included"),
        new("OnAction", 5, "OnAction(Player who, string action, integer map, integer x, integer y)",
            "the player picked one of this module's own verbs"),
        new("OnTick", 0, "OnTick()", "every tick"),
    ];

    private readonly string _folder;
    private readonly List<string> _actions = [];
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private IWorld? _world;
    private LoadedScript? _loaded;
    private bool _onJoined, _onLeft, _onMoved, _onTick, _onAction;

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

        var declaring = new Declaring(builder, _actions);
        ScriptOutcome outcome = _loaded!.Call(Rules, "Configure", declaring);
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
    public void Invoke(EntityHandle from, string actionId, in WorldPlace at)
    {
        if (_onAction) Run("OnAction", from, actionId, (long)at.Map, (long)at.X, (long)at.Y);
    }

    // ── What it does on the tick ──────────────────────────────────────────────

    /// <summary>
    /// Every tick, which is the rate a game reacting to time wants. A module that wants less asks for
    /// less inside its own handler; the engine has no way to guess what "less" means for somebody
    /// else's rules.
    /// </summary>
    public int EveryTicks => 1;

    public void Tick(long tick)
    {
        if (_onTick) Run("OnTick");
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
            }, "Takes that many out of it, worn ones included.");

        game
            .Action("Attribute", [ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Attribute(a.AsText(0), a.AsText(1)),
                "Declares a key this game counts, and who may see it: none, owner, or viewport.")
            .Action("Heading", [ScriptType.Text],
                (b, a) => Build(b).Field(DisplayStyle.Heading, string.Empty, string.Empty, a.AsText(0)),
                "A heading on the sidebar, separating the rows under it.")
            .Action("Field", [ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Field(DisplayStyle.Text, a.AsText(0), string.Empty, a.AsText(1)),
                "A sidebar row: a caption, and a key read live off the player.")
            .Action("Badge", [ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Field(DisplayStyle.Badge, a.AsText(0), string.Empty, a.AsText(1)),
                "The same, drawn as a small tag with no caption.")
            .Action("Meter", [ScriptType.Text, ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Field(DisplayStyle.Meter, a.AsText(0), a.AsText(1), a.AsText(2)),
                "A sidebar bar, filled by one key against another.")
            .Action("Bar",
                [ScriptType.Text, ScriptType.Text, ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (b, a) => Build(b).Bar(a.AsText(0), a.AsText(1),
                    (int)a.AsInteger(2), (int)a.AsInteger(3), (int)a.AsInteger(4)),
                "A bar over every body's head, in a color given as red, green, and blue, each 0 to 255.")
            .Action("Action", [ScriptType.Text, ScriptType.Text, ScriptType.Text],
                (b, a) => Build(b).Action(a.AsText(0), a.AsText(1), a.AsText(2)),
                "A verb in the square menu, under a heading of its own. Picking it calls OnAction.");
    });

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

    public void Dispose() => _loaded?.Dispose();

    /// <summary>
    /// What a script's <c>Configure</c> writes into.
    ///
    /// <para>Not valid after that function returns, the same way <see cref="ICoreBuilder"/> is not — a
    /// script keeping the reference and declaring from a handler is told so rather than declaring into
    /// an engine that has already been built around it.</para>
    /// </summary>
    private sealed class Declaring(ICoreBuilder builder, List<string> actions)
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
        private int _fields, _bars, _verbs;
        private bool _closed;

        public IReadOnlyList<string> Refused => _refused;

        public void Close() => _closed = true;

        public object? Attribute(string key, string visibility) => Guard($"the attribute '{key}'", () =>
            builder.Attributes.Declare(key, Visibility(visibility)));

        public object? Field(DisplayStyle style, string valueKey, string maxKey, string label) =>
            Guard($"the {style.ToString().ToLowerInvariant()} '{label}'", () => builder.AddDisplayField(new DisplayField
            {
                Surface = DisplaySurfaces.Hud,
                Style = style,
                ValueKey = valueKey,
                MaxKey = maxKey,
                LabelKey = label,
                Ordinal = After + _fields++,
            }));

        /// <summary>A bar in a color given one channel at a time, which is how a person picks one.
        ///
        /// <para>Out-of-range channels are clamped rather than refused, for the same reason a bar past its
        /// own maximum draws full: losing a whole row over a mistyped digit is a worse answer than drawing
        /// it slightly wrong.</para></summary>
        public object? Bar(string valueKey, string maxKey, int red, int green, int blue) =>
            Guard($"the bar on '{valueKey}'", () => builder.AddOverheadBar(new OverheadBar
            {
                ValueKey = valueKey,
                MaxKey = maxKey,
                Rgb = (Channel(red) << 16) | (Channel(green) << 8) | Channel(blue),
                Ordinal = After + _bars++,
            }));

        private static int Channel(int value) => Math.Clamp(value, 0, 255);

        public object? Action(string id, string label, string group) => Guard($"the action '{id}'", () =>
        {
            builder.AddAction(new GameAction
            {
                Id = id,
                LabelKey = label,
                GroupKey = group,
                Surface = ActionSurface.Tile,
                Ordinal = After + _verbs++,
            });

            actions.Add(id);
        });

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
    }
}

/// <summary>One function a world's rules may write, and when the engine calls it.</summary>
/// <param name="Name">What the function is called.</param>
/// <param name="Arity">How many arguments it takes, which is part of what the engine asks for.</param>
/// <param name="Signature">How it is written, without the <c>public function</c> in front.</param>
/// <param name="When">What has just happened when it is called.</param>
public sealed record ScriptHandler(string Name, int Arity, string Signature, string When);
