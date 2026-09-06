using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mirage.Server.Core.Configuration;

namespace Mirage.Server.Host.Net;

/// <summary>The certificate both listeners present, persisted so clients can pin its fingerprint.</summary>
public static class SelfSignedCertificate
{
    /// <summary>Subject name, and the host name clients authenticate against.</summary>
    public const string SubjectName = "mirage-server";

    public const string FileName = "server-identity.pfx";

    /// <summary>The identity's home: the server's per-user data dir, NOT the folder the exe runs from.
    /// A Velopack update replaces that folder wholesale, so an identity kept there lasts exactly one
    /// version — see <see cref="ServerPaths"/>.</summary>
    public static string DefaultPath => ServerPaths.Data(FileName);

    /// <summary>Loads the identity from <paramref name="path"/>, creating and saving one if absent.</summary>
    /// <exception cref="InvalidOperationException">The file exists but cannot be read. It is left in
    /// place: replacing it would change the server's identity and trip every client that pinned it.</exception>
    /// <exception cref="IOException">The identity could not be written.</exception>
    public static X509Certificate2 LoadOrCreate(string? path = null)
    {
        path ??= DefaultPath;

        if (File.Exists(path))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The server identity at '{path}' could not be read. Delete it to issue a new one — " +
                    "every client that has connected before will then report a changed certificate.", ex);
            }
        }

        // From the fresh request, not a re-export: a loaded certificate holds its key non-exportable.
        byte[] pfx = NewPfxBytes();
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, pfx);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Could not write the server identity to '{path}'. The server will still run, but its " +
                "certificate changes on every restart and clients cannot pin it.", ex);
        }
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    /// <summary>A fresh identity, not persisted.</summary>
    public static X509Certificate2 Create() => X509CertificateLoader.LoadPkcs12(NewPfxBytes(), password: null);

    /// <summary>SHA-256 of the certificate's raw bytes, lower-case hex.</summary>
    public static string Fingerprint(X509Certificate2 cert) =>
        Convert.ToHexStringLower(SHA256.HashData(cert.RawData));

    /// <summary>The fingerprint in upper-case byte pairs, for reading off a screen.</summary>
    public static string FingerprintForDisplay(X509Certificate2 cert)
    {
        string hex = Fingerprint(cert).ToUpperInvariant();
        return string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    private static byte[] NewPfxBytes()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={SubjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return temp.Export(X509ContentType.Pfx);
    }
}
