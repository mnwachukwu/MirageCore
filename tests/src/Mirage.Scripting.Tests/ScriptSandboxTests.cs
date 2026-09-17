using Compass.Compiler.Semantics;
using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// What a module a stranger wrote is not allowed to touch.
///
/// <para>A world folder is something one person hands to another, and its scripts run inside the server
/// process as the server's user. The runtime offers no way to take a capability away from code already
/// running, so the answer is to refuse the module before it loads — which is also the better answer,
/// because the author finds out at deploy rather than mid-game.</para>
/// </summary>
[TestFixture]
public class ScriptSandboxTests
{
    private static (CompassScript? Script, IReadOnlyList<ScriptProblem> Problems) Module(string body) =>
        ScriptCompiler.CompileModule($$"""
            shared model Rules
                public function Go()
            {{body}}
                end function
            end model
            """, "rules.cm");

    [Test]
    public void AModuleThatReadsAFile_IsRefused()
    {
        var (script, problems) = Module("""
                    Console.Write(File.Read("accounts.json").Or("nothing"));
            """);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null, "it must not become a program at all");
            Assert.That(problems.Select(p => p.Id), Does.Contain("MS0003"));
            Assert.That(problems.Single(p => p.Id == "MS0003").Message, Does.Contain("Files"));
        });
    }

    [Test]
    public void AModuleThatWritesAFile_IsRefused()
    {
        var (script, problems) = Module("""
                    File.Write("gotcha.txt", "hello");
            """);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems.Select(p => p.Id), Does.Contain("MS0003"));
        });
    }

    /// <summary>
    /// The clock is not a danger the way the filesystem is. It is refused so a world cannot behave
    /// differently on the machine it is audited on. Time of day is
    /// something the engine tells a module, through a member the engine registered.
    /// </summary>
    [Test]
    public void AModuleThatReadsTheMachineClock_IsRefused()
    {
        var (script, problems) = Module("""
                    Console.Write(DateTime.Now.Year);
            """);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems.Single(p => p.Id == "MS0003").Message, Does.Contain("Clock"));
        });
    }

    /// <summary>A module doing two of these is told about both, rather than about the first.</summary>
    [Test]
    public void AModuleDoingSeveral_IsToldAboutEach()
    {
        var (_, problems) = Module("""
                    File.Write("gotcha.txt", "hello");
                    Console.Write(DateTime.Now.Year);
            """);

        Assert.That(problems.Count(p => p.Id == "MS0003"), Is.EqualTo(2));
    }

    [Test]
    public void AnOrdinaryModule_IsNotRefused()
    {
        var (script, problems) = Module("""
                    integer total = 0;

                    loop for i = 1 to 10
                        total = total + i;
                    end loop

                    Console.Write(total);
            """);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Not.Null, string.Join("; ", problems));
            Assert.That(problems.Select(p => p.Id), Does.Not.Contain("MS0003"));
        });
    }

    /// <summary>
    /// 🔴 <b>The rule is derived from the language, never transcribed from it.</b>
    ///
    /// <para>Written the other way — a list here of the members that reach outside a program — this check
    /// would keep passing while it stopped meaning anything, the moment Compass grew one more. That
    /// failure has no symptom at all: the guard is green, the module loads, and the thing it was meant to
    /// stop is simply allowed. So what is asserted is not WHICH capabilities exist but that the refusal
    /// is taken from the language's own list of them.</para>
    ///
    /// <para>This fails the day a capability is added to Compass and not to the refusal — which cannot
    /// happen, because the refusal is that list.</para>
    /// </summary>
    [Test]
    public void TheRefusalIsTheLanguagesOwnList()
    {
        var everything = Enum.GetValues<Reaches>()
            .Where(r => r != Reaches.Nothing)
            .Select(r => r.ToString())
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(ScriptSandbox.Refused, Is.EqualTo(everything).AsCollection);
            Assert.That(ScriptSandbox.Refused, Is.Not.Empty,
                "a language that reaches nothing would make this whole check pointless, and would be "
                + "worth noticing rather than passing silently");
        });
    }
}
