namespace Mirage.Server.Host;

/// <summary>
/// The flags the server takes on its command line.
///
/// <para>Both exist because something other than a person is driving the process. The management shell
/// asks a child it spawned for status snapshots on stdout; the load benchmark runs a SECOND server from
/// the same install, pointed at a scratch world on a free port so it can measure the machine without
/// touching the operator's. A server started from a terminal passes neither and behaves as it always
/// has.</para>
/// </summary>
internal static class StartupArgs
{
    private const string ConfigFlag = "--config";
    private const string StatusFlag = "--status-events";

    /// <summary>An alternate serverconfig.json, or null for the one beside the executable.</summary>
    public static string? ConfigPath(string[] args) => Value(args, ConfigFlag);

    /// <summary>Whether status snapshots go to stdout, and how often the backstop fires.
    ///
    /// <para><c>--status-events</c> on its own takes the default cadence. <c>--status-events=1</c> asks
    /// for one reading a second, which is what a ramping benchmark needs: its steps are far shorter than
    /// <see cref="Management.StatusBroadcaster.Backstop"/>, so at the default it would sample once every
    /// several steps and miss the one that broke.</para></summary>
    public static bool StatusEvents(string[] args, out TimeSpan cadence)
    {
        cadence = Management.StatusBroadcaster.Backstop;
        bool present = args.Any(a =>
            a == StatusFlag || a.StartsWith(StatusFlag + "=", StringComparison.Ordinal));
        if (present
            && double.TryParse(Value(args, StatusFlag), System.Globalization.CultureInfo.InvariantCulture, out double seconds)
            && seconds > 0)
        {
            cadence = TimeSpan.FromSeconds(seconds);
        }
        return present;
    }

    /// <summary>Reads <c>--name=value</c> and <c>--name value</c>. Both, because the first is what a
    /// process launcher writes and the second is what a person types.</summary>
    private static string? Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
            if (args[i] == name && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                return args[i + 1];
        }
        return null;
    }
}
