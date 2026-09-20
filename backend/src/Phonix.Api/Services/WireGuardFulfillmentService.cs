using System.Security.Cryptography;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// Creates the W-UI customer a purchase entitles the buyer to — the WireGuard twin of V2RayFulfillmentService,
// with the same shape and the same guarantees: approval fires provisioning off and never waits for the panel,
// WireGuardProvisionWorker retries what didn't land, and an already-created customer is only ever
// re-delivered, never created twice.
public interface IWireGuardFulfillmentService
{
    bool Handles(OrderUnit unit);
    Task<bool> ProvisionAsync(Order order, OrderUnit unit, CancellationToken ct = default);
    Task ProvisionOrderAsync(Order order, CancellationToken ct = default);
    Task ProvisionForTransactionAsync(Transaction tx, CancellationToken ct = default);
}

public sealed class WireGuardFulfillmentService : IWireGuardFulfillmentService
{
    private const string Actor = "سیستم (WireGuard)";
    public const int MaxAttempts = 20;
    public static readonly TimeSpan RetryAfterCap = TimeSpan.FromHours(1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly IDataStore _store;
    private readonly IWireGuardPanelConnector _connector;
    private readonly IServiceProvider _services;
    private readonly ILogger<WireGuardFulfillmentService> _logger;

    public WireGuardFulfillmentService(IDataStore store, IWireGuardPanelConnector connector, IServiceProvider services,
        ILogger<WireGuardFulfillmentService> logger)
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

                var attempts = unit.WireGuard?.Attempts ?? 0;
                if (attempts >= MaxAttempts
                    && unit.WireGuard?.LastAttemptAtUtc is DateTime last
                    && DateTime.UtcNow - last < RetryAfterCap) continue;

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
            _logger.LogWarning(ex, "WireGuard provisioning run failed for order {Code}", order.Code);
        }
    }

    public async Task ProvisionForTransactionAsync(Transaction tx, CancellationToken ct = default)
    {
        try
        {
            if (tx.Type != TxTypes.OrderPayment || tx.Status != TxStatus.Approved) return;
            if (string.IsNullOrWhiteSpace(tx.OrderCode)) return;
            var order = _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode);
            if (order is null || order.Status != OrderStatus.Preparing) return;
            await ProvisionOrderAsync(order, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WireGuard provisioning for tx #{TxId} failed", tx.Id);
        }
    }

    private async Task AnnounceAsync(int orderId, int unitId, CancellationToken ct)
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
            _logger.LogWarning(ex, "Announcing WireGuard unit {Unit} of order #{Order} failed", unitId, orderId);
        }
    }

    // A product linked to BOTH catalogues is V2Ray's (its projection runs first), so it is not ours.
    public bool Handles(OrderUnit unit) =>
        _store.GetProduct(unit.ProductId) is { V2RayCategoryId: <= 0, WireGuardCategoryId: > 0 };

    public async Task<bool> ProvisionAsync(Order order, OrderUnit unit, CancellationToken ct = default)
    {
        // See V2RayFulfillmentService.ProvisionAsync: one caller per unit, acting on the stored state.
        var gate = Locks.GetOrAdd($"{order.Id}:{unit.Id}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var fresh = _store.GetOrder(order.Id)?.Units.FirstOrDefault(u => u.Id == unit.Id) ?? unit;
            return await ProvisionLockedAsync(order, fresh, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> ProvisionLockedAsync(Order order, OrderUnit unit, CancellationToken ct)
    {
        if (unit.Delivered || unit.Rejected) return false;

        var plan = ResolvePlan(unit);
        if (plan is null)
        {
            Record(order, unit, "پلن این سفارش در کاتالوگ WireGuard پیدا نشد.");
            return false;
        }

        var panel = _store.GetWireGuardPanel(plan.PanelId);
        if (panel is null || !panel.Enabled)
        {
            Record(order, unit, "سرور این پلن در دسترس نیست.");
            return false;
        }

        if (unit.WireGuard is { ClientId: > 0 } provisioned)
        {
            _store.DeliverUnit(order.Id, unit.Id, DeliveryText(panel, provisioned), Actor);
            _logger.LogInformation("Re-delivered the existing WireGuard customer for order {Code} unit {Unit}.", order.Code, unit.Id);
            return true;
        }

        if (unit.WireGuardRenewToken is { Length: > 0 } renewToken)
            return await RenewAsync(order, unit, plan, renewToken, ct);

        var name = BuildName(order, unit, panel);

        var result = await _connector.AddClientAsync(
            panel.Provider, Creds(panel),
            new WireGuardNewClient(name, plan.VolumeGb, Math.Max(1, plan.DeviceLimit), plan.DurationDays),
            plan.InterfaceIds,
            ct);

        if (!result.Ok)
        {
            Record(order, unit, result.Error ?? "ساخت مشتری روی پنل ناموفق بود.");
            return false;
        }

        var now = DateTime.UtcNow;
        var account = new WireGuardAccount
        {
            PanelId = panel.Id,
            PlanId = plan.Id,
            ClientId = result.ClientId,
            Name = name,
            SubId = result.SubId,
            SubUrl = result.SubscriptionUrl,
            Token = unit.WireGuard?.Token is { Length: > 0 } t ? t : NewToken(),
            Protocol = plan.Protocol,
            InterfaceIds = plan.InterfaceIds.ToList(),
            VolumeGb = plan.VolumeGb,
            DurationDays = plan.DurationDays,
            DeviceLimit = Math.Max(1, plan.DeviceLimit),
            CreatedAtUtc = now,
            ExpiresAtUtc = plan.DurationDays > 0 ? now.AddDays(plan.DurationDays) : null,
            Attempts = (unit.WireGuard?.Attempts ?? 0) + 1,
            LastAttemptAtUtc = now,
            LastError = null,
        };

        _store.SetUnitWireGuard(order.Id, unit.Id, account);
        _store.DeliverUnit(order.Id, unit.Id, DeliveryText(panel, account), Actor);
        _logger.LogInformation("Provisioned a WireGuard customer for order {Code} unit {Unit} on panel {Panel}.",
            order.Code, unit.Id, panel.Id);
        return true;
    }

    private async Task<bool> RenewAsync(Order order, OrderUnit unit, WireGuardPlan plan, string renewToken, CancellationToken ct)
    {
        var target = _store.FindUnitByWireGuardToken(renewToken);
        if (target is not var (targetOrder, targetUnit) || targetUnit.WireGuard is not { ClientId: > 0 } account)
        {
            Record(order, unit, "سرویسی که قرار بود تمدید شود پیدا نشد.");
            return false;
        }
        if (account.PanelDeletedAtUtc is not null)
        {
            Record(order, unit, "این سرویس از پنل حذف شده و دیگر قابل تمدید نیست.");
            return false;
        }

        var panel = _store.GetWireGuardPanel(account.PanelId);
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
            panel.Provider, Creds(panel), account.ClientId,
            new WireGuardClientLimits(plan.VolumeGb, plan.DurationDays, Math.Max(1, plan.DeviceLimit)),
            ct);

        if (!result.Ok)
        {
            Record(order, unit, result.Error ?? "تمدید مشتری روی پنل ناموفق بود.");
            return false;
        }

        var now = DateTime.UtcNow;
        account.PlanId = plan.Id;
        account.VolumeGb = plan.VolumeGb;
        account.DurationDays = plan.DurationDays;
        account.DeviceLimit = Math.Max(1, plan.DeviceLimit);
        account.ExpiresAtUtc = result.ExpiresAt?.UtcDateTime;
        account.RenewCount += 1;
        account.LastRenewedAtUtc = now;
        account.ExpiryWarnSentUtc = null;
        account.VolumeWarnSentUtc = null;
        account.LastError = null;
        _store.SetUnitWireGuard(targetOrder.Id, targetUnit.Id, account);

        // The renewal's own unit records what it bought, with no token of its own (the service is reached
        // through the original link).
        _store.SetUnitWireGuard(order.Id, unit.Id, new WireGuardAccount
        {
            PanelId = panel.Id,
            PlanId = plan.Id,
            ClientId = account.ClientId,
            Name = account.Name,
            SubId = account.SubId,
            SubUrl = account.SubUrl,
            Token = "",
            Protocol = account.Protocol,
            InterfaceIds = account.InterfaceIds.ToList(),
            VolumeGb = plan.VolumeGb,
            DurationDays = plan.DurationDays,
            DeviceLimit = account.DeviceLimit,
            CreatedAtUtc = now,
            ExpiresAtUtc = account.ExpiresAtUtc,
            Attempts = (unit.WireGuard?.Attempts ?? 0) + 1,
        });
        _store.DeliverUnit(order.Id, unit.Id, RenewalText(panel, account), Actor);
        _logger.LogInformation("Renewed the WireGuard customer {Id} on panel {Panel} for order {Code}.",
            account.ClientId, panel.Id, order.Code);
        return true;
    }

    private WireGuardPlan? ResolvePlan(OrderUnit unit)
    {
        if (unit.PlanId is not int planId || planId <= 0) return null;
        var product = _store.GetProduct(unit.ProductId);
        if (product is null || product.V2RayCategoryId > 0 || product.WireGuardCategoryId <= 0) return null;
        return _store.GetWireGuardPlan(planId);
    }

    private void Record(Order order, OrderUnit unit, string error)
    {
        var account = unit.WireGuard
            ?? new WireGuardAccount { Token = unit.WireGuardRenewToken is { Length: > 0 } ? "" : NewToken() };
        account.Attempts += 1;
        account.LastAttemptAtUtc = DateTime.UtcNow;
        account.LastError = error;
        _store.SetUnitWireGuard(order.Id, unit.Id, account);
        _logger.LogWarning("WireGuard provisioning failed for order {Code} unit {Unit}: {Error}", order.Code, unit.Id, error);
    }

    private static WireGuardCredentials Creds(WireGuardPanel panel) =>
        new(panel.Url, panel.Username, SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken));

    // W-UI names customers freely, so the panel's remark (e.g. "Germany") is prefixed where set to keep an
    // operator's client list readable across servers; the order code keeps it unique.
    private static string BuildName(Order order, OrderUnit unit, WireGuardPanel panel)
    {
        var core = $"{order.Code}-{unit.Id}".Replace(" ", "").ToLowerInvariant();
        var remark = (panel.Remark ?? "").Trim().Replace(" ", "-").ToLowerInvariant();
        return remark.Length > 0 ? $"{remark}-{core}" : core;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string DeliveryText(WireGuardPanel panel, WireGuardAccount a)
    {
        var lines = new List<string>
        {
            $"سرور: {(string.IsNullOrWhiteSpace(panel.Name) ? "—" : panel.Name)}",
            $"نام سرویس: {a.Name}",
        };
        if (!string.IsNullOrWhiteSpace(a.SubUrl)) lines.Add($"لینک اشتراک: {a.SubUrl}");
        lines.Add(a.VolumeGb > 0 ? $"حجم: {a.VolumeGb} گیگابایت" : "حجم: نامحدود");
        lines.Add(a.DurationDays > 0 ? $"مدت: {a.DurationDays} روز" : "مدت: بدون محدودیت");
        lines.Add($"تعداد دستگاه: {Math.Max(1, a.DeviceLimit)}");
        return string.Join("\n", lines);
    }

    private static string RenewalText(WireGuardPanel panel, WireGuardAccount a)
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
