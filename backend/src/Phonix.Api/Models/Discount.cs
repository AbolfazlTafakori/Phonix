namespace Phonix.Api.Models;

public enum DiscountType
{
    Percent,
    Fixed,
}

public class DiscountCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public DiscountType Type { get; set; } = DiscountType.Percent;
    public long Value { get; set; }          // percent (0-100) when Percent, otherwise a fixed Toman amount
    public long MinOrder { get; set; }       // minimum order subtotal required to use the code
    public long MaxDiscount { get; set; }    // cap for percent discounts (0 = no cap)
    public int UsageLimit { get; set; }      // total allowed uses (0 = unlimited)
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }  // UTC; null = never expires

    // The products this code works on. Empty = every product, which is how every code filed before this
    // existed keeps behaving. When set, only those lines of the basket are discounted: a percentage is taken
    // of their total alone, a fixed amount never exceeds it, and MinOrder is measured against it too — so
    // padding the basket with other products can neither unlock the code nor enlarge the discount.
    public List<int> ProductIds { get; set; } = new();
}
