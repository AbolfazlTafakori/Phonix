using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A W-UI panel in a box: records what it is asked and answers what the test tells it to.
internal sealed class FakeWireGuardPanel : IWireGuardPanelConnector
{
    public List<(string name, long gb, int devices, int days, IReadOnlyList<int> ifaces)> Created = new();
    public List<(int clientId, WireGuardClientLimits limits)> Renewed = new();
    public List<int> Deleted = new();
    public WireGuardClientResult AddResult = new(true, null, 41, "subtok", "https://vpn.example.com:44600/subscribe/subtok", 1);
    public WireGuardRenewResult RenewResult = new(true, null, DateTimeOffset.UtcNow.AddDays(30));
    public Dictionary<int, WireGuardClientState> Snapshot = new();

    public Task<WireGuardTestResult> TestAsync(WireGuardProvider p, WireGuardCredentials c, CancellationToken ct = default) =>
        Task.FromResult(WireGuardTestResult.Succeeded(1, "test"));
    public Task<WireGuardInterfacesResult> ListInterfacesAsync(WireGuardProvider p, WireGuardCredentials c, CancellationToken ct = default) =>
        Task.FromResult(new WireGuardInterfacesResult(true, null, new List<WireGuardInterface> { new(1, "wg0", "wireguard", "standard", 51820, "vpn.example.com", true, true, 0, 0, 0, 253, "") }));
    public Task<WireGuardClientResult> AddClientAsync(WireGuardProvider p, WireGuardCredentials c, WireGuardNewClient r, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        Created.Add((r.Name, r.TotalGb, r.DeviceLimit, r.DurationDays, ids));
        return Task.FromResult(AddResult);
    }
    public Task<WireGuardClientState> GetClientAsync(WireGuardProvider p, WireGuardCredentials c, int clientId, CancellationToken ct = default) =>
        Task.FromResult(Snapshot.TryGetValue(clientId, out var s) ? s : WireGuardClientState.Gone("gone"));
    public Task<WireGuardOpResult> DeleteClientAsync(WireGuardProvider p, WireGuardCredentials c, int clientId, CancellationToken ct = default)
    {
        Deleted.Add(clientId);
        return Task.FromResult(WireGuardOpResult.Success);
    }
    public Task<WireGuardRenewResult> RenewClientAsync(WireGuardProvider p, WireGuardCredentials c, int clientId, WireGuardClientLimits limits, CancellationToken ct = default)
    {
        Renewed.Add((clientId, limits));
        return Task.FromResult(RenewResult);
    }
    public Task<WireGuardPanelSnapshot> GetSnapshotAsync(WireGuardProvider p, WireGuardCredentials c, CancellationToken ct = default) =>
        Task.FromResult(new WireGuardPanelSnapshot(true, null, Snapshot));
    public Task<WireGuardProfilesResult> GetProfilesAsync(WireGuardProvider p, WireGuardCredentials c, int clientId, CancellationToken ct = default) =>
        Task.FromResult(new WireGuardProfilesResult(true, null, new List<WireGuardProfile>()));
}

// The whole sales path for a WireGuard-linked product: the plans show up on the product, a purchase counts
// against the plan's cap and never against stock, an approved order is provisioned on the panel with exactly
// the plan's terms, a renewal extends the same customer in place, and the monitor deletes an expired one.
public class WireGuardSalesTests
{
    private sealed class NoBots : IServiceProvider { public object? GetService(Type t) => null; }

    private static (IDataStore store, int productId, int planId, int panelId) Seed(int quantity = 0)
    {
        var store = TestStore.Create();
        var category = store.AddWireGuardCategory(new WireGuardCategory { Name = "آلمان", Active = true });
        var panel = store.AddWireGuardPanel(new WireGuardPanel { Url = "https://de.example.com:2057/base", ApiToken = "wui_x", Name = "آلمان", Flag = "DE", Remark = "Germany" });
        var plan = store.AddWireGuardPlan(new WireGuardPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ", PanelId = panel.Id, InterfaceIds = new() { 1 },
            Protocol = "WireGuard", VolumeGb = 20, DurationDays = 30, DeviceLimit = 1, Price = 150_000, Active = true, Quantity = quantity,
        });
        var product = store.AddProduct(new Product
        {
            Name = "خرید اشتراک فیلترشکن", CategoryId = 1, IsActive = true, Stock = 0,
            WireGuardCategoryId = category.Id, Plans = new(),
        });
        return (store, product.Id, plan.Id, panel.Id);
    }

    private static WireGuardFulfillmentService Fulfil(IDataStore store, IWireGuardPanelConnector panel) =>
        new(store, panel, new NoBots(), NullLogger<WireGuardFulfillmentService>.Instance);

    private static Order Buy(IDataStore store, int productId, int planId, string? renewToken = null)
    {
        var info = renewToken is null ? null : new List<OrderLineInfo> { new(null, renewToken) };
        return store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true, lineInfo: info).Order!;
    }

    [Fact]
    public void A_linked_product_projects_the_catalogue_plans()
    {
        var (store, productId, planId, _) = Seed();
        var product = store.GetProduct(productId)!;

        var plan = Assert.Single(product.Plans);
        Assert.Equal(planId, plan.Id);
        Assert.Equal("۲۰ گیگ", plan.Label);
        Assert.Equal("آلمان", plan.Type);      // the category is the location the buyer picks
        Assert.Equal(1, plan.UserCount);        // devices, not IPs
        Assert.Equal(150_000, plan.Price);
        Assert.True(product.IsPanelProvisioned);
    }

    [Fact]
    public void Buying_ignores_stock_and_counts_against_the_plan_cap()
    {
        var (store, productId, planId, _) = Seed(quantity: 1);

        var order = Buy(store, productId, planId);
        Assert.Equal(OrderStatus.Preparing, order.Status);   // a stock of zero never blocked it
        Assert.Equal(1, store.GetWireGuardPlan(planId)!.Sold);

        // The cap is one; the next buyer is refused with the plan's own words.
        var second = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(second.Order);
        Assert.Contains("ظرفیت فروش", second.Error);

        // Cancelling gives the place back.
        store.CancelOrder(order.Id, "test");
        Assert.Equal(0, store.GetWireGuardPlan(planId)!.Sold);
    }

    [Fact]
    public async Task An_approved_order_is_provisioned_with_the_plan_terms()
    {
        var (store, productId, planId, panelId) = Seed();
        var panel = new FakeWireGuardPanel();
        var order = Buy(store, productId, planId);

        Assert.True(await Fulfil(store, panel).ProvisionAsync(order, order.Units[0]));

        var created = Assert.Single(panel.Created);
        Assert.Equal(20, created.gb);
        Assert.Equal(1, created.devices);
        Assert.Equal(30, created.days);
        Assert.Equal(new[] { 1 }, created.ifaces);
        Assert.StartsWith("germany-", created.name);

        var fresh = store.GetOrder(order.Id)!;
        var unit = fresh.Units[0];
        Assert.True(unit.Delivered);
        Assert.NotNull(unit.WireGuard);
        Assert.Equal(41, unit.WireGuard!.ClientId);
        Assert.Equal(panelId, unit.WireGuard.PanelId);
        Assert.Equal(32, unit.WireGuard.Token.Length);
        Assert.Contains("subscribe/subtok", unit.WireGuard.SubUrl);
        Assert.Contains("لینک اشتراک", unit.DeliveryContent);
        Assert.Equal(OrderStatus.Completed, fresh.Status);

        // The public page finds it by token, and the token lookup is exclusive to this catalogue.
        Assert.NotNull(store.FindUnitByWireGuardToken(unit.WireGuard.Token));
        Assert.Null(store.FindUnitByV2RayToken(unit.WireGuard.Token));

        // Provisioning again re-delivers instead of creating a second customer.
        var again = store.GetOrder(order.Id)!;
        Assert.True(await Fulfil(store, panel).ProvisionAsync(again, again.Units[0]) || again.Units[0].Delivered);
        Assert.Single(panel.Created);
    }

    [Fact]
    public async Task A_panel_failure_is_recorded_and_retried_rather_than_delivered()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeWireGuardPanel { AddResult = WireGuardClientResult.Fail("پنل خاموش است") };
        var order = Buy(store, productId, planId);

        Assert.False(await Fulfil(store, panel).ProvisionAsync(order, order.Units[0]));

        var unit = store.GetOrder(order.Id)!.Units[0];
        Assert.False(unit.Delivered);
        Assert.Equal(1, unit.WireGuard!.Attempts);
        Assert.Equal("پنل خاموش است", unit.WireGuard.LastError);
        Assert.Equal(32, unit.WireGuard.Token.Length);   // a token is minted once and kept across retries

        // Still in the sweep's pool.
        Assert.Contains(store.GetOrdersAwaitingV2Ray(), o => o.Id == order.Id);
    }

    [Fact]
    public async Task A_renewal_extends_the_same_customer_and_keeps_the_original_link()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeWireGuardPanel();
        var first = Buy(store, productId, planId);
        Assert.True(await Fulfil(store, panel).ProvisionAsync(first, first.Units[0]));
        var token = store.GetOrder(first.Id)!.Units[0].WireGuard!.Token;

        var renewal = Buy(store, productId, planId, renewToken: token);
        Assert.Equal(token, renewal.Units[0].WireGuardRenewToken);
        Assert.Null(renewal.Units[0].V2RayRenewToken);

        Assert.True(await Fulfil(store, panel).ProvisionAsync(renewal, renewal.Units[0]));

        Assert.Single(panel.Created);                       // no second customer
        var renewed = Assert.Single(panel.Renewed);
        Assert.Equal(41, renewed.clientId);
        Assert.Equal(20, renewed.limits.TotalGb);

        var original = store.GetOrder(first.Id)!.Units[0].WireGuard!;
        Assert.Equal(1, original.RenewCount);
        Assert.Equal(token, original.Token);
        var mirror = store.GetOrder(renewal.Id)!.Units[0].WireGuard!;
        Assert.Equal("", mirror.Token);                     // the service is still reached through the original link
        Assert.Contains("تمدید", store.GetOrder(renewal.Id)!.Units[0].DeliveryContent);
    }

    [Fact]
    public void The_monitor_decides_like_the_v2ray_one()
    {
        var alerts = new V2RayAlertSettings { ExpiryWarnHours = 48, VolumeWarnGb = 1, DeleteAfterExpiryHours = 48 };
        var now = DateTime.UtcNow;
        var svc = new WireGuardServiceRef(1, 1, "X", 5, 1, 41, "n", "t", null, 20, false, false);
        const long gb = 1024L * 1024 * 1024;

        var fine = WireGuardMonitorWorker.Decide(alerts, new WireGuardClientState(true, UsedBytes: 5 * gb, QuotaBytes: 20 * gb, ExpiresAt: now.AddDays(10)), svc, now);
        Assert.False(fine.Warns); Assert.False(fine.Delete);

        var soon = WireGuardMonitorWorker.Decide(alerts, new WireGuardClientState(true, UsedBytes: 19 * gb + gb / 2, QuotaBytes: 20 * gb, ExpiresAt: now.AddHours(10)), svc, now);
        Assert.True(soon.WarnExpiry); Assert.True(soon.WarnVolume); Assert.False(soon.Delete);

        var over = WireGuardMonitorWorker.Decide(alerts, new WireGuardClientState(true, UsedBytes: 0, QuotaBytes: 20 * gb, ExpiresAt: now.AddHours(-49)), svc, now);
        Assert.True(over.Delete);

        // Out of traffic with days left is warned about, never deleted.
        var exhausted = WireGuardMonitorWorker.Decide(alerts, new WireGuardClientState(true, UsedBytes: 20 * gb, QuotaBytes: 20 * gb, ExpiresAt: now.AddDays(5)), svc, now);
        Assert.True(exhausted.WarnVolume); Assert.False(exhausted.Delete);
    }

    [Fact]
    public async Task Services_are_listed_for_the_monitor_and_marked_when_gone()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeWireGuardPanel();
        var order = Buy(store, productId, planId);
        Assert.True(await Fulfil(store, panel).ProvisionAsync(order, order.Units[0]));

        var service = Assert.Single(store.GetWireGuardServices());
        Assert.Equal(41, service.ClientId);

        Assert.NotNull(store.ClaimWireGuardWarning(order.Id, 1, expiry: true, volume: false, "فردا", ""));
        Assert.Null(store.ClaimWireGuardWarning(order.Id, 1, expiry: true, volume: false, "فردا", ""));   // once per term

        Assert.NotNull(store.MarkWireGuardPanelDeleted(order.Id, 1, "test", notify: true));
        Assert.Empty(store.GetWireGuardServices());
    }
}
