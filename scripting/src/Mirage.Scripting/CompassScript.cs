using Compass.Compiler;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Ast;
using Compass.Compiler.Text;

namespace Mirage.Scripting;

/// <summary>
/// A script that checked, and can be run.
///
/// <para>Holding the checked program rather than its source is what makes running it cheap: the whole
/// front end has already had its say, and what is left is walking a tree.</para>
/// </summary>
public sealed class CompassScript
{
    private readonly IReadOnlyList<CompilationUnit> _lowered;
    private readonly SemanticModel _model;

    internal CompassScript(IReadOnlyList<CompilationUnit> lowered, SemanticModel model, string name)
    {
        _lowered = lowered;
        _model = model;
        Name = name;
    }

    /// <summary>What to call this script when something goes wrong in it.</summary>
    public string Name { get; }

    /// <summary>
    /// Runs the script, returning whatever it yielded and everything it printed.
    ///
    /// <para>🔴 <b>Nothing a script does reaches the server's own console.</b> Its output is captured and
    /// handed back, because a line a script printed is a line about a GAME and belongs wherever that
    /// game's operator is reading — not interleaved with the server's log at whatever moment a player
    /// happened to trip over it.</para>
    ///
    /// <para>🔴 <b>Reading input is not a thing a script can do.</b> The reader is empty and stays empty,
    /// so a script asking for a line gets end-of-input immediately rather than stopping the server on a
    /// console nobody is typing at. That is the first of the sandbox's answers and the cheapest to
    /// get wrong, because the default is <c>Console.In</c>.</para>
    /// </summary>
    public ScriptRun Run()
    {
        using var output = new StringWriter();
        using var noInput = new StringReader("");

        try
        {
            int exit = Compass.Interpreter.Interpreter.Run(_lowered, _model, output, noInput);
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
/// <para>🔴 <b>Nothing runs until everything checks.</b> A script that fails the front end produces no
/// program at all, so a game cannot half-load: every name resolves, every expression has a type, and
/// nothing is read before it holds a value, or there is nothing to run.</para>
///
/// <para>This is deliberately the whole of the embedding: parse, check, lower, run, against Compass's
/// ordinary public front end. Reaching into its internals would make this engine a fork of somebody's
/// compiler, and the thing that would break first is the editor — a module that compiles in VS Code and
/// not on the server is worse than one that compiles nowhere.</para>
/// </summary>
public static class ScriptCompiler
{
    /// <summary>
    /// Checks one piece of source and hands back a runnable script, or null with the reasons.
    /// </summary>
    /// <param name="source">The script's text.</param>
    /// <param name="name">What to call it in a problem — a file name, usually.</param>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) Compile(
        string source, string name = "<script>")
    {
        ArgumentNullException.ThrowIfNull(source);
        return Compile([new ScriptSource(name, source)], name);
    }

    /// <summary>
    /// Checks a whole module — however many sources it is made of — and hands back one runnable script.
    ///
    /// <para>🔴 <b>A game module is not one file, and this is where that is true.</b> The sources are
    /// checked TOGETHER, so a model declared in one is reachable from another and the namespaces,
    /// imports and qualified names Compass offers all work across a module the way they do in any other
    /// Compass program. A host that compiled each file alone would reject the second line of any module
    /// worth writing.</para>
    ///
    /// <para>Where these came from is not asked. A folder on disk is one answer and
    /// <see cref="ScriptModule.Read"/> gives it; an archive or an editor's unsaved buffer are others,
    /// and neither needs anything here to change.</para>
    /// </summary>
    /// <param name="sources">The module's sources. Empty is refused: nothing to run is not a program.</param>
    /// <param name="name">What to call the module as a whole.</param>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) Compile(
        IEnumerable<ScriptSource> sources, string name)
    {
        ArgumentNullException.ThrowIfNull(sources);

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

        // requireEntryPoint: a module IS a program, and one with nothing to run is a folder somebody
        // forgot to finish rather than a library this engine has any use for.
        SemanticModel model = FrontEnd.Check(units, diagnostics, requireEntryPoint: true);

        var problems = Describe(diagnostics);
        if (diagnostics.HasErrors) return (null, problems);

        // Lowering is not optional and not part of checking: the interpreter is written against the
        // simplified tree and meets shapes it deliberately does not handle if it is given the raw one.
        var lowered = Lowering.Lower(units, model);
        return (new CompassScript(lowered, model, name), problems);
    }

    /// <summary>Checks the module in a folder on disk, named after the folder.</summary>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileFolder(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Directory.Exists(root))
        {
            return (null, [new ScriptProblem(
                "MS0002", ScriptSeverity.Error, "There is no module folder here.", root, 0, 0)]);
        }

        return Compile(ScriptModule.Read(root), new DirectoryInfo(root).Name);
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
