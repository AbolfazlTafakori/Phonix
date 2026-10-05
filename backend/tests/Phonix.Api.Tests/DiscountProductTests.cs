using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// A discount code can be limited to chosen products. Outside them it must not work at all, and inside a mixed
// basket it must only ever discount its own products — never the rest of the cart.
public class DiscountProductRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DiscountCode Code(DiscountType type, long value, params int[] productIds) => new()
    {
        Id = 1, Code = "ONLY", Type = type, Value = value, IsActive = true, ProductIds = productIds.ToList(),
    };

    private static List<DiscountLine> Basket(params (int productId, long total)[] lines) =>
        lines.Select(l => new DiscountLine(l.productId, l.total)).ToList();

    [Fact]
    public void A_percentage_is_taken_of_the_eligible_products_only()
    {
        var result = DiscountRules.Resolve(Code(DiscountType.Percent, 10, 2), Basket((1, 300_000), (2, 200_000)), Now);

        Assert.Null(result.Error);
        Assert.Equal(20_000, result.Amount); // 10% of product 2's 200,000 — not of the 500,000 basket
    }

    [Fact]
    public void A_basket_without_any_eligible_product_is_refused()
    {
        var result = DiscountRules.Resolve(Code(DiscountType.Percent, 10, 2), Basket((1, 300_000), (3, 100_000)), Now);

        Assert.NotNull(result.Error);
        Assert.Contains("قابل استفاده نیست", result.Error);
        Assert.Equal(0, result.Amount);
        Assert.Null(result.Code);
    }

    [Fact]
    public void A_fixed_amount_never_exceeds_what_the_eligible_products_cost()
    {
        var result = DiscountRules.Resolve(Code(DiscountType.Fixed, 150_000, 2), Basket((1, 900_000), (2, 100_000)), Now);

        Assert.Equal(100_000, result.Amount);
    }

    [Fact]
    public void The_minimum_is_measured_on_the_eligible_products_not_the_whole_basket()
    {
        var code = Code(DiscountType.Fixed, 20_000, 2);
        code.MinOrder = 250_000;

        // A big basket of OTHER products must not unlock a code meant for product 2.
        var padded = DiscountRules.Resolve(code, Basket((1, 900_000), (2, 200_000)), Now);
        var enough = DiscountRules.Resolve(code, Basket((2, 300_000)), Now);

        Assert.NotNull(padded.Error);
        Assert.Null(enough.Error);
        Assert.Equal(20_000, enough.Amount);
    }

    [Fact]
    public void Several_products_can_share_one_code()
    {
        var result = DiscountRules.Resolve(Code(DiscountType.Percent, 10, 2, 3), Basket((1, 500_000), (2, 100_000), (3, 200_000)), Now);

        Assert.Equal(30_000, result.Amount);
    }

    [Fact]
    public void A_code_with_no_products_listed_still_covers_the_whole_basket()
    {
        // Every code filed before the product list existed has it empty, and must keep working as it did.
        var result = DiscountRules.Resolve(Code(DiscountType.Percent, 10), Basket((1, 300_000), (2, 200_000)), Now);

        Assert.Equal(50_000, result.Amount);
    }

    [Fact]
    public void The_percentage_cap_still_applies_to_a_restricted_code()
    {
        var code = Code(DiscountType.Percent, 50, 2);
        code.MaxDiscount = 30_000;

        Assert.Equal(30_000, DiscountRules.Resolve(code, Basket((2, 200_000)), Now).Amount);
    }
}

// The same rule, through the paths a customer actually takes: the checkout preview and placing the order.
public class DiscountProductOrderTests
{
    // Seed: product 1 = Netflix (290,000), product 2 = Spotify (185,000); user 1 = ali.
    private static DiscountCode Restricted(IDataStore store, string code, params int[] productIds) =>
        store.AddDiscountCode(new DiscountCode
        {
            Code = code, Type = DiscountType.Percent, Value = 10, IsActive = true, ProductIds = productIds.ToList(),
        });

    private static DiscountResultDto Preview(IDataStore store, DiscountValidateInput input) =>
        Assert.IsType<DiscountResultDto>(
            Assert.IsType<OkObjectResult>(new DiscountController(store).Validate(input).Result).Value);

    [Fact]
    public void An_order_discounts_only_the_products_the_code_names()
    {
        var store = TestStore.Create();
        Restricted(store, "SPOTIFY10", 2);
        var spotify = store.GetProduct(2)!.FinalPrice;

        var res = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null), (2, 1, (int?)null) },
            "کارت", fromWallet: false, discountCode: "SPOTIFY10");

        Assert.Null(res.Error);
        Assert.Equal((long)Math.Round(spotify * 10 / 100.0), res.Order!.DiscountAmount);
    }

    [Fact]
    public void An_order_without_the_named_products_is_refused_and_the_code_is_not_spent()
    {
        var store = TestStore.Create();
        var code = Restricted(store, "SPOTIFY10", 2);
        var stockBefore = store.GetProduct(1)!.Stock;

        var res = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) },
            "کارت", fromWallet: false, discountCode: "SPOTIFY10");

        Assert.Null(res.Order);
        Assert.Contains("قابل استفاده نیست", res.Error);
        Assert.Equal(0, store.GetDiscountCodes().Single(d => d.Id == code.Id).UsedCount);
        Assert.Equal(stockBefore, store.GetProduct(1)!.Stock);
    }

    [Fact]
    public void The_preview_prices_a_restricted_code_line_by_line()
    {
        var store = TestStore.Create();
        Restricted(store, "SPOTIFY10", 2);

        var mixed = Preview(store, new DiscountValidateInput("SPOTIFY10", 0, new() { new(1, 290_000), new(2, 185_000) }));

        Assert.True(mixed.Valid);
        Assert.Equal(18_500, mixed.Amount);
        Assert.Equal(475_000 - 18_500, mixed.FinalTotal);
    }

    [Fact]
    public void A_preview_without_line_items_cannot_use_a_restricted_code()
    {
        var store = TestStore.Create();
        Restricted(store, "SPOTIFY10", 2);

        // What a checkout page loaded before this change still sends: the total and nothing else.
        var result = Preview(store, new DiscountValidateInput("SPOTIFY10", 475_000));

        Assert.False(result.Valid);
    }

    [Fact]
    public void Staff_edits_keep_the_product_list_and_drop_bad_ids()
    {
        var store = TestStore.Create();
        var code = Restricted(store, "SPOTIFY10", 2);
        var controller = new DiscountController(store);

        code.ProductIds = new() { 3, 3, 0, -4, 5 };
        controller.Update(code.Id, code);

        Assert.Equal(new[] { 3, 5 }, store.GetDiscountCodes().Single(d => d.Id == code.Id).ProductIds);
    }
}
