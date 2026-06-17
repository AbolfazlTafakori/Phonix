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
    public UserRole Role { get; set; } = UserRole.Customer;
    public int Orders { get; set; }
    public long TotalSpent { get; set; }
    public long Wallet { get; set; }
    public bool Verified { get; set; }
    public bool Blocked { get; set; }
    public string JoinedAt { get; set; } = "";
    public string? Note { get; set; }
}
