using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Covers the per-plan customer-input capture path: values supplied at checkout must land on the matching
// order line, and an extra leading line that gets skipped must not shift the alignment.
public class PlanInputTests
{
    [Fact]
    public void PlaceOrder_attaches_customer_inputs_and_note_to_the_line()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var planId = store.GetProduct(1)!.Plans[0].Id;
        var info = new OrderLineInfo(
            new List<OrderInputValue> { new() { Label = "ایمیل اکانت", Value = "a@b.com", Sensitive = false } },
            "یک توضیح");

        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)planId) }, "کارت", fromWallet: false,
            lineInfo: new[] { info });

        Assert.Null(res.Error);
        var item = Assert.Single(res.Order!.Items);
        var input = Assert.Single(item.CustomerInputs);
        Assert.Equal("ایمیل اکانت", input.Label);
        Assert.Equal("a@b.com", input.Value);
        Assert.Equal("یک توضیح", item.CustomerNote);
    }

    [Fact]
    public void Line_info_stays_aligned_when_an_earlier_line_is_skipped()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var planId = store.GetProduct(1)!.Plans[0].Id;

        // First line has quantity 0 → skipped by PlaceOrder; its info entry must not bleed onto the second.
        var items = new[] { (1, 0, (int?)null), (1, 1, (int?)planId) };
        var lineInfo = new[]
        {
            new OrderLineInfo(new List<OrderInputValue> { new() { Label = "x", Value = "skipped" } }, null),
            new OrderLineInfo(new List<OrderInputValue> { new() { Label = "ایمیل", Value = "real@b.com" } }, null),
        };

        var res = store.PlaceOrder(user, items, "کارت", fromWallet: false, lineInfo: lineInfo);

        var item = Assert.Single(res.Order!.Items);
        var input = Assert.Single(item.CustomerInputs);
        Assert.Equal("ایمیل", input.Label);
        Assert.Equal("real@b.com", input.Value);
    }
}
