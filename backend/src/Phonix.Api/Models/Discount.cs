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
}
