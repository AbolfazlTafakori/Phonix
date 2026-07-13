using Phonix.Api.Models;

namespace Phonix.Api.Dtos;

// A single page of a larger list plus the totals the UI needs to render pagination controls.
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;

    public static PagedResult<T> From(IReadOnlyList<T> all, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<T>(items, all.Count, page, pageSize);
    }
}

public record CategoryDto(
    int Id, string Name, string Slug, string Icon, string Description, bool IsActive, int SortOrder, int ProductCount);

public record CategoryInput(
    string Name, string Slug, string Icon, string? Description, bool IsActive, int SortOrder);

public record ProductDto(
    int Id, string Name, int CategoryId, string CategoryName, long Price, int DiscountPercent,
    long FinalPrice, long Stock, bool IsActive, bool Featured, string Image, string Logo, List<string> Gallery, string Sku, string Description,
    string Warning, int RequiredLevel, double PriceUsd, List<ProductFeature> Features, List<ProductFaq> Faq, List<ProductPlan> Plans, string DeliveryTemplate,
    string ListImage);

public record ProductInput(
    string Name, int CategoryId, long Price, int DiscountPercent, long Stock, bool IsActive,
    bool Featured, string Image, string? Logo, List<string>? Gallery, string Sku, string Description, string? Warning, int? RequiredLevel,
    List<ProductFeature>? Features, List<ProductPlan>? Plans, string? DeliveryTemplate, double? PriceUsd, List<ProductFaq>? Faq, string? ListImage);

public record PriceInput(long Price, int DiscountPercent, double? PriceUsd);

public record UserDto(
    int Id, string Code, string Name, string Username, string Email, string Phone, string Avatar, UserRole Role, int Orders,
    long TotalSpent, long Wallet, bool Verified, int VerificationLevel, bool EmailVerified, bool Blocked, string JoinedAt, string? Note);

public record AuthResultDto(string Token, UserDto User);

public record UserUpdateInput(
    string? Name, string? Email, string? Phone, UserRole? Role, bool? Verified, bool? Blocked, string? Note,
    int? VerificationLevel);

public record WalletInput(long Amount, string? Reason);

public record PlanDto(
    int Id, string Label, int Months, long Price, int DiscountPercent, long FinalPrice, double PriceUsd);

public record PlanInput(string Label, int Months, long Price, int DiscountPercent, double? PriceUsd);

public static class Mapping
{
    public static CategoryDto ToDto(this Category c, int productCount) =>
        new(c.Id, c.Name, c.Slug, c.Icon, c.Description, c.IsActive, c.SortOrder, productCount);

    public static ProductDto ToDto(this Product p, string categoryName) =>
        new(p.Id, p.Name, p.CategoryId, categoryName, p.Price, p.DiscountPercent, p.FinalPrice,
            p.Stock, p.IsActive, p.Featured, p.Image, p.Logo, p.Gallery, p.Sku, p.Description, p.Warning, p.RequiredLevel, p.PriceUsd, p.Features, p.Faq, p.Plans,
            p.DeliveryTemplate, p.ListImage);

    public static UserDto ToDto(this AppUser u) =>
        new(u.Id, u.Code, u.Name, u.Username, u.Email, u.Phone, u.Avatar, u.Role, u.Orders, u.TotalSpent, u.Wallet,
            u.Verified, u.VerificationLevel, u.EmailVerified, u.Blocked, u.JoinedAt, u.Note);

    public static PlanDto ToDto(this SubscriptionPlan p) =>
        new(p.Id, p.Label, p.Months, p.Price, p.DiscountPercent, p.FinalPrice, p.PriceUsd);
}
