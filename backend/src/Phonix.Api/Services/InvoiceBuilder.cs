using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// One line of the invoice: a product/plan the buyer actually received.
public record InvoiceLine(string Name, string? Plan, int Quantity, long UnitPrice, long LineTotal);

// A customer invoice. It bills what was DELIVERED, not what was ordered: when part of an order is cancelled,
// those units are refunded, so keeping them on the invoice would charge the buyer for something they got back.
// Every amount is derived by subtracting the cancelled units' shares from the order's own figures, which is
// what makes the invoice foot exactly against the money that actually moved.
public record InvoiceDto(
    string? InvoiceNumber, string OrderCode, string CustomerName, string? CustomerCode, string? CustomerEmail,
    string Date, string? IssuedAt,
    string PaymentMethod, IReadOnlyList<InvoiceLine> Lines,
    long Subtotal, string? DiscountCode, long DiscountAmount, long VatAmount, long FeeAmount, long Total,
    // How many units of the order were cancelled and what went back — shown as one line, never itemized,
    // because a cancelled product has no place on the buyer's invoice.
    int ExcludedCount, long ExcludedRefund);

public static class InvoiceBuilder
{
    // How much of a line's QUANTITY one of its units represents. A line normally fans out into one unit per
    // quantity; a slot-fulfilled line is a single unit covering the whole quantity.
    private static int PerUnitQuantity(Order order, OrderItem line)
    {
        var units = Math.Max(1, order.Units.Count(u =>
            u.ProductId == line.ProductId && (u.Plan ?? "") == (line.Plan ?? "")));
        return Math.Max(1, line.Quantity / units);
    }

    // `buyer` supplies the identity shown in the buyer block; null falls back to the name on the order.
    public static InvoiceDto Build(Order order, AppUser? buyer = null)
    {
        // Orders placed before per-unit fulfillment have no units at all; the whole order is the invoice.
        var hasUnits = order.Units.Count > 0;
        var rejected = hasUnits ? order.Units.Where(u => u.Rejected).ToList() : new List<OrderUnit>();

        var lines = new List<InvoiceLine>();
        foreach (var line in order.Items)
        {
            var perUnit = PerUnitQuantity(order, line);
            var billedQty = hasUnits
                ? order.Units.Count(u => u.ProductId == line.ProductId && (u.Plan ?? "") == (line.Plan ?? "") && u.Delivered) * perUnit
                : line.Quantity;
            if (billedQty <= 0) continue; // every unit of this line was cancelled — it isn't on the invoice
            lines.Add(new InvoiceLine(line.Name, line.Plan, billedQty, line.UnitPrice, line.UnitPrice * billedQty));
        }

        // Each cancelled unit's slice of the order's own figures. Subtracting them (rather than recomputing
        // proportionally from scratch) is what guarantees the invoice adds up to Total minus the refunds.
        long Slice(long total, long price) => total <= 0 || order.Subtotal <= 0
            ? 0
            : (long)Math.Round(total * (double)price / order.Subtotal, MidpointRounding.AwayFromZero);

        long outPrice = 0, outDiscount = 0, outVat = 0, outFee = 0;
        foreach (var unit in rejected)
        {
            var line = order.Items.FirstOrDefault(i =>
                i.ProductId == unit.ProductId && (i.Plan ?? "") == (unit.Plan ?? ""));
            if (line is null) continue;
            var units = Math.Max(1, order.Units.Count(u =>
                u.ProductId == unit.ProductId && (u.Plan ?? "") == (unit.Plan ?? "")));
            var price = (long)Math.Round(line.UnitPrice * (double)line.Quantity / units, MidpointRounding.AwayFromZero);
            outPrice += price;
            outDiscount += Slice(order.DiscountAmount, price);
            outVat += Slice(order.VatAmount, price);
            outFee += Slice(order.FeeAmount, price);
        }

        var subtotal = Math.Max(0, order.Subtotal - outPrice);
        var discount = Math.Max(0, order.DiscountAmount - outDiscount);
        var vat = Math.Max(0, order.VatAmount - outVat);
        var fee = Math.Max(0, order.FeeAmount - outFee);

        return new InvoiceDto(
            order.InvoiceNumber, order.Code, order.UserName, buyer?.Code, buyer?.Email,
            order.Date, order.DeliveredAt,
            order.PaymentMethod, lines,
            subtotal, order.DiscountCode, discount, vat, fee,
            Math.Max(0, subtotal - discount + vat + fee),
            rejected.Count, rejected.Sum(u => u.RefundedAmount));
    }
}
