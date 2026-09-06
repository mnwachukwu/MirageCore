using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Mirage.Shared.Security;

/// <summary>
/// Certificate check for one connection attempt. Build it, pass <see cref="Validate"/> to
/// <see cref="SslStream"/>, then call <see cref="Commit"/> once the handshake succeeds.
/// </summary>
public sealed class PinnedServer(ServerPins pins, string host, int port)
{
    private string _offered = "";

    public ServerTrust Trust { get; private set; } = ServerTrust.FirstContact;

    /// <summary>Callback for <see cref="SslStream"/>. A changed certificate fails the handshake,
    /// before any data moves.</summary>
    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        _offered = ServerPins.FingerprintOf(certificate.GetRawCertData());
        Trust = pins.Check(host, port, _offered);
        return Trust != ServerTrust.Changed;
    }

    /// <summary>Records the certificate when this was the first connection to the server.</summary>
    public void Commit()
    {
        if (Trust == ServerTrust.FirstContact) pins.Remember(host, port, _offered);
    }

    /// <summary>Restates a handshake failure as <see cref="ServerIdentityChangedException"/> when the
    /// cause was a changed certificate.</summary>
    public Exception Translate(AuthenticationException handshakeFailure) =>
        Trust == ServerTrust.Changed
            ? new ServerIdentityChangedException(host, port, pins.PinnedFingerprint(host, port) ?? "", _offered)
            : handshakeFailure;
}
