using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// A module the server keeps and calls into.
///
/// <para>The thing being proved is that state one call leaves behind is there for the next, since that is
/// what makes an event-driven module possible at all — and that a module which misbehaves is a report
/// rather than a server going down with it.</para>
/// </summary>
[TestFixture]
public class LoadedScriptTests
{
    private const string Counting = """
        shared model Rules
            integer seen = 0;

            public function OnTick()
                Rules.seen = Rules.seen + 1;
            end function

            public integer function Seen()
                yield Rules.seen;
            end function

            public function Greet(string who)
                Console.WriteLine("hello, " + who);
            end function
        end model
        """;

    private static LoadedScript Load(string source, ScriptLimits? limits = null)
    {
        var (script, problems) = ScriptCompiler.CompileModule(source, "rules.cm");
        Assert.That(script, Is.Not.Null, "it should have checked: " + string.Join("; ", problems));

        return LoadedScript.Load(script!, limits ?? ScriptLimits.None);
    }

    [Test]
    public void StateSurvivesFromOneCallToTheNext()
    {
        using var rules = Load(Counting);

        rules.Call("Rules", "OnTick");
        rules.Call("Rules", "OnTick");
        rules.Call("Rules", "OnTick");

        Assert.That(rules.Call("Rules", "Seen").Value, Is.EqualTo(3L));
    }

    [Test]
    public void ArgumentsGoInAndWhatItPrintsComesBack()
    {
        using var rules = Load(Counting);

        var outcome = rules.Call("Rules", "Greet", "Ada");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, outcome.Fault?.ToString());
            Assert.That(outcome.Output.Trim(), Is.EqualTo("hello, Ada"));
        });
    }

    /// <summary>What one call printed is that call's, so a log line is not attributed to the wrong tick.</summary>
    [Test]
    public void OutputIsPerCall()
    {
        using var rules = Load(Counting);

        rules.Call("Rules", "Greet", "Ada");

        Assert.That(rules.Call("Rules", "Greet", "Grace").Output, Does.Not.Contain("Ada"));
    }

    /// <summary>A module is free not to write a handler, so the engine asks rather than calling and catching.</summary>
    [Test]
    public void AskingWhatIsThereDoesNotThrow()
    {
        using var rules = Load(Counting);

        Assert.Multiple(() =>
        {
            Assert.That(rules.Offers("Rules", "OnTick", 0), Is.True);
            Assert.That(rules.Offers("Rules", "OnTick", 1), Is.False, "arity is part of it");
            Assert.That(rules.Offers("Rules", "OnPlayerMoved", 3), Is.False);
            Assert.That(rules.Offers("Nowhere", "OnTick", 0), Is.False);
        });
    }

    [Test]
    public void AskingForAHandlerThatIsNotThere_IsAFaultRatherThanAThrow()
    {
        using var rules = Load(Counting);

        var outcome = rules.Call("Rules", "OnNothing");

        Assert.That(outcome.Fault?.Kind, Is.EqualTo(ScriptFaultKind.NoSuchHandler));
    }

    /// <summary>
    /// 🔴 A handler has to be declared <c>public</c>, and the reason is not obvious: a function only the
    /// HOST calls is a function nothing in the module calls, which is dead code as far as the compiler
    /// can see. Left off, every handler in a module is reported, and an author who has never embedded a
    /// language has no way to guess why. It is written into the module template for that reason, and
    /// this is the fact the template rests on.
    /// </summary>
    [Test]
    public void AHandlerThatIsNotPublic_IsReported()
    {
        var (_, problems) = ScriptCompiler.CompileModule("""
            shared model Rules
                function OnTick()
                    Console.Write("tick");
                end function
            end model
            """, "rules.cm");

        Assert.That(problems.Select(p => p.Id), Does.Contain("CM0410"));
    }

    /// <summary>
    /// 🔴 A module that would never finish is stopped, and the server gets its thread back. Without this
    /// one author's <c>loop while true</c> ends the game for everybody connected, and no amount of care
    /// elsewhere in the engine prevents it.
    /// </summary>
    [Test]
    public void AHandlerThatWouldNeverFinish_IsStopped()
    {
        using var rules = Load("""
            shared model Rules
                public function OnTick()
                    loop while true
                    end loop
                end function
            end model
            """, new ScriptLimits(TimeSpan.FromMilliseconds(50), 0, 512));

        var outcome = rules.Call("Rules", "OnTick");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Fault?.Kind, Is.EqualTo(ScriptFaultKind.RanTooLong));
            Assert.That(outcome.Fault!.File, Is.EqualTo("rules.cm"), "and says where it had got to");
            Assert.That(outcome.Fault.Line, Is.GreaterThan(0));
        });
    }

    /// <summary>The other way a handler fails to end: it grows rather than loops.</summary>
    [Test]
    public void AHandlerThatWouldGrowForever_IsStopped()
    {
        using var rules = Load("""
            shared model Rules
                public function OnTick()
                    string grown = "x";

                    loop while true
                        grown = grown + grown;
                    end loop
                end function
            end model
            """, new ScriptLimits(System.Threading.Timeout.InfiniteTimeSpan, 4 * 1024 * 1024, 512));

        Assert.That(rules.Call("Rules", "OnTick").Fault?.Kind, Is.EqualTo(ScriptFaultKind.AllocatedTooMuch));
    }

    /// <summary>The third: it calls itself forever. Catchable only because the language counts the depth.</summary>
    [Test]
    public void AHandlerThatCallsItselfForever_IsStopped()
    {
        using var rules = Load("""
            shared model Rules
                public function OnTick()
                    Rules.OnTick();
                end function
            end model
            """, new ScriptLimits(System.Threading.Timeout.InfiniteTimeSpan, 0, 64));

        Assert.That(rules.Call("Rules", "OnTick").Fault?.Kind, Is.EqualTo(ScriptFaultKind.WentTooDeep));
    }

    /// <summary>
    /// 🔴 A handler that raises is a broken GAME, not a broken server. It comes back as a value, with the
    /// line, so one game's mistake cannot end the process every other player is connected to.
    /// </summary>
    [Test]
    public void AHandlerThatRaises_ComesBackAsAFaultWithTheLine()
    {
        using var rules = Load("""
            shared model Rules
                public function OnTick()
                    integer zero = 0;

                    Console.Write(1 / zero);
                end function
            end model
            """);

        var outcome = rules.Call("Rules", "OnTick");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Fault?.Kind, Is.EqualTo(ScriptFaultKind.Threw));
            Assert.That(outcome.Fault!.File, Is.EqualTo("rules.cm"));
            Assert.That(outcome.Fault.Line, Is.GreaterThan(0));
        });
    }

    /// <summary>And the module keeps working afterwards, which is what "carry on serving" means.</summary>
    [Test]
    public void AModuleGoesOnWorkingAfterAHandlerFailed()
    {
        using var rules = Load("""
            shared model Rules
                integer seen = 0;

                public function OnTick()
                    Rules.seen = Rules.seen + 1;

                    if Rules.seen == 1
                        integer zero = 0;

                        Console.Write(1 / zero);
                    end if
                end function

                public integer function Seen()
                    yield Rules.seen;
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(rules.Call("Rules", "OnTick").Completed, Is.False);
            Assert.That(rules.Call("Rules", "OnTick").Completed, Is.True);
            Assert.That(rules.Call("Rules", "Seen").Value, Is.EqualTo(2L));
        });
    }

    /// <summary>
    /// 🔴 The depth the language allows costs real stack, and the thread this runs on is sized for it.
    /// On an ordinary thread the same recursion overruns the stack instead, which is not catchable and
    /// takes the process down with every player on it — so this is the test that says the thread is
    /// doing its job rather than merely existing.
    /// </summary>
    [Test]
    public void TheLanguagesFullDepthFitsOnTheThreadItRunsOn()
    {
        using var rules = Load("""
            shared model Rules
                public integer function Down(integer n)
                    if n <= 0
                        yield 0;
                    end if

                    yield 1 + Rules.Down(n - 1);
                end function
            end model
            """, ScriptLimits.None);

        var outcome = rules.Call("Rules", "Down", 400L);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, outcome.Fault?.ToString());
            Assert.That(outcome.Value, Is.EqualTo(400L));
        });
    }
}
