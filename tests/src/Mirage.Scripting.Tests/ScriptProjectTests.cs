using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// A script made of more than one file.
///
/// <para>🔴 <b>A game is not one file, so the unit a game is written in cannot be one either.</b> Compass
/// has folders, imports, namespaces and projects that reference other projects; the demo game is already
/// five files in C#, and nothing about writing it in Compass would make it one. What a build is made of
/// is Compass's question, answered by Compass's own reader — these pin that the answer arrives here
/// intact rather than being re-decided.</para>
/// </summary>
[TestFixture]
public class ScriptProjectTests
{
    private string _dir = "";

    [SetUp]
    public void CreateScratchDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveScratchDir()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Write(string relative, string contents)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private const string Helper = """
        shared model Greeting
            public string function For(string who)
                yield "Hello, " + who + ".";
            end function
        end model
        """;

    private const string Main = """
        shared model Program
            function Main()
                Console.WriteLine(Greeting.For("module"));
            end function
        end model
        """;

    [Test]
    public void ASingleFile_CompilesByItsPath()
    {
        string path = Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"Hello, module.\""));

        var (script, problems) = ScriptCompiler.CompileAt(path);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>🔴 The load-bearing one: a program split across files, reaching a model declared in
    /// another. A host that compiled only the file it was handed would fail here with an unknown name.</summary>
    [Test]
    public void AFolderOfFiles_CompilesAsOneProgram()
    {
        Write("Greeting.cm", Helper);
        string program = Write("Program.cm", Main);

        var (script, problems) = ScriptCompiler.CompileAt(program);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>A <c>.cmp</c> says what a build is made of, including sources in folders of their
    /// own — which is how a module of any size is arranged.</summary>
    [Test]
    public void AProjectFile_CompilesWhatItNames()
    {
        Write("models/Greeting.cm", Helper);
        Write("Program.cm", Main);
        string project = Write("Game.cmp", """
            project Game
                source Program.cm
                source models
            end project
            """);

        var (script, problems) = ScriptCompiler.CompileAt(project);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>A project names itself, and that name is what a problem in it is reported against —
    /// an operator reading a log has the module's name rather than whichever file happened to fail.</summary>
    [Test]
    public void AProjectIsKnownByItsOwnName()
    {
        Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"hi\""));
        string project = Write("Game.cmp", """
            project Game
                source Program.cm
            end project
            """);

        var (script, _) = ScriptCompiler.CompileAt(project);

        Assert.That(script!.Name, Does.Contain("Game"));
    }

    [Test]
    public void APathNamingNothing_IsRefusedWithAReason()
    {
        var (script, problems) = ScriptCompiler.CompileAt(Path.Combine(_dir, "absent.cm"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems, Is.Not.Empty, "and never in silence");
        });
    }

    /// <summary>A file that is neither is refused rather than read hopefully: a file is Compass because
    /// it says so, not because something tried.</summary>
    [Test]
    public void AFileOfSomeOtherKind_IsRefused()
    {
        string path = Write("notes.txt", "this is not a program");

        var (script, problems) = ScriptCompiler.CompileAt(path);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems, Is.Not.Empty);
        });
    }

    /// <summary>🔴 A broken file anywhere in the build stops the whole build. Loading the half that
    /// checked would be a game running rules its author is still writing.</summary>
    [Test]
    public void OneBrokenFile_StopsTheWholeBuild()
    {
        Write("Greeting.cm", "shared model Greeting\n    public string function For()\n        yield 7;\n    end function\nend model");
        string program = Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"hi\""));

        var (script, problems) = ScriptCompiler.CompileAt(program);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems.Any(p => p.Severity == ScriptSeverity.Error), Is.True);
        });
    }
}
