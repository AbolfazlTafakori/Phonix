namespace Phonix.Api.Models;

public class PricingSettings
{
    public decimal ReferralCommissionPercent { get; set; }
    public decimal VatPercent { get; set; }
    public decimal GatewayFeePercent { get; set; }
    public decimal CancellationPenaltyPercent { get; set; }
    public long MinWalletCharge { get; set; }
    public long MinWithdraw { get; set; }
    // How many hours before a subscription expires the renewal reminder is sent. Admin-configurable and
    // read dynamically by SubscriptionExpiryWorker each cycle; 0 (or less) disables reminders entirely.
    public int SubscriptionReminderHoursBefore { get; set; } = 48;
    public string Currency { get; set; } = "تومان";
    public bool ShowOriginalPrice { get; set; } = true;
    // USD→Toman rate control for dollar-priced products. When UsdRateAuto is true the live Nobitex rate is
    // used (falling back to ManualUsdRate if Nobitex is unreachable); when false the manual rate is always used.
    public long ManualUsdRate { get; set; }
    public bool UsdRateAuto { get; set; } = true;
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int Months { get; set; }
    public long Price { get; set; }
    // When > 0 this plan is priced in USD; its Toman Price is recomputed from the live rate (see UsdRateService).
    public double PriceUsd { get; set; }
    public int DiscountPercent { get; set; }

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}
