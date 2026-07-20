using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Order money rules that don't depend on how an order is stored. Keeping them here means the refund a buyer
// sees is computed one way, whichever store is behind it.
public static class OrderRules
{
    // What a buyer gets back when ONE account of an order is cancelled: everything they were actually charged
    // for it — its price, less its slice of the order discount, plus its slice of the VAT and gateway fee.
    // Including tax and fee matters: they were levied on the whole order, so the share belonging to something
    // never delivered is not the shop's to keep. Every slice is proportional to the unit's price within the
    // order, which is also what makes the sum of the parts add back up to the order total.
    public static long UnitRefundAmount(Order order, OrderUnit unit)
    {
        var item = order.Items.FirstOrDefault(i => i.ProductId == unit.ProductId && (i.Plan ?? "") == (unit.Plan ?? ""));
        if (item is null) return 0;
        // A line normally fans out into Quantity units (one unit = one UnitPrice), but a slot-fulfilled line
        // is a SINGLE unit covering the whole quantity — its refund is the line's share, not one seat's.
        var unitsOfLine = Math.Max(1, order.Units.Count(u =>
            u.ProductId == unit.ProductId && (u.Plan ?? "") == (unit.Plan ?? "")));
        var price = (long)Math.Round(item.UnitPrice * (double)item.Quantity / unitsOfLine, MidpointRounding.AwayFromZero);
        if (order.Subtotal <= 0) return price;

        long Share(long total) => total <= 0
            ? 0
            : (long)Math.Round(total * (double)price / order.Subtotal, MidpointRounding.AwayFromZero);

        var net = Math.Max(0, price - Share(order.DiscountAmount));
        return net + Share(order.VatAmount) + Share(order.FeeAmount);
    }

    // What is still owed back to stock for a line: the portion of its quantity whose accounts were never
    // handed over. A delivered account keeps its stock spent.
    public static long UndeliveredQuantity(Order order, OrderItem line)
    {
        var lineUnits = order.Units.Where(u => u.ProductId == line.ProductId && (u.Plan ?? "") == (line.Plan ?? "")).ToList();
        if (lineUnits.Count == 0) return line.Quantity;
        return (long)line.Quantity * lineUnits.Count(u => !u.Delivered) / lineUnits.Count;
    }

    // The order-level delivery text, stitched from every delivered account in order. A single-account order
    // reads as one block; a multi-account one labels each.
    public static string AggregateDeliveryContent(Order o) =>
        string.Join("\n\n", o.Units.Where(u => u.Delivered).OrderBy(u => u.UnitIndex)
            .Select(u => o.Units.Count > 1 ? $"اکانت {u.UnitIndex}:\n{u.DeliveryContent}" : u.DeliveryContent));
}
