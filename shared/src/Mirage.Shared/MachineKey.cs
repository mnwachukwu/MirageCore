using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Mirage.Shared;

/// <summary>
/// The value a hardware ban is keyed on: one opaque hash identifying the machine a client runs on.
///
/// <para>Computed on the CLIENT and sent with the first packet, which is the whole reason this is a
/// last-resort tool rather than a security boundary — the machine being identified is the one doing the
/// identifying, and this engine ships its client's source. It raises the cost of evasion from a network
/// hop to a modified build; it does not close the door, and nothing downstream should be written as
/// though it does.</para>
///
/// <para><b>Cross-platform by construction.</b> Rather than enumerating hardware — which has no parity
/// story, since Linux restricts the DMI serials to root and would leave a Linux player materially harder
/// to ban than a Windows one — this reads the identifier each OS already maintains for itself:</para>
/// <list type="bullet">
///   <item><description>Windows — <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>, written at OS install.</description></item>
///   <item><description>Linux — <c>/etc/machine-id</c>, falling back to <c>/var/lib/dbus/machine-id</c>.</description></item>
///   <item><description>macOS — <c>IOPlatformUUID</c>, read through <c>ioreg</c>.</description></item>
/// </list>
///
/// <para>Nothing else feeds it. A per-installation token was considered and dropped: a file the client
/// writes is deleted by reinstalling the game, which is a far cheaper evasion than anything this is meant
/// to cost. What is left is the identifier the OS owns, which survives wiping the game entirely.</para>
///
/// <para>Two consequences worth knowing rather than discovering. The Windows and Linux ids are
/// INSTALL-scoped, so reinstalling the OS mints a new one, while macOS's is bound to the logic board and
/// survives a reinstall — the same evasion costs a macOS user more. And machines cloned from one disk
/// image SHARE an id, so banning one bans the whole image: intended against somebody spinning up VMs,
/// and the reason the default mode reports rather than refuses.</para>
///
/// <para>The raw identifier NEVER leaves the machine. systemd documents <c>/etc/machine-id</c> as
/// confidential and asks that only application-specific derivations be exposed; hashing here honors that,
/// and the server salts what it receives a second time so a stored key cannot be carried between servers.</para>
/// </summary>
public static class MachineKey
{
    /// <summary>Domain separator, so this hash is specific to this use and could not collide with any
    /// other derivation of the same identifier. Bump the suffix to invalidate every key in the wild.</summary>
    private const string Purpose = "mirage-machine-id-v1";

    /// <summary>How long <c>ioreg</c> gets before the macOS branch gives up. A subprocess is the only
    /// way to reach IOPlatformUUID, and a login must not hang behind one.</summary>
    private const int MacProbeMs = 4000;

    private static string? _cached;

    /// <summary>
    /// This machine's key, as lowercase hex, or an empty string if the OS would not identify itself.
    /// Cached — the value cannot change while the process runs, and the macOS branch spawns a subprocess.
    /// </summary>
    public static string Compute()
    {
        if (_cached is not null) return _cached;
        string id = ReadOsMachineId();
        // No identifier means NO KEY, never a hash of an empty string — that would be the same value on
        // every such machine, so banning one would ban all of them at once.
        return _cached = id.Length == 0
            ? ""
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Purpose}\n{id}")));
    }

    private static string ReadOsMachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindowsMachineGuid();
            if (OperatingSystem.IsMacOS()) return ReadMacPlatformUuid();
            return ReadUnixMachineId();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException or InvalidOperationException)
        {
            // A machine that will not identify itself still logs in; it just cannot be machine-banned.
            return "";
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ReadWindowsMachineGuid()
    {
        // The 64-bit view explicitly: under WOW64 a 32-bit process is redirected into Wow6432Node, where
        // this value does not exist, and the key would silently come back empty on exactly one build flavor.
        using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(
            Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string is { Length: > 0 } guid ? guid.Trim() : "";
    }

    private static string ReadUnixMachineId()
    {
        // /etc/machine-id is the systemd-era location; the dbus copy predates it and is still present on
        // non-systemd distributions. Both are world-readable, unlike the DMI serials.
        foreach (string path in (string[])["/etc/machine-id", "/var/lib/dbus/machine-id"])
        {
            if (!File.Exists(path)) continue;
            string id = File.ReadAllText(path).Trim();
            if (id.Length > 0) return id;
        }
        return "";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static string ReadMacPlatformUuid()
    {
        using var proc = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (proc is null) return "";

        string output = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(MacProbeMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
            return "";
        }

        // The line reads:  "IOPlatformUUID" = "0A1B2C3D-..."  — take what is between the last pair of quotes.
        foreach (string line in output.Split('\n'))
        {
            if (!line.Contains("IOPlatformUUID", StringComparison.Ordinal)) continue;
            int close = line.LastIndexOf('"');
            if (close <= 0) continue;
            int open = line.LastIndexOf('"', close - 1);
            if (open < 0) continue;
            string uuid = line[(open + 1)..close].Trim();
            if (uuid.Length > 0) return uuid;
        }
        return "";
    }
}
