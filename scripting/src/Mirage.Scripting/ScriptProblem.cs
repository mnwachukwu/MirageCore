namespace Mirage.Scripting;

/// <summary>How badly a script is wrong.</summary>
public enum ScriptSeverity : byte
{
    /// <summary>Worth saying, and the script still runs.</summary>
    Warning = 0,

    /// <summary>The script does not run. Nothing partially loads: a game whose rules half-arrived is a
    /// game nobody can reason about.</summary>
    Error = 1,
}

/// <summary>
/// One thing wrong with a script, as data.
///
/// <para>🔴 <b>A problem is returned, never printed.</b> The compiler that produces these is a command-line
/// tool by origin and writes to a console; a server has no console worth writing to, has an operator
/// reading a log somewhere else, and may be compiling a script on behalf of one connection out of twenty.
/// Handing back a list lets the caller decide where it goes and keeps a script's mistakes out of the
/// server's own output.</para>
/// </summary>
public sealed record ScriptProblem(
    string Id,
    ScriptSeverity Severity,
    string Message,
    string File,
    int Line,
    int Column)
{
    /// <summary>One line, in the shape every compiler writes: where, then how bad, then what.</summary>
    public override string ToString() =>
        $"{File}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Id}: {Message}";
}
