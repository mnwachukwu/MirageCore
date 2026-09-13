using Compass.Compiler;
using Compass.Compiler.Diagnostics;
using Compass.Compiler.Parsing;
using Compass.Compiler.Semantics;
using Compass.Compiler.Ast;
using Compass.Compiler.Text;
using Compass.Cli;

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
/// <para>This is deliberately the whole of the embedding. Compass is a finished language with its own
/// repository, and reaching into it would make this engine a fork of somebody's compiler; everything
/// here is its ordinary public front end, driven in the order its own command-line tool drives it.</para>
/// </summary>
public static class ScriptCompiler
{
    /// <summary>The extension a Compass source file carries.</summary>
    public const string Extension = ".cm";

    /// <summary>
    /// Checks one piece of source and hands back a runnable script, or null with the reasons.
    /// </summary>
    /// <param name="source">The script's text.</param>
    /// <param name="name">What to call it in a problem — a file name, usually.</param>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) Compile(
        string source, string name = "<script>")
    {
        ArgumentNullException.ThrowIfNull(source);

        var text = new SourceText(source, name);
        var diagnostics = new DiagnosticBag();

        CompilationUnit unit = Parser.Parse(text, diagnostics);
        // requireEntryPoint: a script IS a program, and one with nothing to run is a file somebody
        // forgot to finish rather than a library this engine has any use for.
        SemanticModel model = FrontEnd.Check([unit], diagnostics, requireEntryPoint: true);

        var problems = Describe(diagnostics);
        if (diagnostics.HasErrors) return (null, problems);

        // Lowering is not optional and not part of checking: the interpreter is written against the
        // simplified tree and meets shapes it deliberately does not handle if it is given the raw one.
        var lowered = Lowering.Lower([unit], model);
        return (new CompassScript(lowered, model, name), problems);
    }

    /// <summary>The extension a Compass PROJECT carries — the file that says what a build is made of.</summary>
    public const string ProjectExtension = ".cmp";

    /// <summary>
    /// Checks whatever <paramref name="path"/> names: one <c>.cm</c> file, a <c>.cmp</c> project, or the
    /// folder either sits in.
    ///
    /// <para>🔴 <b>A game module is not one file.</b> Compass has folders, imports, namespaces and
    /// projects that reference other projects, and a module worth writing will use them — the demo game
    /// is already five C# files. So the unit a game declares itself in is whatever Compass says a build
    /// is, which means this defers to Compass's own reader rather than deciding for itself: a second
    /// reader of somebody else's project format is a reader that drifts from the one their tool uses,
    /// and the first sign of it would be a module that builds in their editor and not on this server.</para>
    ///
    /// <para>A project may name its own entry point; a folder is one program by construction.</para>
    /// </summary>
    public static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) CompileAt(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var diagnostics = new DiagnosticBag();

        if (SourceDiscovery.Locate(path, out string problem) is not { } target)
        {
            return (null, [new ScriptProblem("CM0000", ScriptSeverity.Error, problem, path, 0, 0)]);
        }

        if (SourceDiscovery.Gather(target.Path, diagnostics) is not { } compilation)
        {
            return (null, Describe(diagnostics));
        }

        SemanticModel model = FrontEnd.Check(
            compilation.Units, diagnostics, requireEntryPoint: true,
            compilation.Projects, compilation.EntryPoint);

        var problems = Describe(diagnostics);
        if (diagnostics.HasErrors) return (null, problems);

        var lowered = Lowering.Lower(compilation.Units, model);
        return (new CompassScript(lowered, model, compilation.Label), problems);
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
