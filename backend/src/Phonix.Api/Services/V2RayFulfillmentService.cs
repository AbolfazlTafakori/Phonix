using System.Security.Cryptography;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// Creates the V2Ray account a purchase entitles the buyer to, exactly the way the stock pool serves an
// ordinary product: the moment an order is approved, the unit is fulfilled without anyone touching it.
//
// The panel is a network hop, so provisioning is deliberately NOT part of the approval call — an unreachable
// server would otherwise fail an approval that has already taken the customer's money. Instead the unit is
// left undelivered with a V2Ray record attached, and V2RayProvisionWorker retries it until the panel answers.
public interface IV2RayFulfillmentService
{
    // True when this unit is served from a V2Ray panel rather than the stock pool.
    bool Handles(OrderUnit unit);

    // Attempts to provision one unit. Returns true once the account exists and the unit has been delivered.
    Task<bool> ProvisionAsync(Order order, OrderUnit unit, CancellationToken ct = default);
}

public sealed class V2RayFulfillmentService : IV2RayFulfillmentService
{
    private const string Actor = "سیستم (V2Ray)";

    private readonly IDataStore _store;
    private readonly IV2RayPanelConnector _connector;
    private readonly ILogger<V2RayFulfillmentService> _logger;

    public V2RayFulfillmentService(IDataStore store, IV2RayPanelConnector connector, ILogger<V2RayFulfillmentService> logger)
    {
        _store = store;
        _connector = connector;
        _logger = logger;
    }

    public bool Handles(OrderUnit unit) =>
        _store.GetProduct(unit.ProductId) is { V2RayCategoryId: > 0 };

    public async Task<bool> ProvisionAsync(Order order, OrderUnit unit, CancellationToken ct = default)
    {
        if (unit.Delivered || unit.Rejected) return false;

        var plan = ResolvePlan(unit);
        if (plan is null)
        {
            Record(order, unit, "پلن این سفارش در کاتالوگ V2Ray پیدا نشد.");
            return false;
        }

        var panel = _store.GetV2RayPanel(plan.PanelId);
        if (panel is null || !panel.Enabled)
        {
            Record(order, unit, "سرور این پلن در دسترس نیست.");
            return false;
        }

        // The account may already exist: the panel call can succeed and the delivery that follows still fail
        // (a crash, a lost write). Retrying the panel call then would add a SECOND client for one purchase and
        // quietly burn a slot, so an already-provisioned unit is only re-delivered.
        if (unit.V2Ray is { Uuid.Length: > 0 } provisioned)
        {
            _store.DeliverUnit(order.Id, unit.Id, DeliveryText(panel, provisioned), Actor);
            _logger.LogInformation("Re-delivered the existing V2Ray account for order {Code} unit {Unit}.", order.Code, unit.Id);
            return true;
        }

        var email = BuildEmail(order, unit);

        var result = await _connector.AddClientAsync(
            panel.Provider,
            new V2RayCredentials(panel.Url, panel.Username, SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken)),
            new V2RayNewClient(email, plan.VolumeGb, plan.IpLimit, plan.DurationDays),
            plan.InboundIds,
            ct);

        if (!result.Ok)
        {
            Record(order, unit, result.Error ?? "ساخت اکانت روی پنل ناموفق بود.");
            return false;
        }

        var now = DateTime.UtcNow;
        var account = new V2RayAccount
        {
            PanelId = panel.Id,
            PlanId = plan.Id,
            Email = email,
            Uuid = result.Uuid,
            SubId = result.SubId,
            SubUrl = panel.SubscriptionUrl(result.SubId),
            // A token already handed out (a failed earlier attempt that still recorded one) is kept, so a link
            // the buyer may already hold never goes dead.
            Token = unit.V2Ray?.Token is { Length: > 0 } t ? t : NewToken(),
            Protocol = plan.Protocol,
            Network = plan.Network,
            VolumeGb = plan.VolumeGb,
            DurationDays = plan.DurationDays,
            IpLimit = plan.IpLimit,
            CreatedAtUtc = now,
            ExpiresAtUtc = plan.DurationDays > 0 ? now.AddDays(plan.DurationDays) : null,
            Attempts = (unit.V2Ray?.Attempts ?? 0) + 1,
            LastError = null,
        };

        _store.SetUnitV2Ray(order.Id, unit.Id, account);
        _store.DeliverUnit(order.Id, unit.Id, DeliveryText(panel, account), Actor);
        _logger.LogInformation("Provisioned a V2Ray account for order {Code} unit {Unit} on panel {Panel}.",
            order.Code, unit.Id, panel.Id);
        return true;
    }

    // A V2Ray product's selectable plans ARE the linked category's plans, so the chosen id is a V2RayPlan id.
    private V2RayPlan? ResolvePlan(OrderUnit unit)
    {
        if (unit.PlanId is not int planId || planId <= 0) return null;
        var product = _store.GetProduct(unit.ProductId);
        if (product is null || product.V2RayCategoryId <= 0) return null;
        var plan = _store.GetV2RayPlan(planId);
        return plan is not null && plan.CategoryId == product.V2RayCategoryId ? plan : null;
    }

    // Marks the failure on the unit so the panel shows why, and the worker can back off.
    private void Record(Order order, OrderUnit unit, string error)
    {
        var account = unit.V2Ray ?? new V2RayAccount { Token = NewToken() };
        account.Attempts += 1;
        account.LastError = error;
        _store.SetUnitV2Ray(order.Id, unit.Id, account);
        _logger.LogWarning("V2Ray provisioning failed for order {Code} unit {Unit}: {Error}", order.Code, unit.Id, error);
    }

    // The panel identifies clients by this name, so it has to be unique per account and stay readable to an
    // operator scanning the panel's client list.
    private static string BuildEmail(Order order, OrderUnit unit) =>
        $"{order.Code}-{unit.Id}".Replace(" ", "").ToLowerInvariant();

    // 32 hex chars from a cryptographic RNG: the config page is reachable by this token alone, so it must be
    // impossible to guess or to enumerate from a neighbouring order.
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // What the customer sees in the order itself. The full details live on the config page; this keeps the
    // order view useful on its own (and readable in the Telegram/email copies).
    private static string DeliveryText(V2RayPanel panel, V2RayAccount a)
    {
        var lines = new List<string>
        {
            $"سرور: {(string.IsNullOrWhiteSpace(panel.Name) ? "—" : panel.Name)}",
            $"شناسه اکانت: {a.Uuid}",
        };
        if (!string.IsNullOrWhiteSpace(a.SubUrl)) lines.Add($"لینک اشتراک: {a.SubUrl}");
        lines.Add(a.VolumeGb > 0 ? $"حجم: {a.VolumeGb} گیگابایت" : "حجم: نامحدود");
        lines.Add(a.DurationDays > 0 ? $"مدت: {a.DurationDays} روز" : "مدت: بدون محدودیت");
        return string.Join("\n", lines);
    }
}
