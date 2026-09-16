namespace Mirage.Shared.Extensibility;

/// <summary>
/// Something a game adds to the server's own console.
///
/// <para><b>The operator's console, and nothing a player can reach.</b> Whoever types here already has
/// the machine, so there is no rank to check and no abuse to guard against — which is exactly why this
/// exists and why there is no matching seam for a player's chat. A command a player could type would
/// need a rank the engine enforces, a place in the help, and a client that knows the word is a command
/// rather than something to say out loud.</para>
///
/// <para>What it is for is the work a world does on a schedule nobody can sit and watch: a weekly
/// settlement, a season that runs a quarter of a year, a night that falls once a week. The engine has
/// no idea those exist, so it cannot offer a command to force one.</para>
/// </summary>
public interface IConsoleHandler
{
    /// <summary>Answers a console command Core does not know itself.
    ///
    /// <para>Return what the console should print, or null for a command this game does not know
    /// either — the console then says it is unknown, the same as it would with no game loaded. Returning
    /// blank text counts as handling it and prints nothing.</para>
    ///
    /// <para>⚠ Called ON the game thread, so it may read and write the world directly. It is also
    /// called while the loop is stopped for it, so a handler that takes its time stops the world for
    /// that long.</para></summary>
    /// <param name="command">The word typed, with its leading slash — <c>/startwar</c>.</param>
    /// <param name="rest">Everything after it, trimmed, or blank where nothing followed.</param>
    string? Console(string command, string rest);
}
