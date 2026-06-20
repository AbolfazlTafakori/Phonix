using Phonix.Api.Models;

namespace Phonix.Api.Dtos;

public record CategoryDto(
    int Id, string Name, string Slug, string Icon, bool IsActive, int SortOrder, int ProductCount);

public record CategoryInput(
    string Name, string Slug, string Icon, bool IsActive, int SortOrder);

public record ProductDto(
    int Id, string Name, int CategoryId, string CategoryName, long Price, int DiscountPercent,
    long FinalPrice, long Stock, bool IsActive, bool Featured, string Image, string Sku, string Description,
    string Warning, List<ProductFeature> Features, List<ProductPlan> Plans);

public record ProductInput(
    string Name, int CategoryId, long Price, int DiscountPercent, long Stock, bool IsActive,
    bool Featured, string Image, string Sku, string Description, string? Warning,
    List<ProductFeature>? Features, List<ProductPlan>? Plans);

public record PriceInput(long Price, int DiscountPercent);

public record UserDto(
    int Id, string Code, string Name, string Username, string Email, string Phone, UserRole Role, int Orders,
    long TotalSpent, long Wallet, bool Verified, bool EmailVerified, bool Blocked, string JoinedAt, string? Note);

public record AuthResultDto(string Token, UserDto User);

public record UserUpdateInput(
    string? Name, string? Email, string? Phone, UserRole? Role, bool? Verified, bool? Blocked, string? Note);

public record WalletInput(long Amount, string? Reason);

public record PlanDto(
    int Id, string Label, int Months, long Price, int DiscountPercent, long FinalPrice);

public record PlanInput(string Label, int Months, long Price, int DiscountPercent);

public static class Mapping
{
    public static CategoryDto ToDto(this Category c, int productCount) =>
        new(c.Id, c.Name, c.Slug, c.Icon, c.IsActive, c.SortOrder, productCount);

    public static ProductDto ToDto(this Product p, string categoryName) =>
        new(p.Id, p.Name, p.CategoryId, categoryName, p.Price, p.DiscountPercent, p.FinalPrice,
            p.Stock, p.IsActive, p.Featured, p.Image, p.Sku, p.Description, p.Warning, p.Features, p.Plans);

    public static UserDto ToDto(this AppUser u) =>
        new(u.Id, u.Code, u.Name, u.Username, u.Email, u.Phone, u.Role, u.Orders, u.TotalSpent, u.Wallet,
            u.Verified, u.EmailVerified, u.Blocked, u.JoinedAt, u.Note);

    public static PlanDto ToDto(this SubscriptionPlan p) =>
        new(p.Id, p.Label, p.Months, p.Price, p.DiscountPercent, p.FinalPrice);
}
