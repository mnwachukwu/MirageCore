using System.Reflection;
using System.Text;
using Mirage.Scripting;
using Mirage.Server.Host.Scripting;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// The reference a script author reads, generated from the catalog they are reading about.
///
/// <para>🔴 <b>Nothing else could keep it honest.</b> A page listing these members by hand is a page that
/// stops matching the first time somebody adds one in a hurry — and the failure is silent in the worst
/// way, because the reader finds out by writing something the engine does not offer and being told by a
/// compiler they have never met. So the page is rendered from the live catalog, and this rewrites it when
/// it has drifted and fails so the new one gets committed.</para>
///
/// <para>What it cannot generate is the sentence beside each member. That is written at the member's own
/// declaration, next to the binding that performs it, which is the only place it can be kept true.</para>
/// </summary>
[TestFixture]
public class ScriptApiReferenceTests
{
    private static string RepoRoot => typeof(ScriptApiReferenceTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "RepoRoot").Value!;

    private static string Reference => Path.Combine(RepoRoot, "docs", "scripting-api.md");

    [Test]
    public void TheReferenceMatchesWhatTheEngineOffers()
    {
        string rendered = Render(new ScriptedWorldModule("no-such-world").Catalog());

        Assert.That(File.Exists(Reference), Is.True,
            $"there is no reference at {Reference} — if it moved, teach this test where");

        string current = File.ReadAllText(Reference).ReplaceLineEndings("\n");

        if (current == rendered) return;

        File.WriteAllText(Reference, rendered);

        Assert.Fail(
            "docs/scripting-api.md did not match what the engine registers, so it has been rewritten. "
            + "Read the change and commit it.");
    }

    /// <summary>🔴 A member with nothing said about it is a row in the reference that teaches nothing.</summary>
    [Test]
    public void EveryRegisteredMemberSaysWhatItDoes()
    {
        var silent = new ScriptedWorldModule("no-such-world").Catalog().Types
            .SelectMany(t => t.Members, (t, m) => (Where: $"{t.Name}.{m.Name}", m.Note))
            .Where(m => m.Note.Length == 0)
            .Select(m => m.Where);

        Assert.That(silent, Is.Empty,
            "a member nobody described is one a script author meets for the first time in a diagnostic");
    }

    private static string Render(ScriptCatalog catalog)
    {
        var md = new StringBuilder();

        md.Append("""
            # The scripting API

            What a world's own rules may name, and what each thing does.

            > Generated from the catalog the server registers, by `ScriptApiReferenceTests`. Editing this
            > file by hand is undone by the next test run — the sentences live beside the bindings in
            > `ScriptedWorldModule.Catalog`.

            A module is a folder of `.cm` files under `<world>/scripts/`, holding a shared model called
            `Rules`. Every handler on it is optional and every one has to be `public`, because a function
            only the engine calls is one nothing in the module calls.

            ## The handlers

            | Written | Called |
            |---|---|

            """.ReplaceLineEndings("\n"));

        foreach (ScriptHandler handler in ScriptedWorldModule.Handlers)
        {
            md.Append("| `public ").Append(handler.Signature).Append("` | ")
              .Append(handler.When).Append(" |\n");
        }

        md.Append("\n## The types\n");

        foreach (ScriptTypeInfo type in catalog.Types)
        {
            md.Append('\n').Append("### ").Append(type.Name).Append('\n').Append('\n');

            md.Append(type.Shared
                ? "Reached through its own name; there are no values of it.\n\n"
                : "Held as a value and never made by a script: the engine hands one over.\n\n");

            md.Append("| Written | Yields | What it does |\n|---|---|---|\n");

            foreach (ScriptMemberInfo member in type.Members)
            {
                md.Append("| `").Append(member.Signature).Append("` | ")
                  .Append(member.Yields.IsNothing ? "—" : $"`{member.Yields}`")
                  .Append(" | ").Append(member.Note.Length > 0 ? member.Note : "—").Append(" |\n");
            }
        }

        md.Append("""

            ## What a module may not reach

            The language's own way out of a program — the filesystem, the machine clock — is refused when
            the module is read, not when it runs. A world folder is something one person hands to another,
            and a module that could open a file could read the accounts beside it.

            Everything else a script can name is on this page. There is no way to reach a type the engine
            did not register.
            """.ReplaceLineEndings("\n"));

        return md.ToString().TrimEnd() + "\n";
    }
}
