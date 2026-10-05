using Phonix.Api.Controllers;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// The promo strip above the header is edited from the panel instead of being baked into the page.
public class PromoBarTests
{
    [Fact]
    public void A_site_that_never_edited_the_strip_keeps_the_text_it_always_showed()
    {
        var store = TestStore.Create();

        var bar = store.GetSiteContent().PromoBar;

        Assert.True(bar.Enabled);
        Assert.Contains("جشنواره تابستانی", bar.Text);
        Assert.Equal("مشاهده تخفیف‌ها", bar.ButtonLabel);
        Assert.Equal("/products", bar.ButtonLink);
    }

    [Fact]
    public void Edits_are_saved_and_served_back()
    {
        var store = TestStore.Create();
        var controller = new SiteContentController(store);
        var content = store.GetSiteContent();
        content.PromoBar = new PromoBarContent
        {
            Enabled = true, Emoji = "🔥", Text = "حراج پاییزه!", ButtonLabel = "خرید", ButtonLink = "/category/vpn",
        };

        controller.Update(content);

        var saved = store.GetSiteContent().PromoBar;
        Assert.Equal("🔥", saved.Emoji);
        Assert.Equal("حراج پاییزه!", saved.Text);
        Assert.Equal("خرید", saved.ButtonLabel);
        Assert.Equal("/category/vpn", saved.ButtonLink);
    }

    [Fact]
    public void The_strip_can_be_switched_off()
    {
        var store = TestStore.Create();
        var content = store.GetSiteContent();
        content.PromoBar.Enabled = false;

        new SiteContentController(store).Update(content);

        Assert.False(store.GetSiteContent().PromoBar.Enabled);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("//evil.example")]
    [InlineData("products")]
    public void A_link_that_is_neither_a_site_page_nor_a_web_address_is_dropped(string link)
    {
        var bar = SiteContentController.NormalizePromoBar(new PromoBarContent { ButtonLink = link });

        Assert.Equal("", bar.ButtonLink);
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("https://t.me/phoenixverify")]
    public void Site_pages_and_web_addresses_are_kept(string link)
    {
        Assert.Equal(link, SiteContentController.NormalizePromoBar(new PromoBarContent { ButtonLink = link }).ButtonLink);
    }

    [Fact]
    public void A_save_without_the_strip_gets_the_defaults_rather_than_nothing()
    {
        var store = TestStore.Create();
        var content = store.GetSiteContent();
        content.PromoBar = null!;

        new SiteContentController(store).Update(content);

        Assert.NotNull(store.GetSiteContent().PromoBar);
        Assert.True(store.GetSiteContent().PromoBar.Enabled);
    }

    [Fact]
    public void Overlong_text_is_cut_to_size()
    {
        var bar = SiteContentController.NormalizePromoBar(new PromoBarContent { Text = new string('x', 1000) });

        Assert.Equal(300, bar.Text.Length);
    }
}
