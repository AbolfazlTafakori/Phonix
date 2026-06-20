namespace Phonix.Api.Security;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Returns an error message when the password is too weak, otherwise null.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"گذرواژه باید حداقل {MinLength} کاراکتر باشد.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "گذرواژه باید ترکیبی از حروف و اعداد باشد.";
        return null;
    }
}
