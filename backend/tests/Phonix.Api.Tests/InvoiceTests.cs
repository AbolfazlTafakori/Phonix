using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The invoice bills what was DELIVERED. When part of an order is cancelled the buyer gets that money back, so
// billing them for it would be charging twice — the cancelled units leave the lines entirely and the totals
// come down with them.
// Seed reference: product 1 = Netflix, user 5 = reza (wallet 920,000 — enough to pay in full).
public class InvoiceTests
{
    // A paid two-account order, with one account delivered and the other rejected.
    private static (Data.StoreData store, Order order) PartlyCancelled(string? discountCode = null)
    {
        var store = TestStore.Create();
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 2, (int?)planId) }, "wallet",
            fromWallet: true, discountCode: discountCode);
        Assert.True(placed.Error is null, placed.Error);
        var order = placed.Order!;

        store.DeliverUnit(order.Id, order.Units[0].Id, "اطلاعات اکانت", "admin");
        var (after, _, error) = store.RejectUnit(order.Id, order.Units[1].Id, "موجود نبود", "admin");
        Assert.Null(error);
        return (store, after!);
    }

    [Fact]
    public void A_cancelled_unit_never_appears_on_the_invoice()
    {
        var (_, order) = PartlyCancelled();

        var invoice = InvoiceBuilder.Build(order);

        // Two were ordered, one delivered: the line is billed for one, not two.
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(line.UnitPrice, line.LineTotal);
        // …and the cancellation is reported once, without naming what it was.
        Assert.Equal(1, invoice.ExcludedCount);
        Assert.Equal(order.Units[1].RefundedAmount, invoice.ExcludedRefund);
    }

    // The whole point of deriving the invoice from the order's own figures: the bill and the money agree.
    [Theory]
    [InlineData(null)]
    [InlineData("WELCOME10")]
    public void The_invoice_total_equals_what_the_buyer_actually_kept_paying(string? code)
    {
        var (_, order) = PartlyCancelled(code);

        var invoice = InvoiceBuilder.Build(order);
        var refunded = order.Units.Where(u => u.Rejected).Sum(u => u.RefundedAmount);

        Assert.Equal(order.Total - refunded, invoice.Total);
        // The totals block itself has to foot, or the printed document contradicts its own arithmetic.
        Assert.Equal(invoice.Total, invoice.Subtotal - invoice.DiscountAmount + invoice.VatAmount + invoice.FeeAmount);
    }

    [Fact]
    public void An_order_with_nothing_cancelled_bills_the_whole_thing()
    {
        var store = TestStore.Create();
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 2, (int?)planId) }, "wallet", fromWallet: true).Order!;
        foreach (var unit in order.Units.ToList())
            store.DeliverUnit(order.Id, unit.Id, "اطلاعات اکانت", "admin");

        var invoice = InvoiceBuilder.Build(store.GetOrder(order.Id)!);

        Assert.Equal(2, Assert.Single(invoice.Lines).Quantity);
        Assert.Equal(0, invoice.ExcludedCount);
        Assert.Equal(order.Total, invoice.Total);
        Assert.Equal(order.Subtotal, invoice.Subtotal);
    }
}
