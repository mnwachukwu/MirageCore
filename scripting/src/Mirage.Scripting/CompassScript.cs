using Compass.Compiler;
using Compass.Compiler.Ast;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Text;

namespace Mirage.Scripting;

/// <summary>
/// A module that checked, and can be loaded or run.
///
/// <para>Holding the checked program rather than its source is what makes running it cheap: the whole
/// front end has already had its say, and what is left is walking a tree.</para>
/// </summary>
public sealed class CompassScript
{
    internal CompassScript(IReadOnlyList<CompilationUnit> lowered, SemanticModel model, string name)
    {
        Lowered = lowered;
        Model = model;
        Name = name;
    }

    /// <summary>What to call this module when something goes wrong in it.</summary>
    public string Name { get; }

    /// <summary>Whether it declares a <c>Main</c>, which a game module does not.</summary>
    public bool HasEntryPoint => Model.EntryPoint is not null;

    /// <summary>The shape of every model this module declares, in declaration order.
    ///
    /// <para>What a game DESCRIBES with a model — a kind of record, a form — the engine reads from
    /// here rather than from a line-per-field vocabulary: the model already says which fields there are
    /// and what they hold, and saying it twice is how the two drift.</para>
    ///
    /// <para>Compass's own symbols do not leave this project. This is a description in the engine's
    /// terms, for the same reason <see cref="ScriptType"/> exists.</para></summary>
    public IReadOnlyList<ScriptModelInfo> Models => _models ??= ReadModels();

    private IReadOnlyList<ScriptModelInfo>? _models;

    private IReadOnlyList<ScriptModelInfo> ReadModels()
    {
        var found = new List<ScriptModelInfo>();

        foreach (TypeSymbol type in Model.AllTypes())
        {
            // A model, and one this module declared rather than one the language provides.
            if (type is not ModelSymbol model || model.Members.Count == 0) continue;

            var fields = new List<ScriptModelField>();
            var functions = new List<ScriptModelFunction>();

            foreach (var (_, members) in model.Members)
            {
                foreach (Symbol member in members)
                {
                    switch (member)
                    {
                        case FieldSymbol field:
                            fields.Add(Describe(field));
                            break;

                        case FunctionSymbol function:
                            functions.Add(new ScriptModelFunction(
                                function.Name, function.Parameters.Count, function.IsShared));
                            break;
                    }
                }
            }

            if (fields.Count > 0) found.Add(new ScriptModelInfo(model.Name, fields, functions));
        }

        return found;
    }

    /// <summary>One field, as something the engine can act on.</summary>
    private static ScriptModelField Describe(FieldSymbol field)
    {
        TypeSymbol type = field.Type;

        // An enumeration IS a closed set of choices, so a field typed as one needs nothing else said
        // about it. This is what lets a form have a drop-down without the script declaring one.
        if (type is EnumerationSymbol choices)
        {
            var members = new List<string>();
            foreach (var (_, symbols) in choices.Members)
            {
                foreach (Symbol symbol in symbols)
                {
                    if (symbol is EnumMemberSymbol value) members.Add(value.Name);
                }
            }

            return new ScriptModelField(field.Name, ScriptFieldShape.Choice, type.Display, members);
        }

        // Another model, which is a field pointing AT one of those rather than holding one. The type
        // is the whole declaration: nothing else has to say which model it points at.
        if (type is ModelSymbol other)
        {
            return new ScriptModelField(field.Name, ScriptFieldShape.Reference, other.Name, []);
        }

        ScriptFieldShape shape = type.Display switch
        {
            "string" or "character" => ScriptFieldShape.Text,
            "integer" => ScriptFieldShape.Whole,
            "real" or "float" or "fraction" => ScriptFieldShape.Fraction,
            "boolean" => ScriptFieldShape.Truth,

            // A set, an optional, a function. Named rather than dropped, so whatever reads this can
            // say which field it could not use and why.
            _ => ScriptFieldShape.Unsupported,
        };

        return new ScriptModelField(field.Name, shape, type.Display, []);
    }

    internal IReadOnlyList<CompilationUnit> Lowered { get; }

    internal SemanticModel Model { get; }

    /// <summary>
    /// Runs the module's entry point once, returning whatever it yielded and everything it printed.
    ///
    /// <para>This is the one-shot shape — a tool, a test, an operator trying something. What a GAME does
    /// is <see cref="LoadedScript"/>: loaded once, called many times, with the state each call leaves
    /// behind there for the next.</para>
    ///
    /// <para>🔴 <b>Nothing a script does reaches the server's own console.</b> Its output is captured and
    /// handed back, because a line a script printed is a line about a GAME and belongs wherever that
    /// game's operator is reading — not interleaved with the server's log at whatever moment a player
    /// happened to trip over it.</para>
    ///
    /// <para>🔴 <b>Reading input is not a thing a script can do.</b> The reader is empty and stays empty,
    /// so a script asking for a line gets end-of-input immediately rather than stopping the server on a
    /// console nobody is typing at. That is the cheapest of the sandbox's answers to get wrong, because
    /// the default is <c>Console.In</c>.</para>
    /// </summary>
    public ScriptRun Run()
    {
        using var output = new StringWriter();
        using var noInput = new StringReader(string.Empty);

        try
        {
            int exit = Compass.Interpreter.Interpreter.Run(Lowered, Model, output, noInput);
            return new ScriptRun(exit, output.ToString(), Failure: null);
        }
        catch (Exception ex)
        {
            // A script that threw is a broken GAME, not a broken server: the failure comes back as a
            // value so the caller can log it against the script and carry on serving everybody else.
            return new ScriptRun(ExitCode: -1, output.ToString(), Failure: ex.Message);
        }
    }
}

/// <summary>What one run of a script produced.</summary>
/// <param name="ExitCode">What the script's entry point yielded, or -1 when it threw.</param>
/// <param name="Output">Everything it printed, in order.</param>
/// <param name="Failure">Why it stopped early, or null when it ran to the end.</param>
public sealed record ScriptRun(int ExitCode, string Output, string? Failure)
{
    /// <summary>True when the script ran to the end without throwing.</summary>
    public bool Completed => Failure is null;
}

/// <summary>
/// Turns Compass source into something the engine can run.
///
/// <para>🔴 <b>Nothing runs until everything checks.</b> A module that fails the front end produces no
/// program at all, so a game cannot half-load: every name resolves, every expression has a type, and
/// nothing is read before it holds a value, or there is nothing to run.</para>
///
/// <para>🔴 <b>A module is refused for what it can reach as well as for what it gets wrong.</b> See
/// <see cref="ScriptSandbox"/>: the refusal happens here, between checking and lowering, so a module that
/// could touch the machine never becomes a program at all. The author finds out at deploy rather than
/// mid-game, which is the whole reason it is a compile-time answer.</para>
///
/// <para>This is deliberately the whole of the embedding: parse, check, refuse, lower, convert. Reaching
/// into the compiler's internals would make this engine a fork of somebody's language, and the thing
/// that would break first is the editor — a module that compiles in VS Code and not on the server is
/// worse than one that compiles nowhere.</para>
/// </summary>
public static class ScriptCompiler
{
    /// <summary>
    /// Checks a whole module — however many sources it is made of — against what the engine offers.
    ///
    /// <para>🔴 <b>A game module is not one file, and this is where that is true.</b> The sources are
    /// checked TOGETHER, so a model declared in one is reachable from another and the namespaces,
    /// imports and qualified names Compass offers all work across a module the way they do in any other
    /// Compass program. A host that compiled each file alone would reject the second line of any module
    /// worth writing.</para>
    ///
    /// <para>🔴 <b>No entry point is required, and that is the difference between a module and a
    /// program.</b> Nothing the engine calls is <c>Main</c>: a module is a set of handlers, and demanding
    /// a function nobody runs would make every module carry an empty one.</para>
    ///
    /// <para>Where these came from is not asked. A folder on disk is one answer and
    /// <see cref="ScriptModule.Read"/> gives it; an archive or an editor's unsaved buffer are others, and
    /// neither needs anything here to change.</para>
    /// </summary>
    /// <param name="sources">The module's sources. Empty is refused: nothing to run is not a module.</param>
    /// <param name="name">What to call the module as a whole.</param>
    /// <param name="catalog">What the engine offers it, or nothing.</param>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileModule(
        IEnumerable<ScriptSource> sources, string name, ScriptCatalog? catalog = null) =>
        Check(sources, name, catalog, requireEntryPoint: false);

    /// <summary>One piece of source, which is a module of one.</summary>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileModule(
        string source, string name = "<script>", ScriptCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CompileModule([new ScriptSource(name, source)], name, catalog);
    }

    /// <summary>Checks the module in a folder on disk, named after the folder.</summary>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileFolder(
        string root, ScriptCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root))
        {
            return (null, [new ScriptProblem(
                "MS0002", ScriptSeverity.Error, "There is no module folder here.", root, 0, 0)]);
        }

        return CompileModule(ScriptModule.Read(root), new DirectoryInfo(root).Name, catalog);
    }

    /// <summary>
    /// Checks a program — something with an entry point, to be run once and discarded.
    ///
    /// <para>A tool's shape rather than a game's. <see cref="CompileModule"/> is what a world's rules
    /// are.</para>
    /// </summary>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileProgram(
        string source, string name = "<script>", ScriptCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Check([new ScriptSource(name, source)], name, catalog, requireEntryPoint: true);
    }

    private static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) Check(
        IEnumerable<ScriptSource> sources, string name, ScriptCatalog? catalog, bool requireEntryPoint)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(name);

        catalog ??= ScriptCatalog.Empty;

        var diagnostics = new DiagnosticBag();
        var units = new List<CompilationUnit>();

        foreach (var (file, text) in sources)
        {
            units.Add(Parser.Parse(new SourceText(text, file), diagnostics));
        }

        if (units.Count == 0)
        {
            return (null, [new ScriptProblem(
                "MS0001", ScriptSeverity.Error, "This module holds no Compass source.", name, 0, 0)]);
        }

        SemanticModel model = FrontEnd.Check(
            units, diagnostics, requireEntryPoint: requireEntryPoint, externals: catalog.Externals);

        var problems = Describe(diagnostics);
        if (diagnostics.HasErrors) return (null, problems);

        // What it can reach is asked only of a module that checked, because a module that did not is
        // already refused and its member bindings mean nothing.
        problems.AddRange(ScriptSandbox.Inspect(model, name));
        if (problems.Any(p => p.Severity == ScriptSeverity.Error)) return (null, problems);

        // Lowering is not optional and not part of checking: the interpreter is written against the
        // simplified tree and meets shapes it deliberately does not handle if it is given the raw one.
        // Closure conversion follows it for the same reason — a function value that captured a local is
        // not runnable until the capture has been made a thing rather than a reference to a frame.
        IReadOnlyList<CompilationUnit> lowered =
            ClosureConversion.Convert(Lowering.Lower(units, model), model);

        return (new CompassScript(lowered, model, name), problems);
    }

    private static List<ScriptProblem> Describe(DiagnosticBag diagnostics)
    {
        var problems = new List<ScriptProblem>();

        foreach (Diagnostic d in diagnostics)
        {
            problems.Add(new ScriptProblem(
                d.Id,
                d.Severity == DiagnosticSeverity.Error ? ScriptSeverity.Error : ScriptSeverity.Warning,
                d.Message,
                d.FileName,
                d.Span.Start.Line,
                d.Span.Start.Column));
        }

        return problems;
    }
}
