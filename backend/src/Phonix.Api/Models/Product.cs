namespace Phonix.Api.Models;

public class ProductFeature
{
    public string Text { get; set; } = "";
    public bool Included { get; set; } = true;
}

public class ProductPlan
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Months { get; set; }
    public long Price { get; set; }
    // When > 0 this plan is priced in USD; its Toman Price is recomputed from the live rate (see UsdRateService).
    public double PriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public bool IsActive { get; set; } = true;

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public long Price { get; set; }
    // When > 0 the product is priced in USD; its Toman Price above is recomputed from the live USDT→Toman
    // rate (see UsdRateService) so it tracks the exchange rate. 0 means the Toman Price is set manually.
    public double PriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public long Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Featured { get; set; }
    public string Image { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Description { get; set; } = "";
    public string Warning { get; set; } = "";
    // Minimum identity level required to buy this product (1 = bank card, 2 = national ID). Configured in
    // the admin panel and never shown to customers; enforced at checkout. Defaults to 1 so level-0 users
    // (registered only) can never purchase.
    public int RequiredLevel { get; set; } = 1;
    // Pre-written delivery text for this product; prefills the admin deliver modal so staff
    // don't retype the same instructions for every order of the same product. (Legacy single template,
    // kept for backward compatibility; the multi-template system below supersedes it.)
    public string DeliveryTemplate { get; set; } = "";
    // Multiple named, reusable delivery templates the admin can pick from in the deliver modal. Managed via
    // the product templates endpoints and persisted with the product.
    public List<ProductDeliveryTemplate> DeliveryTemplates { get; set; } = new();
    public List<ProductFeature> Features { get; set; } = new();
    public List<ProductPlan> Plans { get; set; } = new();

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}
