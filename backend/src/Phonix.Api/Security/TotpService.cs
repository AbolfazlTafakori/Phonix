using System.Security.Cryptography;
using System.Text;

namespace Phonix.Api.Security;

// RFC 6238 time-based one-time passwords (the Google Authenticator scheme): HMAC-SHA1, 30-second steps,
// 6 digits. Implemented in-house to avoid a third-party dependency for what is a small, stable algorithm.
public static class TotpService
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const int SecretBytes = 20;

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    // The otpauth:// URI an authenticator app consumes (rendered as a QR code on the client).
    public static string BuildOtpAuthUri(string issuer, string account, string secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={iss}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    // Validates a code against the secret, accepting the adjacent steps so a small clock skew or a code
    // entered right on a boundary still passes. Comparison is constant-time per candidate.
    public static bool Verify(string secret, string code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        var normalized = new string(code.Where(char.IsDigit).ToArray());
        if (normalized.Length != Digits) return false;

        byte[] key;
        try { key = Base32Decode(secret); }
        catch { return false; }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / PeriodSeconds;
        for (var offset = -window; offset <= window; offset++)
        {
            if (FixedTimeEquals(Compute(key, step + offset), normalized)) return true;
        }
        return false;
    }

    private static string Compute(byte[] key, long counter)
    {
        var data = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(data);
        var hash = HMACSHA1.HashData(key, data);

        var bucket = hash[^1] & 0x0f;
        var binary = ((hash[bucket] & 0x7f) << 24)
                   | ((hash[bucket + 1] & 0xff) << 16)
                   | ((hash[bucket + 2] & 0xff) << 8)
                   | (hash[bucket + 3] & 0xff);
        return (binary % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        var clean = input.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xff));
            }
        }
        return output.ToArray();
    }
}
