using Phonix.Api.Models;

namespace Phonix.Api.Data;

// How a discount code prices a basket — the one rule both the checkout preview and order placement use, so
// the amount a customer is shown is the amount the order takes off.
public static class DiscountRules
{
    public static DiscountResult Resolve(DiscountCode? dc, IReadOnlyList<DiscountLine> lines, DateTime nowUtc)
    {
        if (dc is null || !dc.IsActive) return new DiscountResult(null, 0, "کد تخفیف نامعتبر است.");
        if (dc.ExpiresAt is DateTime exp && nowUtc > exp) return new DiscountResult(null, 0, "این کد تخفیف منقضی شده است.");
        if (dc.UsageLimit > 0 && dc.UsedCount >= dc.UsageLimit) return new DiscountResult(null, 0, "ظرفیت این کد تخفیف به پایان رسیده است.");

        // A restricted code only ever sees its own products' lines; everything below is priced on those.
        var restricted = dc.ProductIds is { Count: > 0 };
        var eligible = restricted ? lines.Where(l => dc.ProductIds.Contains(l.ProductId)).ToList() : lines.ToList();
        if (restricted && eligible.Count == 0)
            return new DiscountResult(null, 0, "این کد تخفیف برای محصولات سبد خرید شما قابل استفاده نیست.");
        var basis = eligible.Sum(l => Math.Max(0, l.LineTotal));

        if (basis < dc.MinOrder)
            return new DiscountResult(null, 0, restricted
                ? "مبلغ محصولات مشمول این کد تخفیف به حد لازم نرسیده است."
                : "مبلغ سفارش به حد لازم برای این کد نرسیده است.");

        long amount = dc.Type == DiscountType.Percent ? (long)Math.Round(basis * dc.Value / 100.0) : dc.Value;
        if (dc.Type == DiscountType.Percent && dc.MaxDiscount > 0) amount = Math.Min(amount, dc.MaxDiscount);
        amount = Math.Clamp(amount, 0, basis);
        return new DiscountResult(dc, amount, null);
    }
}
