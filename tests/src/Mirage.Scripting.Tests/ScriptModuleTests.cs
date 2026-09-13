using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// A module made of more than one file.
///
/// <para>🔴 <b>A game is not one file, so the unit a game is written in cannot be one either.</b> Compass
/// has folders, imports, namespaces and qualified names; the demo game is already five files in C#, and
/// nothing about writing it in Compass would make it one. The sources are checked TOGETHER, which is what
/// makes any of that work across a module.</para>
///
/// <para>Which files a module holds is this engine's rule rather than the compiler's — the compiler is
/// handed a set of sources and has no opinion about where they came from.</para>
/// </summary>
[TestFixture]
public class ScriptModuleTests
{
    private string _dir = "";

    [SetUp]
    public void CreateScratchDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirage-module-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>🔴 The load-bearing one: a program split across files, reaching a model declared in
    /// another. A host that compiled each file alone would fail here with an unknown name.</summary>
    [Test]
    public void SourcesAreCheckedTogether_SoOneReachesAnother()
    {
        var (script, problems) = ScriptCompiler.CompileModule(
            [new ScriptSource("Greeting.cm", Helper), new ScriptSource("Program.cm", Main)], "demo");

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    [Test]
    public void AModuleWithNoSourceAtAll_IsRefused()
    {
        var (script, problems) = ScriptCompiler.CompileModule([], "empty");

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems, Is.Not.Empty, "and never in silence");
        });
    }

    // ── The folder rule ───────────────────────────────────────────────────────

    [Test]
    public void AFolderOfFiles_IsOneModule()
    {
        Write("Greeting.cm", Helper);
        Write("Program.cm", Main);

        var (script, problems) = ScriptCompiler.CompileFolder(_dir);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>🔴 Recursively, and with no manifest. A game module is authored by somebody arranging
    /// their own folder and ships as that folder; asking them to also list the files they can see is
    /// bookkeeping this engine exists to remove.</summary>
    [Test]
    public void SubfoldersBelongToTheModuleToo()
    {
        Write("rules/deep/Greeting.cm", Helper);
        Write("Program.cm", Main);

        var (script, problems) = ScriptCompiler.CompileFolder(_dir);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    [Test]
    public void AnythingThatIsNotCompassSourceIsIgnored()
    {
        Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"Hello, module.\""));
        Write("README.md", "notes about this game");
        Write("records/species1.json", "{}");

        var (script, problems) = ScriptCompiler.CompileFolder(_dir);

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>A problem names the file the way its author wrote it, relative to the module — not half
    /// a machine's directory tree, which is unreadable in a log and different on every machine.</summary>
    [Test]
    public void AProblemNamesTheFileRelativeToTheModule()
    {
        Write("rules/broken.cm", "shared model Broken\n    public string function For()\n        yield 7;\n    end function\nend model");
        Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"hi\""));

        var (_, problems) = ScriptCompiler.CompileFolder(_dir);

        var error = problems.First(p => p.Severity == ScriptSeverity.Error);
        Assert.Multiple(() =>
        {
            Assert.That(error.File, Is.EqualTo("rules/broken.cm"));
            Assert.That(error.File, Does.Not.Contain(_dir));
        });
    }

    [Test]
    public void OneBrokenFile_StopsTheWholeModule()
    {
        Write("Greeting.cm", "shared model Greeting\n    public string function For()\n        yield 7;\n    end function\nend model");
        Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"hi\""));

        var (script, problems) = ScriptCompiler.CompileFolder(_dir);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null, "a game cannot half-load");
            Assert.That(problems.Any(p => p.Severity == ScriptSeverity.Error), Is.True);
        });
    }

    [Test]
    public void AFolderThatIsNotThere_IsRefusedWithAReason()
    {
        var (script, problems) = ScriptCompiler.CompileFolder(Path.Combine(_dir, "absent"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems, Is.Not.Empty);
        });
    }

    [Test]
    public void AModuleIsNamedAfterItsFolder()
    {
        Write("Program.cm", Main.Replace("Greeting.For(\"module\")", "\"hi\""));

        var (script, _) = ScriptCompiler.CompileFolder(_dir);

        Assert.That(script!.Name, Is.EqualTo(new DirectoryInfo(_dir).Name));
    }

    // ── Where the text comes from ─────────────────────────────────────────────

    /// <summary>
    /// 🔴 Scripts are read through a delegate, and that is the load-bearing part of this design.
    ///
    /// <para>A world folder is the thing somebody zips up and hands to another machine. Wired to
    /// <c>File.ReadAllText</c> directly, a game's rules would be tied to loose files on a disk forever;
    /// behind the delegate, the same module loads out of an archive, a database, or an editor holding
    /// something not yet saved. This test is that claim, made with no disk at all.</para>
    /// </summary>
    [Test]
    public void AModuleCanComeFromSomewhereOtherThanTheDisk()
    {
        var archive = new Dictionary<string, string>
        {
            ["Greeting.cm"] = Helper,
            ["Program.cm"] = Main,
        };

        var sources = ScriptModule.Read(
            "in-memory",
            list: _ => archive.Keys.OrderBy(k => k, StringComparer.Ordinal),
            read: path => archive[path]);

        var (script, problems) = ScriptCompiler.CompileModule(sources, "packed");

        Assert.That(script, Is.Not.Null, string.Join("; ", problems));
        Assert.That(script!.Run().Output, Does.Contain("Hello, module."));
    }

    /// <summary>The order sources are handed over is the order problems are reported in, and a listing
    /// that changed between two machines would make one log unreadable against the other.</summary>
    [Test]
    public void TheSourcesOfAFolderComeBackInAStableOrder()
    {
        Write("b.cm", Helper);
        Write("a.cm", Main);
        Write("c/d.cm", Helper.Replace("Greeting", "Other"));

        var first = ScriptModule.Read(_dir).Select(s => s.Name).ToList();
        var again = ScriptModule.Read(_dir).Select(s => s.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(again));
            Assert.That(first, Is.EqualTo(first.OrderBy(n => n, StringComparer.Ordinal).ToList()));
        });
    }
}
