using Mirage.Scripting;
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
///     public function OnPlayerMoved(Player who, integer fromX, integer fromY)
///         who.Say("You were at " + fromX + ", " + fromY + ".");
///     end function
/// end model
/// </code>
///
/// <para><b>A world with no scripts is the ordinary case</b>, and so is one whose scripts do not compile:
/// the problems are logged against the world and the server runs the game unscripted. A server that
/// refused to start because somebody's rules had a typo would be a server an operator cannot recover
/// without an editor.</para>
/// </summary>
public sealed class ScriptedWorldModule : ICoreModule, IWorldObserver, ITickWork, IDisposable
{
    /// <summary>The folder inside a world that holds its rules.</summary>
    public const string ScriptsFolder = "scripts";

    /// <summary>The shared model the engine looks for its handlers on.</summary>
    public const string Rules = "Rules";

    private readonly string _folder;
    private IWorld? _world;
    private LoadedScript? _loaded;
    private bool _onJoined, _onLeft, _onMoved, _onTick;

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

    public void Configure(ICoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Registered whether or not a world has scripts. Both are no-ops with nothing loaded, and
        // deciding here would mean reading the folder before the engine is built.
        builder.AddObserver(this);
        builder.AddTickWork(this);
    }

    /// <summary>
    /// Reads the world's scripts, checks them against what the engine offers, and loads them.
    ///
    /// <para>The catalog is built here rather than in <see cref="Configure"/> because its bindings act
    /// through the world, and there is no world to act through until now.</para>
    /// </summary>
    public void Start(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;

        if (!Directory.Exists(_folder)) return;

        var (module, problems) = ScriptCompiler.CompileFolder(_folder, Catalog(world));
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
        _onJoined = _loaded.Offers(Rules, nameof(OnPlayerJoined), 1);
        _onLeft = _loaded.Offers(Rules, nameof(OnPlayerLeft), 1);
        _onMoved = _loaded.Offers(Rules, nameof(OnPlayerMoved), 3);
        _onTick = _loaded.Offers(Rules, "OnTick", 0);

        Log.Information("Scripts: loaded {Module} ({Handlers} handler(s)).", module.Name, Handlers);
    }

    private int Handlers => (_onJoined ? 1 : 0) + (_onLeft ? 1 : 0) + (_onMoved ? 1 : 0) + (_onTick ? 1 : 0);

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
        if (handler == nameof(OnPlayerJoined)) _onJoined = false;
        else if (handler == nameof(OnPlayerLeft)) _onLeft = false;
        else if (handler == nameof(OnPlayerMoved)) _onMoved = false;
        else _onTick = false;

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
    /// <para>Everything here is something <see cref="IWorld"/> already offered. A script reaches exactly
    /// this and nothing else: there is no way to name a type that is not registered, and the language's
    /// own way out — the filesystem, the clock — is refused at compile time.</para>
    /// </summary>
    internal static ScriptCatalog Catalog(IWorld world) => ScriptCatalog.Declare(c =>
    {
        var player = c.Type("Player");

        player
            .Action("Say", [ScriptType.Text], (who, a) =>
            {
                world.Tell(Who(who), a.AsText(0));
                return null;
            })
            .Value("IsHere", ScriptType.Truth, (who, _) => world.IsInWorld(Who(who)))
            .Value("Map", ScriptType.Integer, (who, _) => (long)world.PlaceOf(Who(who)).Map)
            .Value("X", ScriptType.Integer, (who, _) => (long)world.PlaceOf(Who(who)).X)
            .Value("Y", ScriptType.Integer, (who, _) => (long)world.PlaceOf(Who(who)).Y)

            // The attribute bag, which is where everything a GAME counts lives. A key a body does not
            // have reads as zero or as empty text, with Has for the question that tells them apart —
            // a rule asking "how much stamina" wants a number, not a decision about absence.
            .Function("Has", ScriptType.Truth, [ScriptType.Text],
                (who, a) => world.AttributesOf(Who(who))?.Has(a.AsText(0)) ?? false)
            .Function("Number", ScriptType.Integer, [ScriptType.Text],
                (who, a) => Attribute(world, who, a.AsText(0)) is { } v ? v.AsLong() : 0L)
            .Function("Text", ScriptType.Text, [ScriptType.Text],
                (who, a) => Attribute(world, who, a.AsText(0))?.AsText() ?? string.Empty)
            .Action("SetNumber", [ScriptType.Text, ScriptType.Integer], (who, a) =>
            {
                world.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsInteger(1)));
                return null;
            })
            .Action("SetText", [ScriptType.Text, ScriptType.Text], (who, a) =>
            {
                world.SetAttribute(Who(who), a.AsText(0), AttributeValue.From(a.AsText(1)));
                return null;
            })

            .Function("WarpTo", ScriptType.Truth, [ScriptType.Integer, ScriptType.Integer, ScriptType.Integer],
                (who, a) => world.Warp(Who(who), new WorldPlace((int)a.AsInteger(0), (int)a.AsInteger(1), (int)a.AsInteger(2))))
            .Action("Give", [ScriptType.Integer, ScriptType.Integer], (who, a) =>
            {
                world.Give(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            })
            .Action("Take", [ScriptType.Integer, ScriptType.Integer], (who, a) =>
            {
                world.Take(Who(who), (int)a.AsInteger(0), (int)a.AsInteger(1));
                return null;
            });
    });

    private static AttributeValue? Attribute(IWorld world, object? who, string key) =>
        world.AttributesOf(Who(who)) is { } bag && bag.TryGet(key, out AttributeValue value) ? value : null;

    /// <summary>The handle behind a script's <c>Player</c>. Nothing a script can write reaches this.</summary>
    private static EntityHandle Who(object? value) => value is EntityHandle handle ? handle : EntityHandle.None;

    public void Dispose() => _loaded?.Dispose();
}
