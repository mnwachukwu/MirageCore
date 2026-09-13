using Compass.Compiler.Semantics;

namespace Mirage.Scripting;

/// <summary>
/// What a game module may not reach, and the refusal that holds it to that.
///
/// <para>A world folder is something one person hands to another, and its scripts run inside the server
/// process, on the server's thread, as the server's user. A module that could open a file could read the
/// accounts beside it; one that could read the clock could behave differently on the machine it is
/// audited on. Neither is a thing a game needs, and the .NET runtime offers no way to take either away
/// from code already running — so the answer is to refuse the module before it loads.</para>
///
/// <para>🔴 <b>The rule is derived from the language, never transcribed from it.</b> Compass classifies
/// each of its own members by what it touches and adds them up over a checked program; this refuses
/// anything that adds up to more than nothing. Written the other way — a list here of the members that
/// reach outside — the check would keep passing while it stopped meaning anything, the moment the
/// language grew one more. That failure has no symptom: the guard is green, the module loads, and the
/// thing it was guarding against is simply allowed.</para>
///
/// <para>This is one half of the sandbox. The other is <see cref="ScriptCatalog"/>: a script reaches
/// exactly what the engine registered, which is why what a registered member touches is not counted
/// here — the engine knows what its own bindings do.</para>
///
/// <para>⚠ <b>Where this stops.</b> It defends a trusted server from a careless or opportunistic script.
/// A genuinely hostile one is an operating-system problem — a separate process with restrictions — and
/// that costs the in-process embedding and the cheap handles this layer exists to provide.</para>
/// </summary>
public static class ScriptSandbox
{
    /// <summary>
    /// Everything the language says one of its members can touch beyond a program, named as Compass
    /// names it. A module using any of them is refused.
    /// </summary>
    public static IReadOnlyList<string> Refused { get; } =
        [.. Enum.GetValues<Reaches>()
                .Where(r => r != Reaches.Nothing)
                .Select(r => r.ToString())
                .OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>The problem to report for each thing a checked module reaches, or none.</summary>
    internal static IEnumerable<ScriptProblem> Inspect(SemanticModel model, string name)
    {
        Reaches reaches = model.Reaches;
        if (reaches == Reaches.Nothing) yield break;

        // Named one at a time, so a module doing two of these is told about both rather than about the
        // first. The name comes from the language's own enum, so a capability it grows later reports
        // itself without anything here being edited.
        foreach (Reaches one in Enum.GetValues<Reaches>())
        {
            if (one == Reaches.Nothing || !reaches.HasFlag(one)) continue;

            yield return new ScriptProblem(
                "MS0003",
                ScriptSeverity.Error,
                $"This module uses part of the language that reaches {one} beyond the program. A game "
                + "module may reach only what the engine registers, so that a world somebody hands you "
                + "cannot touch the machine it is run on.",
                name,
                0,
                0);
        }
    }
}
