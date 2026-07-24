using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// A product linked to a V2Ray category has no plans of its own — they are projected from that category on
// every read. This is what makes the price, the plan selector, the cart, the checkout and order placement
// all agree, so it is worth pinning: if the projection ever stops happening, orders for these products would
// be rejected because the store looks the chosen plan up in product.Plans.
public class V2RayProductLinkTests
{
    private static (Phonix.Api.Data.IDataStore store, int categoryId) SeedCatalogue()
    {
        var store = TestStore.Create();
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "سرویس‌های یک‌ماهه", Active = true });

        store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۵ گیگ یک کاربر", PanelId = 1, InboundIds = new() { 1 },
            VolumeGb = 5, DurationDays = 30, IpLimit = 1, Price = 100_000, Active = true, SortOrder = 1,
        });
        store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ دو کاربر", PanelId = 1, InboundIds = new() { 1 },
            VolumeGb = 20, DurationDays = 90, IpLimit = 2, Price = 300_000, DiscountPercent = 10, Active = true, SortOrder = 2,
        });
        // Inactive plans must never reach the storefront.
        store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "پلن غیرفعال", PanelId = 1, InboundIds = new() { 1 },
            VolumeGb = 1, DurationDays = 30, IpLimit = 1, Price = 1, Active = false, SortOrder = 3,
        });

        return (store, category.Id);
    }

    private static Product NewProduct(Phonix.Api.Data.IDataStore store, int v2rayCategoryId) =>
        store.AddProduct(new Product
        {
            Name = "خرید اشتراک V2Ray", CategoryId = 1, IsActive = true,
            V2RayCategoryId = v2rayCategoryId,
            Plans = new(), // linked products own no plans
        });

    [Fact]
    public void A_linked_product_reads_its_plans_from_the_v2ray_category()
    {
        var (store, categoryId) = SeedCatalogue();
        var created = NewProduct(store, categoryId);

        var product = store.GetProduct(created.Id)!;

        Assert.Equal(2, product.Plans.Count); // the inactive one is excluded
        Assert.Equal("۵ گیگ یک کاربر", product.Plans[0].Type);
        Assert.Equal("۲۰ گیگ دو کاربر", product.Plans[1].Type);
    }

    [Fact]
    public void The_projected_plan_carries_the_price_the_customer_pays()
    {
        var (store, categoryId) = SeedCatalogue();
        var created = NewProduct(store, categoryId);

        var plans = store.GetProduct(created.Id)!.Plans;

        Assert.Equal(100_000, plans[0].Price);
        Assert.Equal(100_000, plans[0].FinalPrice);
        // 10% off 300,000
        Assert.Equal(300_000, plans[1].Price);
        Assert.Equal(270_000, plans[1].FinalPrice);
    }

    [Fact]
    public void Days_are_translated_into_the_months_the_storefront_speaks()
    {
        var (store, categoryId) = SeedCatalogue();
        var created = NewProduct(store, categoryId);

        var plans = store.GetProduct(created.Id)!.Plans;

        Assert.Equal(1, plans[0].Months);  // 30 days
        Assert.Equal(3, plans[1].Months);  // 90 days
    }

    [Fact]
    public void The_ip_limit_becomes_the_plan_user_count()
    {
        var (store, categoryId) = SeedCatalogue();
        var created = NewProduct(store, categoryId);

        var plans = store.GetProduct(created.Id)!.Plans;

        Assert.Equal(1, plans[0].UserCount);
        Assert.Equal(2, plans[1].UserCount);
    }

    [Fact]
    public void The_projection_also_applies_when_products_are_listed()
    {
        // The listing is where the "starting from" price is read, so it has to see the plans too.
        var (store, categoryId) = SeedCatalogue();
        var created = NewProduct(store, categoryId);

        var listed = store.GetProducts().Single(p => p.Id == created.Id);

        Assert.Equal(2, listed.Plans.Count);
    }

    [Fact]
    public void An_ordinary_product_is_left_completely_alone()
    {
        var (store, _) = SeedCatalogue();
        var created = store.AddProduct(new Product
        {
            Name = "محصول عادی", CategoryId = 1, IsActive = true, V2RayCategoryId = 0,
            Plans = new() { new ProductPlan { Id = 1, Type = "اشتراکی", Months = 1, Price = 50_000, IsActive = true } },
        });

        var product = store.GetProduct(created.Id)!;

        Assert.Single(product.Plans);
        Assert.Equal("اشتراکی", product.Plans[0].Type);
    }
}
