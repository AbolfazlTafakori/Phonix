using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// The dashboard's «پرفروش‌ترین محصولات» ranking must count only what a buyer actually kept. Two ways an order
// line stops being a sale after the fact: the whole order is cancelled (by the buyer or by staff), or staff
// reject one account inside an order that otherwise goes through and refund the buyer for it. The line stays
// on the order either way, so both have to be taken back out of the ranking.
public class StatsTopProductsTests
{
    private static long SoldOf(IDataStore store, int productId) =>
        new StatsController(store).TopProducts().FirstOrDefault(p => p.ProductId == productId)?.Sold ?? 0;

    private static long RevenueOf(IDataStore store, int productId) =>
        new StatsController(store).TopProducts().FirstOrDefault(p => p.ProductId == productId)?.Revenue ?? 0;

    [Fact]
    public void An_account_rejected_inside_a_live_order_stops_counting_as_sold()
    {
        var store = TestStore.Create();
        var before = SoldOf(store, 1);
        var revenueBefore = RevenueOf(store, 1);

        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 2, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;
        Assert.Equal(before + 2, SoldOf(store, 1));

        var (rejected, refunded, error) = store.RejectUnit(order.Id, order.Units[0].Id, "اکانت خراب بود");
        Assert.Null(error);
        Assert.NotNull(rejected);
        Assert.True(refunded > 0);

        Assert.Equal(before + 1, SoldOf(store, 1));                                   // one account, not two
        Assert.Equal(order.Items[0].LineTotal - refunded, RevenueOf(store, 1) - revenueBefore);
    }

    [Fact]
    public void A_cancelled_order_leaves_the_ranking_exactly_as_it_was()
    {
        var store = TestStore.Create();
        var before = SoldOf(store, 1);
        var revenueBefore = RevenueOf(store, 1);

        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 2, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;
        Assert.Equal(before + 2, SoldOf(store, 1));

        store.CancelOrder(order.Id, "admin", "تست");

        Assert.Equal(before, SoldOf(store, 1));
        Assert.Equal(revenueBefore, RevenueOf(store, 1));
    }

    // Rejecting the last live account cancels the order itself, so the product leaves the ranking by that
    // route rather than through the Sold > 0 filter. Pinned because the two rules have to agree: whichever
    // one fires, a product nobody kept must not sit in the list on zero.
    [Fact]
    public void A_product_whose_every_account_was_rejected_drops_out_of_the_ranking()
    {
        var store = TestStore.Create();
        var product = store.GetProducts().First(p => p.Stock > 0 && new StatsController(store).TopProducts().All(t => t.ProductId != p.Id));

        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (product.Id, 1, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;
        Assert.Equal(1, SoldOf(store, product.Id));

        store.RejectUnit(order.Id, order.Units[0].Id, "موجود نبود");

        Assert.DoesNotContain(new StatsController(store).TopProducts(), t => t.ProductId == product.Id);
    }
}
