namespace Phonix.Api.Models;

public enum UserRole
{
    Customer,
    Support,
    Admin
}

public class AppUser
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Avatar { get; set; } = ""; // public URL of the user's uploaded profile picture; empty = render the initial
    public UserRole Role { get; set; } = UserRole.Customer;
    public int Orders { get; set; }
    public long TotalSpent { get; set; }
    public long Wallet { get; set; }
    public bool Verified { get; set; }
    // identity tier: 0 = just registered (can't purchase), 1 = bank card approved, 2 = national-ID approved.
    // Kept in sync so Verified == (VerificationLevel >= 2). Upgrades are permanent.
    public int VerificationLevel { get; set; }
    public bool EmailVerified { get; set; }
    public bool Blocked { get; set; }
    public string JoinedAt { get; set; } = "";
    public string? Note { get; set; }
    public int? ReferredBy { get; set; }
    // Nonce embedded in every issued (stateless) session token. Rotating it invalidates all of this
    // user's outstanding cookies at once — e.g. after a password change. Empty is a valid initial value.
    public string SecurityStamp { get; set; } = "";
    // TOTP two-factor authentication. The secret is provisioned during setup and only becomes active once
    // the owner confirms a code (TwoFactorEnabled). Staff accounts are then challenged for a code on login.
    public bool TwoFactorEnabled { get; set; }
    public string TwoFactorSecret { get; set; } = "";
    // Section keys a limited (Support) staff member may access in the admin panel. Ignored for Admin, who
    // always has full access. Empty = a Support account that can only see the dashboard.
    public List<string> Permissions { get; set; } = new();
}

public class ReferralEarning
{
    public int ReferrerId { get; set; }
    public string ReferredName { get; set; } = "";
    public string OrderCode { get; set; } = "";
    public long OrderAmount { get; set; }
    public long Commission { get; set; }
    public string Date { get; set; } = "";
}
