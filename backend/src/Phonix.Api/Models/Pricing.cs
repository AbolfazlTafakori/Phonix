namespace Phonix.Api.Models;

public class PricingSettings
{
    public decimal ReferralCommissionPercent { get; set; }
    public decimal VatPercent { get; set; }
    public decimal GatewayFeePercent { get; set; }
    public long MinWalletCharge { get; set; }
    public long MinWithdraw { get; set; }
    public string Currency { get; set; } = "تومان";
    public bool ShowOriginalPrice { get; set; } = true;
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int Months { get; set; }
    public long Price { get; set; }
    public int DiscountPercent { get; set; }

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}
