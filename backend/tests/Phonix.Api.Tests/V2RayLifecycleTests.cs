using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A panel that answers however a test needs it to, so the rules around renewal and clean-up can be checked
// without a live 3x-ui instance. Records what it was asked to do, which is most of what these tests assert.
internal sealed class FakeV2RayPanel : IV2RayPanelConnector
{
    public List<string> Renewed = new();
    public List<string> Deleted = new();
    public V2RayOpResult RenewResult = V2RayOpResult.Success;
    public V2RayOpResult DeleteResult = V2RayOpResult.Success;
    public V2RayClientResult AddResult = new(true, null, "uuid-0001", "subid0001", 1);

    public Task<V2RayTestResult> TestAsync(V2RayProvider p, V2RayCredentials c, CancellationToken ct = default) =>
        Task.FromResult(V2RayTestResult.Succeeded(1));

    public Task<V2RayInboundsResult> ListInboundsAsync(V2RayProvider p, V2RayCredentials c, CancellationToken ct = default) =>
        Task.FromResult(new V2RayInboundsResult(true, null, new List<V2RayInbound> { new(1, "in", "vless", 443, true, 0) }));

    public Task<V2RayClientResult> AddClientAsync(V2RayProvider p, V2RayCredentials c, V2RayNewClient r, IReadOnlyList<int> ids, CancellationToken ct = default) =>
        Task.FromResult(AddResult);

    public Task<V2RayTraffic> GetTrafficAsync(V2RayProvider p, V2RayCredentials c, string email, CancellationToken ct = default) =>
        Task.FromResult(new V2RayTraffic(true));

    public Task<V2RaySubscription> GetSubscriptionAsync(string subUrl, CancellationToken ct = default) =>
        Task.FromResult(new V2RaySubscription(true));

    public Task<V2RayPanelSnapshot> GetSnapshotAsync(V2RayProvider p, V2RayCredentials c, CancellationToken ct = default) =>
        Task.FromResult(new V2RayPanelSnapshot(true, null, new Dictionary<string, V2RayClientState>(), new HashSet<string>()));

    public Task<V2RayOpResult> RenewClientAsync(V2RayProvider p, V2RayCredentials c, string email, V2RayClientLimits limits, CancellationToken ct = default)
    {
        Renewed.Add(email);
        return Task.FromResult(RenewResult);
    }

    public Task<V2RayOpResult> DeleteClientAsync(V2RayProvider p, V2RayCredentials c, string email, CancellationToken ct = default)
    {
        Deleted.Add(email);
        return Task.FromResult(DeleteResult);
    }
}

// Renewing a config has to extend the account the customer already has. Everything they hold — the
// subscription link, the config URIs, the page the link opens — is derived from that one record, so a
// renewal that created anything new would hand them a second service and silently strand the first.
public class V2RayRenewalTests
{
    private const long Gb = 1024L * 1024L * 1024L;

    private static (IDataStore store, int productId, int planId, int otherPlanId) Seed()
    {
        var store = TestStore.Create();
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "سرویس‌های ماهانه", Active = true });
        store.AddV2RayPanel(new V2RayPanel
        {
            Url = "https://nl.example.com:8080", Name = "هلند", Flag = "NL",
            SubDomain = "sub.example.com", SubPath = "sub", SubHttps = true,
        });
        // A SECOND server in the same category: renewing must refuse to move a client onto it.
        store.AddV2RayPanel(new V2RayPanel { Url = "https://de.example.com:8080", Name = "آلمان", Flag = "DE" });

        var plan = store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ", PanelId = 1, InboundIds = new() { 1 },
            Protocol = "vless", Network = "ws",
            VolumeGb = 20, DurationDays = 30, IpLimit = 2, Price = 300_000, Active = true,
        });
        var other = store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ آلمان", PanelId = 2, InboundIds = new() { 1 },
            VolumeGb = 20, DurationDays = 30, IpLimit = 2, Price = 300_000, Active = true,
        });

        var product = store.AddProduct(new Product
        {
            Name = "خرید اشتراک V2Ray", CategoryId = 1, IsActive = true,
            V2RayCategoryId = category.Id, Plans = new(),
        });
        return (store, product.Id, plan.Id, other.Id);
    }

    // Buys one service and provisions it, returning the live account.
    private static async Task<(Order order, OrderUnit unit)> BuyAsync(IDataStore store, IV2RayFulfillmentService fulfil, int productId, int planId)
    {
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;
        Assert.True(await fulfil.ProvisionAsync(order, order.Units[0]));
        var fresh = store.GetOrder(order.Id)!;
        return (fresh, fresh.Units[0]);
    }

    private static Order PlaceRenewal(IDataStore store, int productId, int planId, string token) =>
        store.PlaceOrder(
            store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true,
            lineInfo: new List<OrderLineInfo> { new(null, token) }).Order!;

    private static V2RayFulfillmentService Service(IDataStore store, IV2RayPanelConnector panel) =>
        new(store, panel, NullLogger<V2RayFulfillmentService>.Instance);

    [Fact]
    public async Task Renewing_extends_the_same_account_and_never_creates_a_second_one()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (_, bought) = await BuyAsync(store, fulfil, productId, planId);
        var original = bought.V2Ray!;
        var newExpiry = DateTimeOffset.UtcNow.AddDays(45).ToUnixTimeMilliseconds();
        panel.RenewResult = new V2RayOpResult(true, null, newExpiry);

        var renewal = PlaceRenewal(store, productId, planId, original.Token);
        Assert.Equal(original.Token, renewal.Units[0].V2RayRenewToken);
        Assert.True(await fulfil.ProvisionAsync(renewal, renewal.Units[0]));

        // The panel was asked to extend the existing client, not to add one.
        Assert.Equal(new[] { original.Email }, panel.Renewed);

        // The customer's own record moved: later expiry, one renewal counted, same identity throughout.
        var extended = store.FindUnitByV2RayToken(original.Token)!.Value.unit.V2Ray!;
        Assert.Equal(original.Uuid, extended.Uuid);
        Assert.Equal(original.SubId, extended.SubId);
        Assert.Equal(original.SubUrl, extended.SubUrl);
        Assert.Equal(1, extended.RenewCount);
        Assert.NotNull(extended.LastRenewedAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(newExpiry).UtcDateTime, extended.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_renewals_own_unit_carries_no_token_of_its_own()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (_, bought) = await BuyAsync(store, fulfil, productId, planId);
        var token = bought.V2Ray!.Token;

        var renewal = PlaceRenewal(store, productId, planId, token);
        await fulfil.ProvisionAsync(renewal, renewal.Units[0]);

        // Two units answering to one token would make the config page's lookup a coin toss, and the monitor
        // would then evaluate the same live service once per renewal it had ever had.
        var mirror = store.GetOrder(renewal.Id)!.Units[0];
        Assert.Equal("", mirror.V2Ray!.Token);
        Assert.Equal(bought.Id, store.FindUnitByV2RayToken(token)!.Value.unit.Id);
        Assert.Single(store.GetV2RayServices(), s => s.Token == token);
    }

    [Fact]
    public async Task Renewing_re_arms_the_warnings_for_the_new_term()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (order, bought) = await BuyAsync(store, fulfil, productId, planId);
        // The customer was warned during the term that just ended.
        Assert.NotNull(store.ClaimV2RayWarning(order.Id, bought.Id, expiry: true, volume: true, "۱۴۰۴/۰۵/۱۲", "۵۰۰ مگابایت"));

        var renewal = PlaceRenewal(store, productId, planId, bought.V2Ray!.Token);
        await fulfil.ProvisionAsync(renewal, renewal.Units[0]);

        // Without this a customer would be warned once, ever, about a service they keep renewing.
        var extended = store.FindUnitByV2RayToken(bought.V2Ray!.Token)!.Value.unit.V2Ray!;
        Assert.Null(extended.ExpiryWarnSentUtc);
        Assert.Null(extended.VolumeWarnSentUtc);
    }

    [Fact]
    public async Task A_plan_on_a_different_server_is_refused_rather_than_written_somewhere_else()
    {
        var (store, productId, planId, otherPlanId) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (_, bought) = await BuyAsync(store, fulfil, productId, planId);

        var renewal = PlaceRenewal(store, productId, otherPlanId, bought.V2Ray!.Token);
        Assert.False(await fulfil.ProvisionAsync(renewal, renewal.Units[0]));

        // Nothing was written to either panel, and the buyer's service is untouched.
        Assert.Empty(panel.Renewed);
        var untouched = store.FindUnitByV2RayToken(bought.V2Ray!.Token)!.Value.unit.V2Ray!;
        Assert.Equal(0, untouched.RenewCount);
    }

    [Fact]
    public async Task An_account_already_removed_from_the_panel_is_not_renewed()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (order, bought) = await BuyAsync(store, fulfil, productId, planId);
        store.MarkV2RayPanelDeleted(order.Id, bought.Id, "تست", notify: false);

        var renewal = PlaceRenewal(store, productId, planId, bought.V2Ray!.Token);
        Assert.False(await fulfil.ProvisionAsync(renewal, renewal.Units[0]));
        Assert.Empty(panel.Renewed);
    }

    [Fact]
    public async Task A_second_attempt_on_a_finished_renewal_does_not_extend_it_twice()
    {
        var (store, productId, planId, _) = Seed();
        var panel = new FakeV2RayPanel();
        var fulfil = Service(store, panel);

        var (_, bought) = await BuyAsync(store, fulfil, productId, planId);
        var renewal = PlaceRenewal(store, productId, planId, bought.V2Ray!.Token);
        await fulfil.ProvisionAsync(renewal, renewal.Units[0]);

        // The provisioning worker retries anything undelivered; one payment must buy exactly one term.
        var again = store.GetOrder(renewal.Id)!;
        await fulfil.ProvisionAsync(again, again.Units[0]);

        Assert.Single(panel.Renewed);
        Assert.Equal(1, store.FindUnitByV2RayToken(bought.V2Ray!.Token)!.Value.unit.V2Ray!.RenewCount);
    }

    [Fact]
    public async Task A_warning_is_claimed_once_however_many_sweeps_run()
    {
        var (store, productId, planId, _) = Seed();
        var fulfil = Service(store, new FakeV2RayPanel());
        var (order, bought) = await BuyAsync(store, fulfil, productId, planId);

        Assert.NotNull(store.ClaimV2RayWarning(order.Id, bought.Id, expiry: true, volume: false, "۱۴۰۴/۰۵/۱۲", ""));
        // The second sweep finds the flag already set and sends nothing.
        Assert.Null(store.ClaimV2RayWarning(order.Id, bought.Id, expiry: true, volume: false, "۱۴۰۴/۰۵/۱۲", ""));
        // The volume warning is independent and is still available to claim.
        Assert.NotNull(store.ClaimV2RayWarning(order.Id, bought.Id, expiry: true, volume: true, "۱۴۰۴/۰۵/۱۲", "۵۰۰ مگابایت"));
        Assert.Null(store.ClaimV2RayWarning(order.Id, bought.Id, expiry: true, volume: true, "۱۴۰۴/۰۵/۱۲", "۵۰۰ مگابایت"));
    }

    [Fact]
    public async Task A_removed_account_drops_out_of_the_monitors_view()
    {
        var (store, productId, planId, _) = Seed();
        var fulfil = Service(store, new FakeV2RayPanel());
        var (order, bought) = await BuyAsync(store, fulfil, productId, planId);

        Assert.Single(store.GetV2RayServices());
        Assert.NotNull(store.MarkV2RayPanelDeleted(order.Id, bought.Id, "پس از پایان مهلت", notify: true));
        Assert.Empty(store.GetV2RayServices());
        // Recording it twice would send the customer a second "your service ended" notice.
        Assert.Null(store.MarkV2RayPanelDeleted(order.Id, bought.Id, "پس از پایان مهلت", notify: true));
    }
}

// The rules the monitor acts on. Deletion is the dangerous one — it takes a working config away from a
// paying customer — so the cases that must NOT delete are pinned as carefully as the one that must.
public class V2RayMonitorRuleTests
{
    private const long Gb = 1024L * 1024L * 1024L;
    private static readonly DateTime Now = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    private static V2RayAlertSettings Alerts(int warnHours = 48, double warnGb = 1, int deleteAfter = 48) =>
        new() { Enabled = true, ExpiryWarnHours = warnHours, VolumeWarnGb = warnGb, DeleteAfterExpiryHours = deleteAfter };

    private static V2RayServiceRef Service(bool expiryWarned = false, bool volumeWarned = false) =>
        new(1, 1, "ORD-1", 5, 1, "ord-1-1", new string('a', 32), null, 20, expiryWarned, volumeWarned);

    private static V2RayClientState State(DateTime? expires, long totalGb = 20, long usedGb = 0) =>
        new("ord-1-1",
            Up: 0,
            Down: usedGb * Gb,
            Total: totalGb * Gb,
            ExpiryTimeMs: expires is DateTime e ? new DateTimeOffset(e).ToUnixTimeMilliseconds() : 0,
            Enable: true,
            LastOnlineMs: 0);

    [Fact]
    public void Warns_about_time_only_inside_the_configured_window()
    {
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(60)), Service(), Now).WarnExpiry);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(47)), Service(), Now).WarnExpiry);
        // Already over: the useful message at that point is the removal notice, not a warning.
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(-1)), Service(), Now).WarnExpiry);
        // A service with no expiry can never be close to one.
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(null), Service(), Now).WarnExpiry);
    }

    [Fact]
    public void Warns_about_volume_at_or_below_the_threshold_including_when_it_is_gone()
    {
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddDays(20), usedGb: 18), Service(), Now).WarnVolume);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddDays(20), usedGb: 19), Service(), Now).WarnVolume);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddDays(20), usedGb: 20), Service(), Now).WarnVolume);
        // Unlimited traffic has no threshold to cross.
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddDays(20), totalGb: 0, usedGb: 500), Service(), Now).WarnVolume);
    }

    [Fact]
    public void Both_warnings_falling_due_together_are_reported_as_one_verdict()
    {
        // Nothing stops them coinciding — a monthly plan often runs out of both at once — and the claim then
        // covers both, so the customer gets one message rather than two seconds apart.
        var verdict = V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(6), usedGb: 20), Service(), Now);
        Assert.True(verdict.WarnExpiry);
        Assert.True(verdict.WarnVolume);
        Assert.False(verdict.Delete);
    }

    [Fact]
    public void A_warning_already_sent_is_not_repeated()
    {
        var state = State(Now.AddHours(6), usedGb: 20);
        var verdict = V2RayMonitorWorker.Decide(Alerts(), state, Service(expiryWarned: true, volumeWarned: true), Now);
        Assert.False(verdict.Warns);
    }

    [Fact]
    public void Thresholds_of_zero_switch_each_warning_off_independently()
    {
        var state = State(Now.AddHours(6), usedGb: 20);
        Assert.False(V2RayMonitorWorker.Decide(Alerts(warnHours: 0), state, Service(), Now).WarnExpiry);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(warnHours: 0), state, Service(), Now).WarnVolume);
        Assert.False(V2RayMonitorWorker.Decide(Alerts(warnGb: 0), state, Service(), Now).WarnVolume);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(warnGb: 0), state, Service(), Now).WarnExpiry);
    }

    [Fact]
    public void Deletes_only_after_the_full_grace_period_has_passed()
    {
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(-1)), Service(), Now).Delete);
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(-47)), Service(), Now).Delete);
        Assert.True(V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(-48)), Service(), Now).Delete);
    }

    [Fact]
    public void Running_out_of_traffic_never_deletes_a_service()
    {
        // The customer still owns the days they paid for, and renewing has to give them back the very same
        // config — which it cannot do if the account has been taken off the panel.
        var exhausted = State(Now.AddDays(20), usedGb: 20);
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), exhausted, Service(), Now).Delete);

        // Not even long after it was exhausted, as long as the time is still running.
        var longExhausted = State(Now.AddDays(300), usedGb: 40);
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), longExhausted, Service(), Now).Delete);
    }

    [Fact]
    public void A_service_with_no_expiry_is_never_deleted()
    {
        Assert.False(V2RayMonitorWorker.Decide(Alerts(), State(null, usedGb: 20), Service(), Now).Delete);
    }

    [Fact]
    public void Setting_the_grace_period_to_zero_turns_removal_off_entirely()
    {
        Assert.False(V2RayMonitorWorker.Decide(Alerts(deleteAfter: 0), State(Now.AddYears(-1)), Service(), Now).Delete);
    }

    [Fact]
    public void A_service_due_for_removal_is_not_also_warned_about()
    {
        var verdict = V2RayMonitorWorker.Decide(Alerts(), State(Now.AddHours(-72), usedGb: 20), Service(), Now);
        Assert.True(verdict.Delete);
        Assert.False(verdict.Warns);
    }
}

// The thresholds are owner-set, so the store is the last line between a typo in the admin panel and a sweep
// that warns constantly or deletes on the wrong schedule.
public class V2RayAlertSettingsTests
{
    [Fact]
    public void Defaults_are_the_documented_forty_eight_hours_and_one_gigabyte()
    {
        var alerts = TestStore.Create().GetV2RayAlertSettings();
        Assert.True(alerts.Enabled);
        Assert.Equal(48, alerts.ExpiryWarnHours);
        Assert.Equal(1, alerts.VolumeWarnGb);
        Assert.Equal(48, alerts.DeleteAfterExpiryHours);
    }

    [Fact]
    public void Saved_thresholds_survive_a_restart()
    {
        var store = TestStore.Create(out var dbPath);
        store.UpdateV2RayAlertSettings(new V2RayAlertSettings
        {
            Enabled = true, ExpiryWarnHours = 24, VolumeWarnGb = 0.5, DeleteAfterExpiryHours = 72,
        });

        var reopened = TestStore.Reopen(dbPath).GetV2RayAlertSettings();
        Assert.Equal(24, reopened.ExpiryWarnHours);
        Assert.Equal(0.5, reopened.VolumeWarnGb);
        Assert.Equal(72, reopened.DeleteAfterExpiryHours);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_rather_than_stored()
    {
        var saved = TestStore.Create().UpdateV2RayAlertSettings(new V2RayAlertSettings
        {
            ExpiryWarnHours = -5, VolumeWarnGb = -1, DeleteAfterExpiryHours = 999_999,
        });
        // A negative threshold would read as "disabled" and silently stop the warnings altogether.
        Assert.Equal(0, saved.ExpiryWarnHours);
        Assert.Equal(0, saved.VolumeWarnGb);
        Assert.Equal(24 * 365, saved.DeleteAfterExpiryHours);
    }

    [Fact]
    public void Editing_the_thresholds_leaves_the_panels_and_the_catalogue_alone()
    {
        var store = TestStore.Create();
        store.AddV2RayPanel(new V2RayPanel { Url = "https://nl.example.com:8080", Name = "هلند" });
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "ماهانه" });
        store.AddV2RayPlan(new V2RayPlan { CategoryId = category.Id, Title = "۲۰ گیگ", PanelId = 1 });

        store.UpdateV2RayAlertSettings(new V2RayAlertSettings { ExpiryWarnHours = 12 });

        // All four live in one settings blob, so a careless write here would wipe the shop's servers.
        Assert.Single(store.GetV2RayPanels());
        Assert.Single(store.GetV2RayCategories());
        Assert.Single(store.GetV2RayPlans());
    }
}
