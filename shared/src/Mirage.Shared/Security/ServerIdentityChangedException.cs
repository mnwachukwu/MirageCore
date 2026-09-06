namespace Mirage.Shared.Security;

/// <summary>Thrown when a server presents a different certificate than the one on record.</summary>
public sealed class ServerIdentityChangedException(string host, int port, string expected, string actual)
    : Exception($"The server at {host}:{port} presented a different certificate than last time.")
{
    public string Host { get; } = host;
    public int Port { get; } = port;

    /// <summary>The fingerprint on record.</summary>
    public string Expected { get; } = expected;

    /// <summary>The fingerprint just offered.</summary>
    public string Actual { get; } = actual;
}
