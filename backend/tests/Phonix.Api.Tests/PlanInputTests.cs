using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Covers per-account (unit) capture and delivery: customer info lands on the matching unit, a skipped line
// doesn't shift alignment, and delivering every unit completes the order.
public class PlanInputTests
{
    [Fact]
    public void PlaceOrder_creates_one_unit_per_quantity_with_its_inputs()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var planId = store.GetProduct(1)!.Plans[0].Id;
        var lineInfo = new[]
        {
            new OrderLineInfo(new[]
            {
                new OrderUnitInfo(new List<OrderInputValue> { new() { Label = "ایمیل", Value = "first@b.com" } }, "اولی"),
                new OrderUnitInfo(new List<OrderInputValue> { new() { Label = "ایمیل", Value = "second@b.com" } }, null),
            }),
        };

        var res = store.PlaceOrder(user, new[] { (1, 2, (int?)planId) }, "کارت", fromWallet: false, lineInfo: lineInfo);

        Assert.Null(res.Error);
        Assert.Equal(2, res.Order!.Units.Count);
        Assert.Equal("first@b.com", res.Order.Units[0].CustomerInputs[0].Value);
        Assert.Equal("اولی", res.Order.Units[0].CustomerNote);
        Assert.Equal("second@b.com", res.Order.Units[1].CustomerInputs[0].Value);
        Assert.Equal(2, res.Order.Units[1].UnitIndex);
    }

    [Fact]
    public void Line_info_stays_aligned_when_an_earlier_line_is_skipped()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var planId = store.GetProduct(1)!.Plans[0].Id;

        // First line has quantity 0 → skipped; its info must not bleed onto the second line's unit.
        var items = new[] { (1, 0, (int?)null), (1, 1, (int?)planId) };
        var lineInfo = new[]
        {
            new OrderLineInfo(new[] { new OrderUnitInfo(new List<OrderInputValue> { new() { Label = "x", Value = "skipped" } }, null) }),
            new OrderLineInfo(new[] { new OrderUnitInfo(new List<OrderInputValue> { new() { Label = "ایمیل", Value = "real@b.com" } }, null) }),
        };

        var res = store.PlaceOrder(user, items, "کارت", fromWallet: false, lineInfo: lineInfo);

        var unit = Assert.Single(res.Order!.Units);
        Assert.Equal("real@b.com", unit.CustomerInputs[0].Value);
    }

    [Fact]
    public void Delivering_every_unit_completes_the_order()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var res = store.PlaceOrder(user, new[] { (1, 2, (int?)null) }, "کارت", fromWallet: false);
        var order = res.Order!;
        Assert.Equal(2, order.Units.Count);

        var (_, firstDone) = store.DeliverUnit(order.Id, order.Units[0].Id, "اطلاعات اکانت اول", "admin");
        Assert.False(firstDone); // still one unit left
        Assert.NotEqual(OrderStatus.Completed, store.GetOrder(order.Id)!.Status);

        var (completed, secondDone) = store.DeliverUnit(order.Id, order.Units[1].Id, "اطلاعات اکانت دوم", "admin");
        Assert.True(secondDone);
        Assert.Equal(OrderStatus.Completed, completed!.Status);
        Assert.True(completed.Units.All(u => u.Delivered));
    }
}
