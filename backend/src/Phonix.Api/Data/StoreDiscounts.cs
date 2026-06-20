using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record DiscountResult(DiscountCode? Code, long Amount, string? Error);

public partial class StoreData
{
    private readonly List<DiscountCode> _discountCodes = new();
    private int _discountSeq;

    public IReadOnlyList<DiscountCode> GetDiscountCodes()
    {
        lock (_gate) return _discountCodes.OrderByDescending(d => d.Id).ToList();
    }

    public DiscountCode AddDiscountCode(DiscountCode code)
    {
        lock (_gate)
        {
            code.Id = ++_discountSeq;
            code.UsedCount = 0;
            _discountCodes.Add(code);
            return code;
        }
    }

    public bool UpdateDiscountCode(DiscountCode code)
    {
        lock (_gate)
        {
            var existing = _discountCodes.FirstOrDefault(d => d.Id == code.Id);
            if (existing is null) return false;
            existing.Code = code.Code;
            existing.Type = code.Type;
            existing.Value = code.Value;
            existing.MinOrder = code.MinOrder;
            existing.MaxDiscount = code.MaxDiscount;
            existing.UsageLimit = code.UsageLimit;
            existing.IsActive = code.IsActive;
            existing.ExpiresAt = code.ExpiresAt;
            return true;
        }
    }

    public bool DeleteDiscountCode(int id)
    {
        lock (_gate)
        {
            var existing = _discountCodes.FirstOrDefault(d => d.Id == id);
            if (existing is null) return false;
            _discountCodes.Remove(existing);
            return true;
        }
    }

    // validates a code against a subtotal and returns the discount amount (without consuming it).
    public DiscountResult ResolveDiscount(string? code, long subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return new DiscountResult(null, 0, null);
        lock (_gate)
        {
            var dc = _discountCodes.FirstOrDefault(d => string.Equals(d.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (dc is null || !dc.IsActive) return new DiscountResult(null, 0, "کد تخفیف نامعتبر است.");
            if (dc.ExpiresAt is DateTime exp && DateTime.UtcNow > exp) return new DiscountResult(null, 0, "این کد تخفیف منقضی شده است.");
            if (dc.UsageLimit > 0 && dc.UsedCount >= dc.UsageLimit) return new DiscountResult(null, 0, "ظرفیت این کد تخفیف به پایان رسیده است.");
            if (subtotal < dc.MinOrder) return new DiscountResult(null, 0, "مبلغ سفارش به حد لازم برای این کد نرسیده است.");

            long amount = dc.Type == DiscountType.Percent
                ? (long)Math.Round(subtotal * dc.Value / 100.0)
                : dc.Value;
            if (dc.Type == DiscountType.Percent && dc.MaxDiscount > 0) amount = Math.Min(amount, dc.MaxDiscount);
            amount = Math.Clamp(amount, 0, subtotal);
            return new DiscountResult(dc, amount, null);
        }
    }

    private void ConsumeDiscount(int id)
    {
        var dc = _discountCodes.FirstOrDefault(d => d.Id == id);
        if (dc is not null) dc.UsedCount++;
    }
}
