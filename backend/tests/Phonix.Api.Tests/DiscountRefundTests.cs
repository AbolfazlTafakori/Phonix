using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A code limited to some products lowered only those lines. Refunding one account of the order must take the
// discount back only from the accounts it lowered: the others are refunded what was really paid for them.
public class DiscountRefundTests
{
    private static int ApprovedCard(IDataStore store)
    {
        var card = store.AddCard(1, "6037991234567893", "علی محمدی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        return card.Id;
    }

    // Netflix (product 1) and Spotify (product 2) in one order, paid by card, with a 50% code for Netflix only.
    private static Order PaidMixedOrder(IDataStore store)
    {
        store.AddDiscountCode(new DiscountCode
        {
            Code = "NETFLIX50", Type = DiscountType.Percent, Value = 50, IsActive = true, ProductIds = new() { 1 },
        });
        var netflixPlan = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        // Spotify has no plans: it is priced on the product itself.
        var placed = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)netflixPlan), (2, 1, (int?)null) },
            "درگاه", fromWallet: false, discountCode: "NETFLIX50", paymentMethodId: 3,
            payment: new RemainderPayment(ApprovedCard(store), "/uploads/r.png", "TRK-9", "1403/03/22", null), customerCheckout: true);
        Assert.Null(placed.Error);
        var order = placed.Order!;
        var payment = store.GetTransactions().First(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment);
        store.DecideTransaction(payment.Id, TxStatus.Approved, DecisionVia.Site, null, "reza");
        return store.GetOrder(order.Id)!;
    }

    private static long Expected(Order o, int productId, long discountTaken)
    {
        var price = o.Items.First(i => i.ProductId == productId).UnitPrice;
        var net = price - discountTaken;
        var goods = o.Subtotal - o.DiscountAmount;
        var vat = goods > 0 ? (long)Math.Round(o.VatAmount * (double)net / goods, MidpointRounding.AwayFromZero) : 0;
        // The gateway fee was charged on the whole order: each account carries its share of it by price.
        var fee = (long)Math.Round(o.FeeAmount * (double)price / o.Subtotal, MidpointRounding.AwayFromZero);
        return net + vat + fee;
    }

    [Fact]
    public void An_account_the_code_did_not_cover_is_refunded_its_full_price()
    {
        var store = TestStore.Create();
        var order = PaidMixedOrder(store);
        Assert.Equal(new List<int> { 1 }, order.DiscountProductIds);
        Assert.True(order.DiscountAmount > 0);
        var spotify = order.Units.First(u => u.ProductId == 2);

        var (_, refunded, error) = store.RejectUnit(order.Id, spotify.Id, "موجود نیست", "reza");

        Assert.Null(error);
        Assert.Equal(Expected(order, 2, discountTaken: 0), refunded);
    }

    [Fact]
    public void The_covered_account_gives_back_the_whole_discount()
    {
        var store = TestStore.Create();
        var order = PaidMixedOrder(store);
        var netflix = order.Units.First(u => u.ProductId == 1);

        var (_, refunded, error) = store.RejectUnit(order.Id, netflix.Id, "موجود نیست", "reza");

        Assert.Null(error);
        Assert.Equal(Expected(order, 1, discountTaken: order.DiscountAmount), refunded);
    }
}
