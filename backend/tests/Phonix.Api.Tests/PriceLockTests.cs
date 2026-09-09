using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Xunit;

namespace Phonix.Api.Tests;

// The buyer is charged the price they were looking at when they committed to paying — not what the live USD
// rate says by the time the order is filed. The quote that guarantees it is signed, owner-scoped and
// short-lived, so it can only ever replay a price this server itself gave to this buyer.
public class PriceLockTests
{
    private static Dictionary<(int, int?), long> Locked(IPriceLock locks, int userId, int productId, long price)
    {
        var token = locks.Issue(userId, new[] { (productId, (int?)null, price) });
        return new Dictionary<(int, int?), long>(locks.Resolve(token, userId)!);
    }

    [Fact]
    public void An_order_is_filed_at_the_quoted_price_not_the_catalogue_price()
    {
        var store = TestStore.Create();
        var locks = TestPriceLock.Create();
        var product = store.GetProduct(1)!;
        var quoted = product.FinalPrice;

        // the rate moves after the buyer was quoted, while they are away paying for what they were shown
        product.Price += 500_000;
        store.UpdateProduct(product);
        Assert.NotEqual(quoted, store.GetProduct(1)!.FinalPrice);

        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت به کارت",
            fromWallet: false, lockedPrices: Locked(locks, 1, 1, quoted)).Order!;

        Assert.Equal(quoted, order.Items[0].UnitPrice);
    }

    [Fact]
    public void Without_a_quote_the_catalogue_price_applies_as_before()
    {
        var store = TestStore.Create();
        var expected = store.GetProduct(1)!.FinalPrice;

        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;

        Assert.Equal(expected, order.Items[0].UnitPrice);
    }

    // The owner is inside the signed payload, so a quote handed to one account cannot price another's order.
    [Fact]
    public void A_quote_issued_to_one_buyer_does_not_resolve_for_another()
    {
        var locks = TestPriceLock.Create();
        var token = locks.Issue(1, new[] { (1, (int?)null, 50_000L) });

        Assert.NotNull(locks.Resolve(token, 1));
        Assert.Null(locks.Resolve(token, 5));
    }

    [Fact]
    public void A_tampered_or_missing_quote_resolves_to_nothing()
    {
        var locks = TestPriceLock.Create();
        var token = locks.Issue(1, new[] { (1, (int?)null, 50_000L) });

        Assert.Null(locks.Resolve(token + "x", 1));
        Assert.Null(locks.Resolve("", 1));
        Assert.Null(locks.Resolve(null, 1));
    }

    // Plans are quoted per (product, plan): two plans of one product must not share a price.
    [Fact]
    public void Each_plan_carries_its_own_quoted_price()
    {
        var locks = TestPriceLock.Create();
        var token = locks.Issue(7, new[] { (3, (int?)11, 100_000L), (3, (int?)12, 250_000L) });
        var map = locks.Resolve(token, 7)!;

        Assert.Equal(100_000L, map[(3, 11)]);
        Assert.Equal(250_000L, map[(3, 12)]);
        Assert.False(map.ContainsKey((3, null)));
    }
}
