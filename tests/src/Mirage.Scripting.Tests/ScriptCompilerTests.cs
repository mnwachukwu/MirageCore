using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// Compass source, checked and run inside this process.
///
/// <para>The whole of the embedding is Compass's own public front end driven in the order its
/// command-line tool drives it. These pin the parts a SERVER needs that a command-line tool does not:
/// that a script's mistakes come back as values, that a broken script cannot take the process with it,
/// and that a script cannot reach the console at either end.</para>
/// </summary>
[TestFixture]
public class ScriptCompilerTests
{
    private const string Hello = """
        shared model Program
            function Main()
                Console.WriteLine("Hello from a script.");
            end function
        end model
        """;

    [Test]
    public void AScriptThatChecks_Runs()
    {
        var (script, problems) = ScriptCompiler.Compile(Hello, "hello.cm");

        Assert.That(script, Is.Not.Null, "it should have checked: " + string.Join("; ", problems));
        var run = script!.Run();

        Assert.Multiple(() =>
        {
            Assert.That(run.Completed, Is.True, run.Failure);
            Assert.That(run.Output, Does.Contain("Hello from a script."));
        });
    }

    /// <summary>🔴 What a script prints is captured, never written to the server's own output. A line a
    /// script printed is about a GAME and belongs where that game's operator is reading.</summary>
    [Test]
    public void WhatAScriptPrints_ComesBackRatherThanGoingToTheConsole()
    {
        var before = Console.Out;

        var (script, _) = ScriptCompiler.Compile(Hello, "hello.cm");
        var run = script!.Run();

        Assert.Multiple(() =>
        {
            Assert.That(run.Output, Is.Not.Empty, "it printed something, and this is where that went");
            Assert.That(Console.Out, Is.SameAs(before), "and the server's own output was never redirected");
        });
    }

    /// <summary>
    /// 🔴 A script cannot read input. The interpreter's default reader is <c>Console.In</c>, so a script
    /// asking for a line would stop the whole server on a console nobody is typing at — every player
    /// frozen by one line of somebody's game script.
    /// </summary>
    [Test]
    public void AScriptAskingForInput_GetsEndOfInputRatherThanStoppingTheServer()
    {
        const string reads = """
            shared model Program
                function Main()
                    string? line = Console.Read();
                    Console.WriteLine("done");
                end function
            end model
            """;

        var (script, problems) = ScriptCompiler.Compile(reads, "reads.cm");
        Assert.That(script, Is.Not.Null, "it should have checked: " + string.Join("; ", problems));

        // The assertion is that this RETURNS. A test that hangs here is the bug.
        var run = script!.Run();

        Assert.That(run.Output, Does.Contain("done"));
    }

    // ── What a broken script does ─────────────────────────────────────────────

    [Test]
    public void AScriptThatDoesNotCheck_ProducesNoProgramAtAll()
    {
        var (script, problems) = ScriptCompiler.Compile("""
            shared model Program
                function Main()
                    integer n = "not a number";
                end function
            end model
            """, "broken.cm");

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null, "a game cannot half-load");
            Assert.That(problems.Any(p => p.Severity == ScriptSeverity.Error), Is.True);
        });
    }

    /// <summary>A problem names where it is, because an operator reading a log has nothing else to go
    /// on — no editor, no source in front of them, and possibly somebody else's script.</summary>
    [Test]
    public void AProblemNamesTheFileAndThePlace()
    {
        var (_, problems) = ScriptCompiler.Compile("""
            shared model Program
                function Main()
                    integer n = "not a number";
                end function
            end model
            """, "broken.cm");

        var error = problems.First(p => p.Severity == ScriptSeverity.Error);
        Assert.Multiple(() =>
        {
            Assert.That(error.File, Is.EqualTo("broken.cm"));
            Assert.That(error.Line, Is.GreaterThan(0));
            Assert.That(error.Id, Is.Not.Empty, "and the rule that objected");
            Assert.That(error.ToString(), Does.Contain("broken.cm"));
        });
    }

    [Test]
    public void AFileWithNothingToRun_IsRefused()
    {
        var (script, problems) = ScriptCompiler.Compile("""
            shared model Helper
                public integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """, "library.cm");

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null, "a script is a program, and this one has no entry point");
            Assert.That(problems, Is.Not.Empty);
        });
    }

    /// <summary>
    /// 🔴 A script that throws is a broken GAME, not a broken server. The failure comes back as a value
    /// so one game's mistake cannot end the process every other player is connected to.
    /// </summary>
    [Test]
    public void AScriptThatThrows_ComesBackAsAFailureRatherThanTakingTheProcess()
    {
        var (script, problems) = ScriptCompiler.Compile("""
            shared model Program
                function Main()
                    integer zero = 0;
                    integer boom = 1 / zero;
                end function
            end model
            """, "boom.cm");
        Assert.That(script, Is.Not.Null, "it should have checked: " + string.Join("; ", problems));

        var run = script!.Run();

        Assert.Multiple(() =>
        {
            Assert.That(run.Completed, Is.False);
            Assert.That(run.Failure, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void AScriptCanBeRunMoreThanOnce()
    {
        var (script, _) = ScriptCompiler.Compile(Hello, "hello.cm");

        // Checking is the expensive half and is done once; running is walking a tree. A host that had to
        // recompile per call could not afford to put a script on a per-step event.
        Assert.Multiple(() =>
        {
            Assert.That(script!.Run().Output, Does.Contain("Hello"));
            Assert.That(script.Run().Output, Does.Contain("Hello"));
        });
    }
}
