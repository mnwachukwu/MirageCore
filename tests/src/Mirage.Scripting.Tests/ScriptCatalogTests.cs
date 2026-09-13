using Mirage.Scripting;
using NUnit.Framework;

namespace Mirage.Scripting.Tests;

/// <summary>
/// The engine's own types, as a script sees them.
///
/// <para>This is the half of scripting that makes it worth having: a module holds a <c>Player</c> and
/// asks it questions, rather than juggling integer handles and a bag of free functions. What the engine
/// registers is also the whole of what a script can reach through the engine, so a gap here is not a
/// missing convenience — it is the difference between a seam and a wall.</para>
/// </summary>
[TestFixture]
public class ScriptCatalogTests
{
    /// <summary>Stands in for whatever the engine would hand over. A script never sees inside one.</summary>
    private sealed class Person(string name)
    {
        public string Name { get; } = name;

        public List<string> Heard { get; } = [];
    }

    private static ScriptCatalog Catalog(Person? found = null) => ScriptCatalog.Declare(c =>
    {
        var player = c.Type("Player");
        var world = c.Shared("World");

        player
            .Value("Name", ScriptType.Text, (who, _) => ((Person)who!).Name)
            .Action("Message", [ScriptType.Text], (who, args) =>
            {
                ((Person)who!).Heard.Add(args.AsText(0));
                return null;
            });

        world
            .Function("PlayerNamed", player.AsType.OrNothing(), [ScriptType.Text],
                (_, args) => found is not null && found.Name == args.AsText(0) ? found : null)
            .Function("Doubled", ScriptType.Integer, [ScriptType.Integer], (_, args) => args.AsInteger(0) * 2);
    });

    private static ScriptOutcome Call(
        string body, ScriptCatalog catalog, params object?[] arguments)
    {
        string source = $$"""
            shared model Rules
                public function Go({{(arguments.Length > 0 ? "Player who" : string.Empty)}})
            {{body}}
                end function
            end model
            """;

        var (script, problems) = ScriptCompiler.CompileModule(source, "rules.cm", catalog);
        Assert.That(script, Is.Not.Null, "it should have checked: " + string.Join("; ", problems));

        using var loaded = LoadedScript.Load(script!, ScriptLimits.None);
        return loaded.Call("Rules", "Go", arguments);
    }

    [Test]
    public void AScriptCallsAMemberTheEngineRegistered()
    {
        var ada = new Person("Ada");
        var outcome = Call("        who.Message(\"hello, \" + who.Name);", Catalog(), ada);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, outcome.Fault?.ToString());
            Assert.That(ada.Heard, Is.EqualTo(new[] { "hello, Ada" }));
        });
    }

    /// <summary>
    /// 🔴 A member's signature naming a type the same catalog declares is the case the two-pass build
    /// exists for. Built any other way the catalog holds two symbols for one name, a <c>Player</c> from
    /// <c>World.PlayerNamed</c> does not fit a <c>Player</c> variable, and the compiler reports a type
    /// error on code that is plainly correct.
    /// </summary>
    [Test]
    public void AMemberYieldingARegisteredTypeFitsAVariableOfIt()
    {
        var ada = new Person("Ada");
        var outcome = Call("""
                    Player? maybe = World.PlayerNamed("Ada");

                    if maybe.HasValue()
                        Player who = maybe.Value();

                        who.Message("found " + who.Name);
                    end if
            """, Catalog(ada));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, outcome.Fault?.ToString());
            Assert.That(ada.Heard, Is.EqualTo(new[] { "found Ada" }));
        });
    }

    /// <summary>Absence is a type rather than a value a script has to remember to check.</summary>
    [Test]
    public void AMemberThatFindsNothingHandsBackAnAbsentOptional()
    {
        var outcome = Call("""
                    Console.Write(World.PlayerNamed("Nobody").HasValue());
            """, Catalog());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.True, outcome.Fault?.ToString());
            Assert.That(outcome.Output, Is.EqualTo("false"));
        });
    }

    [Test]
    public void WholeNumbersCrossInBothDirections()
    {
        var outcome = Call("        Console.Write(World.Doubled(21));", Catalog());

        Assert.That(outcome.Output, Is.EqualTo("42"));
    }

    /// <summary>
    /// 🔴 The half of the boundary the compiler cannot check. A call's arguments are type-checked before
    /// a binding ever sees them; what the binding hands BACK is whatever the engine's own code returned.
    /// A member saying it yields a whole number and returning an <c>int</c> rather than a <c>long</c> —
    /// or a <c>double</c> where the language means an exact decimal — is wrong in a way nothing else
    /// reports, and it surfaces later, somewhere else, as a script misbehaving.
    /// </summary>
    [Test]
    public void ABindingHandingBackTheWrongShape_IsReportedAgainstTheEngine()
    {
        var catalog = ScriptCatalog.Declare(c => c.Shared("World")
            // 42 is an int here, and the language's whole number is a long.
            .Function("Count", ScriptType.Integer, [], (_, _) => 42));

        var outcome = Call("        Console.Write(World.Count());", catalog);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Completed, Is.False, "the engine handed back a shape it did not declare");
            Assert.That(outcome.Fault!.Kind, Is.EqualTo(ScriptFaultKind.EngineFailed),
                "and the fault is the ENGINE's, not the script's — an operator sent looking through "
                + "somebody's module for this would never find it");
            Assert.That(outcome.Fault.Message, Does.Contain("World.Count"));
        });
    }

    /// <summary>The same guard, for the case a binding is likeliest to get wrong.</summary>
    [Test]
    public void ABindingHandingBackABinaryFloatWhereTheLanguageMeansAnExactDecimal_IsReported()
    {
        var catalog = ScriptCatalog.Declare(c => c.Shared("World")
            .Function("Share", ScriptType.Real, [], (_, _) => 0.1));

        var outcome = Call("        Console.Write(World.Share());", catalog);

        Assert.That(outcome.Fault?.Kind, Is.EqualTo(ScriptFaultKind.EngineFailed));
    }

    /// <summary>A script may not name what the engine did not register, which is the sandbox's other half.</summary>
    [Test]
    public void AScriptCannotNameATypeTheEngineDidNotRegister()
    {
        var (script, problems) = ScriptCompiler.CompileModule("""
            shared model Rules
                public function Go()
                    Secret.Read();
                end function
            end model
            """, "rules.cm", Catalog());

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.Null);
            Assert.That(problems, Is.Not.Empty);
        });
    }

    [Test]
    public void ATypeRegisteredTwice_IsRefusedAtDeclaration()
    {
        Assert.That(
            () => ScriptCatalog.Declare(c => { c.Type("Player"); c.Type("Player"); }),
            Throws.ArgumentException);
    }

    /// <summary>The names a script may write are the ones that were declared, in that order.</summary>
    [Test]
    public void ACatalogSaysWhatItOffers()
    {
        Assert.That(Catalog().TypeNames, Is.EqualTo(new[] { "Player", "World" }));
    }
}
