using System.Security.Cryptography;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// Creates the V2Ray account a purchase entitles the buyer to, exactly the way the stock pool serves an
// ordinary product: the moment an order is approved, the unit is fulfilled without anyone touching it.
//
// The panel is a network hop, so provisioning is deliberately NOT part of the approval call — an unreachable
// server would otherwise fail an approval that has already taken the customer's money. Instead the approval
// FIRES OFF the attempt and returns; on the happy path the account exists a second later, and when it doesn't,
// the unit is left undelivered with a V2Ray record attached and V2RayProvisionWorker retries until the panel
// answers.
public interface IV2RayFulfillmentService
{
    // True when this unit is served from a V2Ray panel rather than the stock pool.
    bool Handles(OrderUnit unit);

    // Attempts to provision one unit. Returns true once the account exists and the unit has been delivered.
    Task<bool> ProvisionAsync(Order order, OrderUnit unit, CancellationToken ct = default);

    // Provisions every account of a just-approved order that this service handles, and posts each one to the
    // orders group as it lands. Written to be fired and forgotten from a request path: it awaits nothing the
    // caller needs and it NEVER throws, so a panel that is down costs the buyer a short wait for the worker's
    // next sweep rather than costing the approval itself.
    Task ProvisionOrderAsync(Order order, CancellationToken ct = default);

    // The approval paths' entry point: given a just-approved order payment, provision that order's V2Ray
    // accounts. Mirrors IStockFulfillmentService.AutoDeliverForTransaction, which does the same job for the
    // products served out of the stock pool.
    Task ProvisionForTransactionAsync(Transaction tx, CancellationToken ct = default);
}

public sealed class V2RayFulfillmentService : IV2RayFulfillmentService
{
    private const string Actor = "سیستم (V2Ray)";

    // How many times an account is retried before it is left for staff. At the worker's cadence this is a
    // quarter of an hour of a panel being unreachable, which is a real outage rather than a blip.
    public const int MaxAttempts = 20;

    private readonly IDataStore _store;
    private readonly IV2RayPanelConnector _connector;
    // Resolved lazily: the orders bot depends on THIS service (it asks what self-provisions), so taking it as a
    // constructor argument would close a DI cycle.
    private readonly IServiceProvider _services;
    private readonly ILogger<V2RayFulfillmentService> _logger;

    public V2RayFulfillmentService(IDataStore store, IV2RayPanelConnector connector, IServiceProvider services,
        ILogger<V2RayFulfillmentService> logger)
    {
        _store = store;
        _connector = connector;
        _services = services;
        _logger = logger;
    }

    public async Task ProvisionOrderAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            foreach (var unit in order.Units.Where(u => !u.Delivered && !u.Rejected).ToList())
            {
                if (ct.IsCancellationRequested) return;
                if (!Handles(unit)) continue;

                var attempts = unit.V2Ray?.Attempts ?? 0;
                // An account that keeps failing stops being retried and becomes a person's problem. The group
                // is told once, on the attempt that gives up, so a service nobody can create is never simply
                // missing from the channel.
                if (attempts >= MaxAttempts) continue;

                if (await ProvisionAsync(order, unit, ct))
                {
                    await AnnounceAsync(order.Id, unit.Id, ct);
                    continue;
                }
                if (attempts + 1 >= MaxAttempts) await AnnounceAsync(order.Id, unit.Id, ct);
            }
        }
        catch (Exception ex)
        {
            // The approval already succeeded and the customer already paid; a failure here is the worker's to
            // pick up on its next sweep, never the caller's to see.
            _logger.LogWarning(ex, "V2Ray provisioning run failed for order {Code}", order.Code);
        }
    }

    public async Task ProvisionForTransactionAsync(Transaction tx, CancellationToken ct = default)
    {
        try
        {
            if (tx.Type != TxTypes.OrderPayment || tx.Status != TxStatus.Approved) return;
            if (string.IsNullOrWhiteSpace(tx.OrderCode)) return;
            var order = _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode);
            // Only an order the approval actually advanced into fulfillment has anything to provision.
            if (order is null || order.Status != OrderStatus.Preparing) return;
            await ProvisionOrderAsync(order, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "V2Ray provisioning for tx #{TxId} failed", tx.Id);
        }
    }

    // Posts one finished account to the orders group. The unit is re-read because provisioning wrote to it —
    // the copy this method was handed says nothing about the service that now exists.
    public async Task AnnounceAsync(int orderId, int unitId, CancellationToken ct = default)
    {
        try
        {
            var bot = _services.GetService<ITelegramOrderService>();
            if (bot is null) return;
            if (_store.GetOrder(orderId) is not { } fresh) return;
            if (fresh.Units.FirstOrDefault(u => u.Id == unitId) is not { } unit) return;
            await bot.NotifyUnitAsync(fresh, unit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Announcing V2Ray unit {Unit} of order #{Order} failed", unitId, orderId);
        }
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
        // quietly burn a slot — or, for a renewal, extend the same service twice for one payment. Either way
        // an already-fulfilled unit is only re-delivered.
        if (unit.V2Ray is { Uuid.Length: > 0 } provisioned)
        {
            _store.DeliverUnit(order.Id, unit.Id, DeliveryText(panel, provisioned), Actor);
            _logger.LogInformation("Re-delivered the existing V2Ray account for order {Code} unit {Unit}.", order.Code, unit.Id);
            return true;
        }

        // A renewal never creates a client. It extends the one the customer already has, so their link, their
        // configs and their config page all keep working exactly as before.
        if (unit.V2RayRenewToken is { Length: > 0 } renewToken)
            return await RenewAsync(order, unit, plan, renewToken, ct);

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

    // Extends an account the buyer already owns, in place on the panel.
    //
    // Everything the customer holds stays put: the UUID, the subscription id and link, the config list, and
    // the token their config page is reachable by. What moves is the term — a fresh quota, a later expiry, the
    // plan's device limit — and the account is re-enabled, since the panel switches off a client whose traffic
    // or time has run out.
    private async Task<bool> RenewAsync(Order order, OrderUnit unit, V2RayPlan plan, string renewToken, CancellationToken ct)
    {
        var target = _store.FindUnitByV2RayToken(renewToken);
        if (target is not var (targetOrder, targetUnit) || targetUnit.V2Ray is not { Uuid.Length: > 0 } account)
        {
            Record(order, unit, "سرویسی که قرار بود تمدید شود پیدا نشد.");
            return false;
        }
        if (account.PanelDeletedAtUtc is not null)
        {
            Record(order, unit, "این سرویس از پنل حذف شده و دیگر قابل تمدید نیست.");
            return false;
        }

        // The renewal runs against the panel the ACCOUNT lives on, never the plan's — a plan pointing
        // somewhere else would otherwise write the new term onto a server that doesn't hold this client.
        // Checkout only offers same-server plans; this is the guard that makes a stale basket harmless.
        var panel = _store.GetV2RayPanel(account.PanelId);
        if (panel is null || !panel.Enabled)
        {
            Record(order, unit, "سرور این سرویس در دسترس نیست.");
            return false;
        }
        if (plan.PanelId != account.PanelId)
        {
            Record(order, unit, "پلن انتخاب‌شده برای سرور این سرویس نیست.");
            return false;
        }

        var result = await _connector.RenewClientAsync(
            panel.Provider,
            new V2RayCredentials(panel.Url, panel.Username, SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken)),
            account.Email,
            new V2RayClientLimits(plan.VolumeGb, plan.DurationDays, plan.IpLimit),
            ct);

        if (!result.Ok)
        {
            Record(order, unit, result.Error ?? "تمدید اکانت روی پنل ناموفق بود.");
            return false;
        }

        var now = DateTime.UtcNow;
        account.PlanId = plan.Id;
        account.VolumeGb = plan.VolumeGb;
        account.DurationDays = plan.DurationDays;
        account.IpLimit = plan.IpLimit;
        account.ExpiresAtUtc = result.ExpiryTimeMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(result.ExpiryTimeMs).UtcDateTime
            : null;
        account.RenewCount += 1;
        account.LastRenewedAtUtc = now;
        // A new term earns a new set of warnings; without this the customer would be told once, ever, that a
        // service they have since renewed twice is about to run out.
        account.ExpiryWarnSentUtc = null;
        account.VolumeWarnSentUtc = null;
        account.LastError = null;
        _store.SetUnitV2Ray(targetOrder.Id, targetUnit.Id, account);

        // The renewal's OWN unit records what it bought, so the order reads properly on its own — but with no
        // token of its own, because the service is still reached through the original link. A second unit
        // carrying the same token would make the config page's lookup ambiguous.
        _store.SetUnitV2Ray(order.Id, unit.Id, new V2RayAccount
        {
            PanelId = panel.Id,
            PlanId = plan.Id,
            Email = account.Email,
            Uuid = account.Uuid,
            SubId = account.SubId,
            SubUrl = account.SubUrl,
            Token = "",
            Protocol = account.Protocol,
            Network = account.Network,
            VolumeGb = plan.VolumeGb,
            DurationDays = plan.DurationDays,
            IpLimit = plan.IpLimit,
            CreatedAtUtc = now,
            ExpiresAtUtc = account.ExpiresAtUtc,
            Attempts = (unit.V2Ray?.Attempts ?? 0) + 1,
        });
        _store.DeliverUnit(order.Id, unit.Id, RenewalText(panel, account), Actor);
        _logger.LogInformation("Renewed the V2Ray account {Email} on panel {Panel} for order {Code}.",
            account.Email, panel.Id, order.Code);
        return true;
    }

    // A V2Ray product's selectable plans are the catalogue's plans, so the chosen id is a V2RayPlan id.
    //
    // The plan is NOT required to sit in the product's own linked category: every active category is a
    // location the product offers (see ApplyV2RayPlans), so a buyer picking the second server legitimately
    // lands on a plan from a different category. What still has to hold is that the product sells V2Ray at
    // all and that the plan really exists — otherwise an arbitrary id from a tampered checkout would
    // provision something nobody bought.
    private V2RayPlan? ResolvePlan(OrderUnit unit)
    {
        if (unit.PlanId is not int planId || planId <= 0) return null;
        var product = _store.GetProduct(unit.ProductId);
        if (product is null || product.V2RayCategoryId <= 0) return null;
        return _store.GetV2RayPlan(planId);
    }

    // Marks the failure on the unit so the panel shows why, and the worker can back off.
    private void Record(Order order, OrderUnit unit, string error)
    {
        // A renewal is served through the original account's link, so its unit must never mint a token of its
        // own — a second token pointing at the same service is one the config page could resolve either way.
        var account = unit.V2Ray
            ?? new V2RayAccount { Token = unit.V2RayRenewToken is { Length: > 0 } ? "" : NewToken() };
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

    // A renewal's order entry. It says plainly that nothing has to be re-imported — the single most common
    // question after one, since the customer's app shows no change until it next refreshes the link.
    private static string RenewalText(V2RayPanel panel, V2RayAccount a)
    {
        var lines = new List<string>
        {
            "این خرید، تمدید سرویس قبلی شماست؛ لینک و کانفیگ‌های شما تغییری نکرده است.",
            $"سرور: {(string.IsNullOrWhiteSpace(panel.Name) ? "—" : panel.Name)}",
        };
        if (!string.IsNullOrWhiteSpace(a.SubUrl)) lines.Add($"لینک اشتراک: {a.SubUrl}");
        lines.Add(a.VolumeGb > 0 ? $"حجم جدید: {a.VolumeGb} گیگابایت" : "حجم جدید: نامحدود");
        lines.Add(a.ExpiresAtUtc is DateTime e
            ? $"اعتبار تا: {JalaliDate.Format(e)}"
            : "اعتبار: بدون محدودیت زمانی");
        return string.Join("\n", lines);
    }
}
