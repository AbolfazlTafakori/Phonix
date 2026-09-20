using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The W-UI (WireGuard) panel wiring mirrors the V2Ray one; these pin the parts that are pure and the parts
// that would strand an operator if they regressed: URL acceptance, the unit conversions the connector sends
// to the panel, credentials never sitting in plaintext, blank-on-update keeping a credential, the plan
// catalogue's cascade on category delete, and the backup section carrying the panels along.
public class WireGuardUrlTests
{
    [Theory]
    [InlineData("https://203.0.113.5:41873/t7GhP3nLqZ8xWc2vRy", "https://203.0.113.5:41873/t7GhP3nLqZ8xWc2vRy")]
    [InlineData("https://vpn.example.com:2096/", "https://vpn.example.com:2096")]     // trailing slash trimmed
    [InlineData("http://vpn.example.com:8080", "http://vpn.example.com:8080")]
    [InlineData("  https://vpn.example.com  ", "https://vpn.example.com")]           // trimmed
    public void Accepts_the_documented_url_shapes(string input, string expected)
    {
        Assert.Equal(expected, IWireGuardPanelConnector.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vpn.example.com:41873")]  // no scheme
    [InlineData("ftp://vpn.example.com")]  // wrong scheme
    [InlineData("javascript:alert(1)")]    // not http(s)
    [InlineData("/only/a/path")]           // not absolute
    public void Rejects_anything_that_is_not_a_usable_http_url(string input)
    {
        Assert.Null(IWireGuardPanelConnector.NormalizeUrl(input));
    }
}

public class WireGuardClientMathTests
{
    [Fact]
    public void Zero_traffic_stays_unlimited()
    {
        Assert.Equal(0, IWireGuardPanelConnector.GbToBytes(0));
        Assert.Equal(0, IWireGuardPanelConnector.GbToBytes(-5));
    }

    [Fact]
    public void Gb_converts_to_bytes()
    {
        Assert.Equal(1024L * 1024 * 1024, IWireGuardPanelConnector.GbToBytes(1));
        Assert.Equal(50L * 1024 * 1024 * 1024, IWireGuardPanelConnector.GbToBytes(50));
    }

    [Fact]
    public void Zero_or_negative_duration_never_expires()
    {
        Assert.Null(IWireGuardPanelConnector.ExpiryFromNow(0));
        Assert.Null(IWireGuardPanelConnector.ExpiryFromNow(-3));
    }

    [Fact]
    public void Duration_maps_to_a_fixed_expiry_that_many_days_out()
    {
        var before = DateTimeOffset.UtcNow.AddDays(30);
        var expiry = IWireGuardPanelConnector.ExpiryFromNow(30)!.Value;
        var after = DateTimeOffset.UtcNow.AddDays(30);
        Assert.InRange(expiry, before, after);
    }

    [Fact]
    public void A_plan_price_applies_its_own_discount()
    {
        var plan = new WireGuardPlan { Price = 100_000, DiscountPercent = 25 };
        Assert.Equal(75_000, plan.FinalPrice);
        plan.DiscountPercent = 0;
        Assert.Equal(100_000, plan.FinalPrice);
        plan.DiscountPercent = 101; // out of range → ignored rather than a negative price
        Assert.Equal(100_000, plan.FinalPrice);
    }

    [Fact]
    public void A_capped_plan_sells_out_when_sold_reaches_quantity()
    {
        var plan = new WireGuardPlan { Quantity = 2, Sold = 1 };
        Assert.False(plan.SoldOut);
        plan.Sold = 2;
        Assert.True(plan.SoldOut);
        plan.Quantity = 0; // no cap
        Assert.False(plan.SoldOut);
    }
}

public class WireGuardStoreTests
{
    private static SqliteDataStore FreshStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"));
    }

    private static WireGuardPanel Seed(IDataStore store) => store.AddWireGuardPanel(new WireGuardPanel
    {
        Url = "https://203.0.113.5:41873/base", Username = "admin", Password = "panel-secret", ApiToken = "wui_abc",
        Name = "آلمان", Flag = "DE",
    });

    [Fact]
    public void Panels_are_stored_encrypted_and_read_back_decrypted()
    {
        var store = FreshStore();
        var saved = Seed(store);
        Assert.Equal(1, saved.Id);

        var panel = Assert.Single(store.GetWireGuardPanels());
        Assert.Equal("panel-secret", panel.Password);
        Assert.Equal("wui_abc", panel.ApiToken);

        // What actually sits in the singleton is ciphertext.
        var raw = store.DeserializeSnapshot(store.SerializeSection(BackupSection.V2Ray))!.WireGuard!.Panels.Single();
        Assert.NotEqual("panel-secret", raw.Password);
        Assert.Equal("panel-secret", SensitiveField.Reveal(raw.Password));
    }

    [Fact]
    public void Blank_credentials_on_update_keep_the_stored_ones()
    {
        var store = FreshStore();
        var saved = Seed(store);

        // The controller resolves blanks to the existing values before calling the store; the store itself
        // must also honour an explicit carry-over, so an edit of only the name never rotates a credential.
        var current = store.GetWireGuardPanel(saved.Id)!;
        var updated = store.UpdateWireGuardPanel(saved.Id, new WireGuardPanel
        {
            Url = current.Url, Username = current.Username, Password = current.Password, ApiToken = current.ApiToken,
            Name = "آلمان ۲", Flag = "DE",
        })!;
        Assert.Equal("آلمان ۲", updated.Name);
        Assert.Equal("panel-secret", store.GetWireGuardPanel(saved.Id)!.Password);
        Assert.Equal("wui_abc", store.GetWireGuardPanel(saved.Id)!.ApiToken);
    }

    [Fact]
    public void A_check_records_status_and_keeps_the_last_good_interface_count_on_failure()
    {
        var store = FreshStore();
        var saved = Seed(store);

        store.RecordWireGuardPanelCheck(saved.Id, true, "", 3, "1.4.0");
        var ok = store.GetWireGuardPanel(saved.Id)!;
        Assert.True(ok.LastCheckOk);
        Assert.Equal(3, ok.InterfaceCount);
        Assert.Equal("1.4.0", ok.PanelVersion);

        store.RecordWireGuardPanelCheck(saved.Id, false, "timeout", 0, "");
        var bad = store.GetWireGuardPanel(saved.Id)!;
        Assert.False(bad.LastCheckOk);
        Assert.Equal("timeout", bad.LastCheckError);
        Assert.Equal(3, bad.InterfaceCount); // the last count seen, not zero
        Assert.Equal("1.4.0", bad.PanelVersion);
    }

    [Fact]
    public void Deleting_a_category_takes_its_plans_with_it()
    {
        var store = FreshStore();
        var panel = Seed(store);
        var keep = store.AddWireGuardCategory(new WireGuardCategory { Name = "ماهانه" });
        var gone = store.AddWireGuardCategory(new WireGuardCategory { Name = "سالانه" });
        store.AddWireGuardPlan(new WireGuardPlan { CategoryId = keep.Id, PanelId = panel.Id, InterfaceIds = { 1 }, Title = "۱۰ گیگ" });
        store.AddWireGuardPlan(new WireGuardPlan { CategoryId = gone.Id, PanelId = panel.Id, InterfaceIds = { 1 }, Title = "۱۰۰ گیگ" });

        Assert.True(store.DeleteWireGuardCategory(gone.Id));

        var plan = Assert.Single(store.GetWireGuardPlans());
        Assert.Equal(keep.Id, plan.CategoryId);
    }

    [Fact]
    public void Panels_and_plans_travel_with_the_v2ray_backup_section()
    {
        var a = FreshStore();
        var panel = Seed(a);
        var category = a.AddWireGuardCategory(new WireGuardCategory { Name = "ماهانه" });
        a.AddWireGuardPlan(new WireGuardPlan { CategoryId = category.Id, PanelId = panel.Id, InterfaceIds = { 1, 2 }, Title = "۱۰ گیگ", DeviceLimit = 2 });

        var b = FreshStore();
        b.RestoreSection(BackupSection.V2Ray, b.DeserializeSnapshot(a.SerializeSection(BackupSection.V2Ray))!);

        Assert.Equal("panel-secret", Assert.Single(b.GetWireGuardPanels()).Password);
        Assert.Single(b.GetWireGuardCategories());
        var plan = Assert.Single(b.GetWireGuardPlans());
        Assert.Equal(new[] { 1, 2 }, plan.InterfaceIds);
        Assert.Equal(2, plan.DeviceLimit);
    }

    [Fact]
    public void Restoring_a_backup_taken_before_wireguard_existed_leaves_it_alone()
    {
        var store = FreshStore();
        Seed(store);

        var legacy = store.DeserializeSnapshot(store.SerializeSnapshot())!;
        legacy.WireGuard = null;
        store.LoadSnapshot(legacy);

        Assert.Single(store.GetWireGuardPanels());
    }
}
