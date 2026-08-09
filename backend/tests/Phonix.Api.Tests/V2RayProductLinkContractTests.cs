using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The link between a product and its V2Ray category is the whole reason a V2Ray product has anything to
// sell: break it and every plan silently vanishes from the storefront, with no error raised anywhere. Two
// separate mistakes could do that, and both are pinned here.
public class V2RayProductLinkContractTests
{
    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static ProductsController Controller(IDataStore store)
    {
        var rate = new UsdRateService(new NoHttp(), store, NullLogger<UsdRateService>.Instance);
        return new ProductsController(store, rate, new LocalFileStorageService(), new CatalogCache())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "1"),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Admin)),
                    }, "test")),
                },
            },
        };
    }

    private static (IDataStore store, int productId, int categoryId) Seed()
    {
        var store = TestStore.Create();
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "اشتراک V2Ray", Active = true });
        store.AddV2RayPanel(new V2RayPanel { Url = "https://de.example.com:8080", Name = "آلمان تانل", Flag = "DE" });
        store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۵ گیگ یک کاربر", PanelId = 1, InboundIds = new() { 1 },
            VolumeGb = 5, DurationDays = 30, IpLimit = 1, Price = 100_000, Active = true,
        });
        var product = store.AddProduct(new Product
        {
            Name = "خرید اشتراک V2Ray", CategoryId = 1, IsActive = true,
            V2RayCategoryId = category.Id, Plans = new(),
        });
        return (store, product.Id, category.Id);
    }

    // A positional record with only the fields a caller would realistically set.
    private static ProductInput Input(int? v2rayCategoryId) =>
        new(Name: "خرید اشتراک V2Ray", CategoryId: 1, Price: 100_000, DiscountPercent: 0, Stock: 0,
            IsActive: true, Featured: false, Image: "", Logo: null, Gallery: null, Sku: "", Description: "",
            Warning: null, RequiredLevel: 1, Features: null, Plans: null, DeliveryTemplate: null,
            PriceUsd: null, Faq: null, ListImage: null, V2RayCategoryId: v2rayCategoryId);

    [Fact]
    public void The_link_survives_an_update_that_omits_the_field()
    {
        var (store, productId, categoryId) = Seed();

        // The admin form read the id back under the wrong name for a while, so it posted nothing here. Any
        // ordinary edit — renaming the product, fixing a typo — then detached it from its whole catalogue.
        var result = Controller(store).Update(productId, Input(null));

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        Assert.Equal(categoryId, store.GetProduct(productId)!.V2RayCategoryId);
        Assert.NotEmpty(store.GetProduct(productId)!.Plans);
    }

    [Fact]
    public void An_explicit_zero_still_unlinks_the_product()
    {
        var (store, productId, _) = Seed();

        // "Ordinary product" is a real choice in the dropdown and has to keep working.
        Controller(store).Update(productId, Input(0));

        Assert.Equal(0, store.GetProduct(productId)!.V2RayCategoryId);
    }

    [Fact]
    public void The_link_can_be_moved_to_another_category()
    {
        var (store, productId, _) = Seed();
        var other = store.AddV2RayCategory(new V2RayCategory { Name = "دسته دوم", Active = true });

        Controller(store).Update(productId, Input(other.Id));

        Assert.Equal(other.Id, store.GetProduct(productId)!.V2RayCategoryId);
    }

    // The browser reads this id off the product JSON to decide whether the page is selling V2Ray at all.
    // C#'s camelCase policy keeps the capital R (V2RayCategoryId -> v2RayCategoryId), which is easy to spell
    // wrong by hand and produces `undefined` rather than an error — silent on both sides.
    [Fact]
    public void The_category_id_serializes_under_the_name_the_browser_reads()
    {
        var json = JsonSerializer.Serialize(
            new Product { Id = 1, Name = "x", V2RayCategoryId = 7 },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("v2RayCategoryId", out var id),
            "the product JSON must expose v2RayCategoryId — the frontend reads exactly this name");
        Assert.Equal(7, id.GetInt32());
    }
}
