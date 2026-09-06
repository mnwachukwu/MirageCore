using System.Security.Cryptography;
using System.Text;

namespace Mirage.Shared.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing.
///
/// Stored format: 32-hex-char salt + 64-hex-char hash = 96 chars total.
/// The salt occupies the fixed-width prefix so it can always be extracted
/// without a delimiter.
/// </summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private const int SaltHexLen = SaltBytes * 2; // 32
    private const int StoredLength = (SaltBytes + HashBytes) * 2; // 96

    public static string Hash(string plaintext)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(plaintext, salt);
        return Convert.ToHexString(salt) + Convert.ToHexString(hash);
    }

    public static bool Verify(string plaintext, string stored)
    {
        if (stored.Length != StoredLength) return false;

        byte[] salt = Convert.FromHexString(stored[..SaltHexLen]);
        byte[] expected = Convert.FromHexString(stored[SaltHexLen..]);
        byte[] actual = Derive(plaintext, salt);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string plaintext, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plaintext),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
}
