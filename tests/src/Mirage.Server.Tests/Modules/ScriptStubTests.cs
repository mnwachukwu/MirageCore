using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// The written-down catalog, and the shipped world's copy of it.
///
/// <para>🔴 <b>The engine's types exist only inside a running server.</b> The catalog is built while a
/// world loads and its members are delegates in that process, so a checker outside cannot see them.
/// Every declaring line in a world's rules then reads as an unknown type, and an author told their
/// correct code is wrong on every line that matters learns to ignore the tooling.</para>
///
/// <para>The stub is the catalog written as Compass a checker can read. This holds it to the shape
/// the language accepts, and rewrites the copy inside the shipped world so the seed carries one.</para>
/// </summary>
[TestFixture]
public class ScriptStubTests
{
    private static ScriptCatalog Catalog() => new ScriptedWorldModule("no-such-world").Catalog();

    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);

        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? throw new InvalidOperationException("The repository root is not above here.");
    }

    private static string ShippedWorld() =>
        Path.Combine(Repository(), "server", "src", "Mirage.Server.Host", "world");

    /// <summary>Every registered type reaches the stub, with every member on it.</summary>
    [Test]
    public void TheStubCarriesTheWholeCatalog()
    {
        ScriptCatalog catalog = Catalog();
        string stub = ScriptStubs.Stub(catalog);

        Assert.Multiple(() =>
        {
            foreach (ScriptTypeInfo type in catalog.Types)
            {
                Assert.That(stub, Does.Contain($"model {type.Name}"), $"{type.Name} is not declared");

                foreach (ScriptMemberInfo member in type.Members)
                {
                    Assert.That(stub, Does.Contain(member.Name), $"{type.Name}.{member.Name} is missing");
                }
            }
        });
    }

    /// <summary>⚠ A value is read without parentheses, so it has to be a field rather than a function.
    /// Declared as a function, <c>who.IsHere</c> would not compile in any script that reads it.</summary>
    [Test]
    public void AValueIsAFieldAndACallIsAnOpenFunction()
    {
        string stub = ScriptStubs.Stub(Catalog());

        Assert.Multiple(() =>
        {
            Assert.That(stub, Does.Contain("public boolean IsHere;"));
            Assert.That(stub, Does.Contain("public abstract function Message(string line);"));
            Assert.That(stub, Does.Contain("public abstract boolean function Has(string key);"));
        });
    }

    /// <summary>🔴 A parameter carries the name an author reads in the editor.
    ///
    /// <para>The name is the whole of what a completion list has to go on while somebody is typing the
    /// call. Six positions to count is the thing this replaced.</para></summary>
    [Test]
    public void AParameterIsNamedForWhatItIs()
    {
        string stub = ScriptStubs.Stub(Catalog());

        Assert.Multiple(() =>
        {
            Assert.That(stub, Does.Contain(
                "function Field(string key, string caption, integer red, integer green, integer blue)"));
            Assert.That(stub, Does.Contain("function WarpTo(integer map, integer x, integer y)"));
            Assert.That(stub, Does.Contain("function Are(string plural, string singular, integer limit)"));
        });
    }

    /// <summary>🔴 A parameter named for a reserved word is refused where it is registered.
    ///
    /// <para>It would otherwise produce a stub that does not parse, in the one file an author cannot
    /// fix — and every declaring line in their own rules would then read as an error.</para></summary>
    [Test]
    public void AParameterNamedForAReservedWord_IsRefusedAtRegistration()
    {
        Assert.That(
            () => ScriptCatalog.Declare(c => c.Type("Thing").Action(
                "Say", [ScriptType.Text.Named("model")], (_, _) => null)),
            Throws.ArgumentException.With.Message.Contains("model"));
    }

    /// <summary>What a model and a member each ARE is written above them, so the editor can show it
    /// on hover.
    ///
    /// <para>A Compass documentation comment is a block opening with <c>@summary:</c>. A block that
    /// does not is ordinary prose, which is how a remark above a declaration stays a remark.</para>
    ///
    /// <para>⚠ The MODEL's note is the one a list of its members cannot supply: what a value of it
    /// stands for and where one comes from. Hovering <c>Player</c> is a different question from
    /// hovering <c>Player.Message</c>.</para>
    /// </summary>
    [Test]
    public void EveryModelAndMemberCarriesItsNoteAsADocumentationComment()
    {
        ScriptCatalog catalog = Catalog();
        string stub = ScriptStubs.Stub(catalog);

        int members = catalog.Types.SelectMany(t => t.Members).Count(m => m.Note.Length > 0);
        int models = catalog.Types.Count(t => t.Note.Length > 0);

        Assert.Multiple(() =>
        {
            Assert.That(members, Is.GreaterThan(40), "the catalog itself has notes to write down");
            Assert.That(models, Is.EqualTo(catalog.Types.Count), "and every type says what it is");

            Assert.That(
                stub.Split("@summary:").Length - 1, Is.EqualTo(members + models),
                "everything with a note carries one, and nothing else does");

            Assert.That(stub, Does.Contain(
                "@summary: Whether they are still in the world. A handle outlives the body it names."),
                "a member's");

            Assert.That(stub, Does.Contain("@summary: Somebody in the world, as a handle"), "a model's");
        });
    }

    /// <summary>Every folder holding scripts is named, because a project's source does not descend.</summary>
    [Test]
    public void TheProjectNamesEveryFolderThatHoldsScripts()
    {
        string project = ScriptStubs.Project(["scripts", "scripts/declare", "scripts/rules"]);

        Assert.Multiple(() =>
        {
            Assert.That(project, Does.Contain("source .mirage"), "the engine's types come first");
            Assert.That(project, Does.Contain("source scripts/declare"));
            Assert.That(project, Does.Contain("source scripts/rules"));
        });
    }

    /// <summary>
    /// The shipped world carries a current copy, and this writes one when it drifts.
    ///
    /// <para>It ships in the seed, so a fresh install can be opened in an editor before the server has
    /// ever run. Regenerating here rather than by hand is the same arrangement
    /// <c>docs/scripting-api.md</c> uses: the file cannot be stale and quietly wrong.</para>
    /// </summary>
    [Test]
    public void TheShippedWorldsStubsAreCurrent()
    {
        string world = ShippedWorld();
        string scripts = Path.Combine(world, ScriptedWorldModule.ScriptsFolder);

        Assume.That(Directory.Exists(scripts), "the shipped world carries no scripts folder");

        var folders = Directory.EnumerateDirectories(scripts, "*", SearchOption.AllDirectories)
            .Prepend(scripts)
            .Where(d => Directory.EnumerateFiles(d, "*" + ScriptModule.Extension).Any())
            .Select(d => Path.GetRelativePath(world, d).Replace('\\', '/'))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

        bool wrote = ScriptStubs.Write(world, Catalog(), folders);

        Assert.That(wrote, Is.False,
            "the shipped world's editor stubs were out of date and have been rewritten. "
            + "Run the tests again, and commit "
            + $"{ScriptStubs.Folder}/{ScriptStubs.StubFile} and {ScriptStubs.ProjectFile}.");
    }
}
