using System.Security.Cryptography;

namespace Phonix.Api.Security;

/// <summary>
/// Salted PBKDF2 (SHA-256) password hashing. Stored format: {iterations}.{saltBase64}.{hashBase64}
/// </summary>
public static class PasswordHasher
{
    // Bumped from 100_000 towards current OWASP guidance for PBKDF2-HMAC-SHA256. The iteration count travels
    // with each stored hash (see the {iterations}.{salt}.{hash} format below), so this is backward-compatible
    // — existing hashes keep verifying at whatever count they were created with; only new hashes use this one.
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        // PBKDF2-HMAC uses the password as the HMAC key; a key longer than the hash's block size gets
        // re-hashed on every single iteration, so an attacker submitting a multi-megabyte "password" can force
        // real CPU cost per request — pre-authentication, since this runs before any account is confirmed to
        // exist. PasswordPolicy already caps new passwords at creation time; this is the matching guard for
        // the read (login) path, which never goes through PasswordPolicy at all. No legitimate password is
        // anywhere near this long, so nothing real is rejected by it.
        if (password.Length > PasswordPolicy.MaxLength) return false;

        var parts = stored.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        byte[] salt, key;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            key = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, key.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, key);
    }
}
