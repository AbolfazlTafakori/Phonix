namespace Phonix.Api.Security;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    // Generous enough that no realistic human passphrase is ever rejected, but bounds the PBKDF2 cost a
    // caller can force per request — without this, an attacker submitting a multi-megabyte password makes
    // Login/Register/ResetPassword re-hash that entire string 100,000 times before rejecting it, a cheap
    // pre-auth CPU-exhaustion lever. See PasswordHasher.Verify for the matching guard on the login path.
    public const int MaxLength = 256;

    /// <summary>Returns an error message when the password is too weak, otherwise null.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"گذرواژه باید حداقل {MinLength} کاراکتر باشد.";
        if (password.Length > MaxLength)
            return $"گذرواژه نمی‌تواند بیش از {MaxLength} کاراکتر باشد.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "گذرواژه باید ترکیبی از حروف و اعداد باشد.";
        return null;
    }
}
