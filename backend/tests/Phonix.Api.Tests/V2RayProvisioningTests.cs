using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// A purchased V2Ray plan becomes a real account on the panel, and the buyer reaches its live status page by a
// token alone. Two things have to hold for that to work at all: the order must remember WHICH plan was
// bought (the panel, inbounds and limits all come from it), and a token must resolve back to exactly one
// unit. Both are easy to break from unrelated changes, so they are pinned here.
public class V2RayProvisioningTests
{
    private static (IDataStore store, int productId, int planId) Seed(int quantityCap = 0)
    {
        var store = TestStore.Create();
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "سرویس‌های ماهانه", Active = true });
        store.AddV2RayPanel(new V2RayPanel { Url = "https://nl.example.com:8080", Name = "هلند تانل", Flag = "NL" });

        var plan = store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ دو کاربر", PanelId = 1, InboundIds = new() { 1 },
            Protocol = "vless", Network = "ws",
            VolumeGb = 20, DurationDays = 30, IpLimit = 2, Price = 300_000, Active = true,
            Quantity = quantityCap,
        });

        var product = store.AddProduct(new Product
        {
            Name = "خرید اشتراک V2Ray", CategoryId = 1, IsActive = true,
            V2RayCategoryId = category.Id, Plans = new(),
        });

        return (store, product.Id, plan.Id);
    }

    [Fact]
    public void An_order_remembers_the_v2ray_plan_that_was_bought()
    {
        var (store, productId, planId) = Seed();

        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        var order = placed.Order!;

        // Provisioning reads the plan off the unit it is serving; without this it cannot know which panel,
        // which inbounds, or what limits the customer actually paid for.
        Assert.Equal(planId, order.Units[0].PlanId);
        Assert.Equal(planId, order.Items[0].PlanId);
    }

    [Fact]
    public void A_config_token_resolves_to_its_own_unit()
    {
        var (store, productId, planId) = Seed();
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 2, (int?)planId) }, "wallet", fromWallet: true).Order!;

        var first = order.Units[0];
        var second = order.Units[1];
        // Real tokens are exactly what NewToken() mints — 16 random bytes as 32 lowercase hex characters —
        // and the lookup now refuses anything else, since an unvalidated token reaches a LIKE pattern where
        // '%' would match every order in the shop.
        store.SetUnitV2Ray(order.Id, first.Id, new V2RayAccount { Token = new string('a', 32), Uuid = "uuid-1", Email = "e1" });
        store.SetUnitV2Ray(order.Id, second.Id, new V2RayAccount { Token = new string('b', 32), Uuid = "uuid-2", Email = "e2" });

        var found = store.FindUnitByV2RayToken(new string('b', 32));

        Assert.NotNull(found);
        Assert.Equal(second.Id, found!.Value.unit.Id);
        Assert.Equal("uuid-2", found.Value.unit.V2Ray!.Uuid);
    }

    [Fact]
    public void An_unknown_token_resolves_to_nothing()
    {
        var (store, productId, planId) = Seed();
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;
        store.SetUnitV2Ray(order.Id, order.Units[0].Id, new V2RayAccount { Token = new string('a', 32) });

        // The config page is reachable by token alone, so a wrong one must reveal nothing at all.
        Assert.Null(store.FindUnitByV2RayToken(new string('c', 32)));
        Assert.Null(store.FindUnitByV2RayToken(""));
        // Anything that isn't a real token shape is refused before it reaches SQLite — in particular the LIKE
        // wildcards, which would otherwise match every order row and scan the whole table on an ANONYMOUS
        // endpoint. A bare "%" must not resolve to the account that does exist.
        Assert.Null(store.FindUnitByV2RayToken("%"));
        Assert.Null(store.FindUnitByV2RayToken(new string('%', 32)));
        Assert.Null(store.FindUnitByV2RayToken("a".PadRight(32, '_')));
        Assert.Null(store.FindUnitByV2RayToken(new string('a', 31)));
        Assert.Null(store.FindUnitByV2RayToken(new string('A', 32)));
    }

    [Fact]
    public void Provisioning_details_survive_a_reload()
    {
        var (store, productId, planId) = Seed();
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;
        var expiry = DateTime.UtcNow.AddDays(30);

        store.SetUnitV2Ray(order.Id, order.Units[0].Id, new V2RayAccount
        {
            PanelId = 1, PlanId = planId, Email = "ph-1-1", Uuid = "u-1", SubId = "s-1",
            SubUrl = "https://sub.example.com/sub/s-1", Token = "tok-1",
            Protocol = "vless", Network = "ws", VolumeGb = 20, DurationDays = 30, IpLimit = 2,
            ExpiresAtUtc = expiry,
        });

        var unit = store.GetOrder(order.Id)!.Units[0];

        Assert.Equal("https://sub.example.com/sub/s-1", unit.V2Ray!.SubUrl);
        Assert.Equal(20, unit.V2Ray.VolumeGb);
        Assert.Equal(2, unit.V2Ray.IpLimit);
        Assert.Equal(expiry, unit.V2Ray.ExpiresAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_v2ray_product_sells_without_a_stock_counter()
    {
        var (store, productId, planId) = Seed();
        // Deliberately zero: the panel provisions on demand, so there is no inventory to keep topped up.
        Assert.Equal(0, store.GetProduct(productId)!.Stock);

        var first = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(first.Error);

        // …and it does not quietly count down, which would stop sales after the first buyer.
        Assert.Equal(0, store.GetProduct(productId)!.Stock);
    }

    [Fact]
    public void A_capped_plan_stops_selling_once_it_runs_out()
    {
        var (store, productId, planId) = Seed(quantityCap: 2);
        var buyer = store.GetUser(5)!;

        Assert.Null(store.PlaceOrder(buyer, new[] { (productId, 2, (int?)planId) }, "wallet", fromWallet: true).Error);

        // The cap is the whole supply for this plan, so the next buyer is refused rather than oversold.
        var over = store.PlaceOrder(buyer, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.NotNull(over.Error);
        Assert.Contains("ظرفیت", over.Error);
    }

    [Fact]
    public void A_sold_out_plan_disappears_from_the_storefront()
    {
        var (store, productId, planId) = Seed(quantityCap: 1);
        Assert.Single(store.GetProduct(productId)!.Plans, p => p.IsActive);

        Assert.Null(store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Error);

        // The picker only offers active plans, so a full one is no longer selectable. It is still carried in
        // the list (inactive) so a stale checkout can be told it filled up rather than "not found".
        Assert.DoesNotContain(store.GetProduct(productId)!.Plans, p => p.IsActive);
    }

    [Fact]
    public void Cancelling_an_undelivered_order_gives_the_plan_its_place_back()
    {
        var (store, productId, planId) = Seed(quantityCap: 1);
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;
        Assert.DoesNotContain(store.GetProduct(productId)!.Plans, p => p.IsActive);   // sold out

        store.CancelOrder(order.Id, "admin", "test");

        // A refunded sale must not hold a place forever.
        Assert.Equal(0, store.GetV2RayPlan(planId)!.Sold);
        Assert.Single(store.GetProduct(productId)!.Plans, p => p.IsActive);
    }

    [Fact]
    public void An_uncapped_plan_is_never_limited()
    {
        var (store, productId, planId) = Seed();   // Quantity = 0 → unlimited
        var buyer = store.GetUser(5)!;

        for (var i = 0; i < 3; i++)
            Assert.Null(store.PlaceOrder(buyer, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Error);

        Assert.Single(store.GetProduct(productId)!.Plans, p => p.IsActive);
    }

    [Fact]
    public void Editing_a_plan_does_not_reset_what_it_has_already_sold()
    {
        var (store, productId, planId) = Seed(quantityCap: 2);
        Assert.Null(store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Error);
        Assert.Equal(1, store.GetV2RayPlan(planId)!.Sold);

        // The admin form posts the plan's editable fields; the sold tally is not one of them and must survive,
        // or a price tweak would silently hand out the whole cap again.
        var edited = store.GetV2RayPlan(planId)!;
        edited.Price = 350_000;
        store.UpdateV2RayPlan(edited);

        Assert.Equal(1, store.GetV2RayPlan(planId)!.Sold);
        Assert.Equal(350_000, store.GetV2RayPlan(planId)!.Price);
    }
}
