using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The catalogue cache serves the two reads every anonymous visitor makes. The property that matters is that it
// never outlives an edit: a stale product listing shows wrong prices to buyers.
public class CatalogCacheTests
{
    private static List<ProductDto> One(string name) =>
        new() { new Product { Name = name }.ToDto("") };

    [Fact]
    public void A_second_read_is_served_without_touching_the_store()
    {
        var cache = new CatalogCache();
        var builds = 0;

        List<ProductDto> Load() { builds++; return One("first"); }

        Assert.Equal("first", cache.Products(Load)[0].Name);
        Assert.Equal("first", cache.Products(Load)[0].Name);
        Assert.Equal(1, builds); // the store was read once for two requests
    }

    [Fact]
    public void An_edit_is_visible_on_the_very_next_request()
    {
        var cache = new CatalogCache();
        cache.Products(() => One("before"));

        cache.Invalidate(); // what every catalogue write calls

        Assert.Equal("after", cache.Products(() => One("after"))[0].Name);
    }

    [Fact]
    public void Categories_and_products_are_both_dropped_by_an_edit()
    {
        var cache = new CatalogCache();
        cache.Products(() => One("p"));
        cache.Categories(() => new List<CategoryDto> { new Category { Name = "c" }.ToDto(0) });

        cache.Invalidate();

        var productBuilds = 0;
        var categoryBuilds = 0;
        cache.Products(() => { productBuilds++; return One("p"); });
        cache.Categories(() => { categoryBuilds++; return new List<CategoryDto> { new Category { Name = "c" }.ToDto(0) }; });
        Assert.Equal(1, productBuilds);
        Assert.Equal(1, categoryBuilds);
    }

    [Fact]
    public void Setting_the_ttl_to_zero_turns_caching_off_entirely()
    {
        Environment.SetEnvironmentVariable("PHONIX_CATALOG_CACHE_SECONDS", "0");
        try
        {
            var cache = new CatalogCache();
            var builds = 0;
            cache.Products(() => { builds++; return One("x"); });
            cache.Products(() => { builds++; return One("x"); });

            Assert.False(cache.Enabled);
            Assert.Equal(2, builds); // every request goes to the store
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHONIX_CATALOG_CACHE_SECONDS", null);
        }
    }
}
