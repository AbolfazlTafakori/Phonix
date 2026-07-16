namespace Phonix.Api.Security;

public static class UsernamePolicy
{
    public const int MinLength = 5;

    /// <summary>Returns an error message when the username breaks the policy, otherwise null.</summary>
    /// <remarks>
    /// English letters and digits only: no Persian/Arabic letters, no spaces, and no symbols (#, (), _, - …).
    /// The check is on ASCII specifically — char.IsLetterOrDigit would happily accept "کاربر۱".
    /// </remarks>
    public static string? Validate(string? username)
    {
        var name = (username ?? "").Trim();
        if (name.Length < MinLength)
            return $"نام کاربری باید حداقل {MinLength} کاراکتر باشد.";
        if (!name.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
            return "نام کاربری فقط می‌تواند شامل حروف انگلیسی و اعداد باشد.";
        return null;
    }
}
