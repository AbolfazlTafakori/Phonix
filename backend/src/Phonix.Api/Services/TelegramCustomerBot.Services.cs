using Phonix.Api.Models;

namespace Phonix.Api.Services;

// «🔐 سرویس‌های من»: every V2Ray and WireGuard service this chat bought, each as a card of what it is — plan,
// dates, volume, status — with its subscription link a tap away, the page that shows live usage and the QR code,
// and renewing it right here: the same link and the same server, a new term. Renewing in the bot follows the
// purchase rules (the product opened for the bot, card-to-card with a receipt); otherwise it happens on the
// service's own page on the site.
public sealed partial class TelegramCustomerBot
{
    private const int ServicesPerPage = 8;

    private sealed record ServiceFacts(string Token, bool Ready, long VolumeGb, DateTime? Expires, DateTime? Created,
        string Protocol, int Limit, int Renewed, bool Removed, string SubUrl, string Page);

    private static ServiceFacts? FactsOf(OrderUnit u) => u.V2Ray is { Token.Length: > 0 } v
        ? new ServiceFacts(v.Token, v.Uuid.Length > 0, v.VolumeGb, v.ExpiresAtUtc, v.CreatedAtUtc,
            string.Join(" · ", new[] { v.Protocol, v.Network }.Where(x => !string.IsNullOrWhiteSpace(x))),
            v.IpLimit, v.RenewCount, v.PanelDeletedAtUtc is not null, v.SubUrl, $"/config/{v.Token}")
        : u.WireGuard is { Token.Length: > 0 } w
            ? new ServiceFacts(w.Token, w.ClientId > 0, w.VolumeGb, w.ExpiresAtUtc, w.CreatedAtUtc, "WireGuard",
                w.DeviceLimit, w.RenewCount, w.PanelDeletedAtUtc is not null, w.SubUrl, $"/wg/{w.Token}")
            : null;

    // This chat's services, newest first: what was delivered and not rejected or cancelled.
    private List<(Order Order, OrderUnit Unit)> MyServices(long chatId) =>
        Buyers(chatId).SelectMany(b => _store.GetUserOrders(b.Id))
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Units.Where(u => !u.Rejected && FactsOf(u) is not null).Select(u => (Order: o, Unit: u)))
            .OrderByDescending(s => s.Order.Id).ThenBy(s => s.Unit.Id)
            .ToList();

    private (Order Order, OrderUnit Unit)? MyService(long chatId, int orderId, int unitId) =>
        MyServices(chatId).Where(s => s.Order.Id == orderId && s.Unit.Id == unitId).Select(s => ((Order, OrderUnit)?)s).FirstOrDefault();

    private static (string Dot, string Text) ServiceState(ServiceFacts f) =>
        f.Removed ? ("⚫", "پایان یافته")
        : !f.Ready ? ("⏳", "در حال ساخت")
        : f.Expires is DateTime e && e <= DateTime.UtcNow ? ("🔴", "منقضی شده")
        : ("🟢", "فعال");

    private async Task ShowServicesAsync(string token, Screen at, int page, CancellationToken ct)
    {
        var services = MyServices(at.ChatId);
        if (services.Count == 0)
        {
            await ShowAsync(token, at, $"<b>🔐 سرویس‌های من</b>\n{Rule}\nهنوز سرویسی ندارید.",
                Inline(new[] { new[] { Btn("🚀 خرید کانفیگ", "cfg", Green) }, HomeRow() }), ct, html: true);
            return;
        }
        var pages = (services.Count + ServicesPerPage - 1) / ServicesPerPage;
        page = Math.Clamp(page, 1, pages);
        var rows = services.Skip((page - 1) * ServicesPerPage).Take(ServicesPerPage)
            .Select(s => new[]
            {
                Btn($"{ServiceState(FactsOf(s.Unit)!).Dot} {Short(s.Unit.Name, 16)} · {Short(s.Unit.Plan ?? s.Order.Code, 20)}", $"svc:v:{s.Order.Id}:{s.Unit.Id}"),
            })
            .Cast<object[]>().ToList();
        if (pages > 1)
        {
            var pager = new List<object>();
            if (page > 1) pager.Add(Btn("« قبلی", $"svc:l:{page - 1}", Blue));
            pager.Add(Btn($"صفحه {Fa(page)} از {Fa(pages)}", "noop"));
            if (page < pages) pager.Add(Btn("بعدی »", $"svc:l:{page + 1}", Blue));
            rows.Add(pager.ToArray());
        }
        rows.Add(new[] { Btn("🚀 خرید کانفیگ جدید", "cfg", Green) });
        rows.Add(HomeRow());
        await ShowAsync(token, at,
            $"<b>🔐 سرویس‌های من ({Fa(services.Count)})</b>\n{Rule}\nروی هر سرویس بزنید تا جزئیات، لینک اشتراک و تمدید را ببینید.\n\n"
            + "<i>🟢 فعال · 🔴 منقضی · ⚫ پایان یافته · ⏳ در حال ساخت</i>",
            Inline(rows), ct, html: true);
    }

    private async Task ShowServiceAsync(string token, Screen at, int orderId, int unitId, CancellationToken ct)
    {
        if (MyService(at.ChatId, orderId, unitId) is not var (order, unit) || FactsOf(unit) is not { } f)
        {
            await ShowServicesAsync(token, at, 1, ct);
            return;
        }
        var (dot, state) = ServiceState(f);
        var rows = new List<object[]>
        {
            Cell(Short(unit.Plan ?? unit.Name, 30), "📦 پلن"),
            Cell($"{dot} {state}", "📍 وضعیت"),
        };
        if (f.Created is DateTime created) rows.Add(Cell(JalaliDate.Format(created), "📅 تاریخ خرید"));
        rows.Add(Cell(f.Expires is DateTime e ? JalaliDate.Format(e) : "نامحدود", "⏳ انقضا"));
        if (f.Expires is DateTime left && left > DateTime.UtcNow && !f.Removed)
            rows.Add(Cell($"{Fa((long)Math.Ceiling((left - DateTime.UtcNow).TotalDays))} روز", "⌛ باقی‌مانده"));
        rows.Add(Cell(f.VolumeGb > 0 ? $"{Fa(f.VolumeGb)} گیگابایت" : "نامحدود", "💾 حجم"));
        rows.Add(Cell(f.Limit > 0 ? $"{Fa(f.Limit)} دستگاه" : "نامحدود", "📱 کاربر"));
        if (f.Protocol.Length > 0) rows.Add(Cell(f.Protocol, "🔌 پروتکل"));
        if (f.Renewed > 0) rows.Add(Cell($"{Fa(f.Renewed)} بار", "♻️ تمدید شده"));

        if (!f.Removed && f.Ready)
            rows.Add(new[] { Btn("🔗 لینک اشتراک", $"svc:s:{order.Id}:{unit.Id}", Blue), SiteButton("📊 مصرف و QR", f.Page, Blue) });
        if (!f.Removed) rows.Add(new[] { Btn("♻️ تمدید سرویس", $"svc:r:{order.Id}:{unit.Id}", Green) });
        rows.Add(NavRow("svc:l:1", "⬅️ سرویس‌ها"));
        await ShowAsync(token, at,
            $"<b>🔐 {H(unit.Name)}</b>\n{Rule}\nسفارش {H(order.Code)}"
            + (f.Removed ? "\n\nاین سرویس پایان یافته و از سرور حذف شده است." : "\n\nمصرف لحظه‌ای و کد QR در «📊 مصرف و QR» است."),
            Inline(rows), ct, html: true);
    }

    // The link goes as its own message, so it stays in the chat to copy again later.
    private async Task SendSubscriptionLinkAsync(string token, long chatId, int orderId, int unitId, CancellationToken ct)
    {
        if (MyService(chatId, orderId, unitId) is not var (_, unit) || FactsOf(unit) is not { Removed: false } f
            || string.IsNullOrWhiteSpace(f.SubUrl))
        {
            await ReplyAsync(token, chatId, "لینک اشتراک این سرویس هنوز آماده نیست.", ct, HomeOnly());
            return;
        }
        await ReplyAsync(token, chatId,
            $"<b>🔗 لینک اشتراک {H(unit.Name)}</b>\n{Rule}\n<code>{H(f.SubUrl.Trim())}</code>\n\n"
            + "برای کپی روی لینک بزنید و آن را در برنامه‌ی اتصال (v2rayNG، Streisand، Hiddify و…) اضافه کنید. "
            + "راهنمای نصب در «📚 آموزش‌ها» است.",
            ct, Inline(new[] { new[] { SiteButton("📊 مصرف و QR", f.Page, Blue) }, HomeRow() }), html: true);
    }

    // ── Renewing ────────────────────────────────────────────────────────────────────────────────────────────

    // The plans this service can be renewed onto: the product's, on the very server the service lives on —
    // a renewal rewrites the term of the account where it already is.
    private List<ProductPlan> RenewalPlans(OrderUnit unit, Product product) =>
        product.Plans.Where(p => p.IsActive && (unit.V2Ray is { } v
                ? product.V2RayCategoryId > 0 && _store.GetV2RayPlan(p.Id)?.PanelId == v.PanelId
                : unit.WireGuard is { } w && product.V2RayCategoryId <= 0 && _store.GetWireGuardPlan(p.Id)?.PanelId == w.PanelId))
            .ToList();

    // Why this chat can't renew the service behind this token onto this plan now, or null when it can.
    private string? RenewalBlocked(long chatId, string renewToken, int productId, int? planId)
    {
        var service = MyServices(chatId).FirstOrDefault(s => FactsOf(s.Unit)?.Token == renewToken);
        if (service.Unit is null || FactsOf(service.Unit) is not { } f) return "این سرویس پیدا نشد.";
        if (f.Removed) return "این سرویس پایان یافته و قابل تمدید نیست؛ سرویس تازه بخرید.";
        if (service.Unit.ProductId != productId || _store.GetProduct(productId) is not { IsActive: true } product)
            return "تمدید این سرویس در حال حاضر ممکن نیست.";
        if (planId is not int id || !RenewalPlans(service.Unit, product).Any(p => p.Id == id))
            return "این پلن برای تمدید این سرویس در دسترس نیست.";
        return null;
    }

    private async Task ShowRenewalsAsync(string token, Screen at, int orderId, int unitId, CancellationToken ct)
    {
        var back = NavRow($"svc:v:{orderId}:{unitId}");
        if (MyService(at.ChatId, orderId, unitId) is not var (order, unit) || FactsOf(unit) is not { } f)
        {
            await ShowServicesAsync(token, at, 1, ct);
            return;
        }
        if (f.Removed || _store.GetProduct(unit.ProductId) is not { IsActive: true } product)
        {
            await ShowAsync(token, at, "این سرویس قابل تمدید نیست؛ می‌توانید سرویس تازه بخرید.",
                Inline(new[] { new[] { Btn("🚀 خرید کانفیگ", "cfg", Green) }, back }), ct);
            return;
        }
        var plans = RenewalPlans(unit, product);
        var head = $"<b>♻️ تمدید {H(unit.Name)}</b>\n{Rule}\nلینک و سرور همین می‌ماند؛ حجم و زمان از نو شروع می‌شود.";
        // Renewed here on the same terms as buying here; anything else renews on the service's own page.
        var guestPlans = SalesOn && GuestSellable(product) ? GuestPlans(product).Select(p => p.Id).ToHashSet() : new HashSet<int>();
        var here = plans.Where(p => guestPlans.Contains(p.Id)).ToList();
        if (here.Count == 0)
        {
            await ShowAsync(token, at,
                head + (plans.Count == 0 ? "\n\nفعلاً پلنی برای تمدید این سرویس موجود نیست." : "\n\nتمدید این سرویس در صفحه‌ی خودش در سایت انجام می‌شود."),
                Inline(plans.Count == 0 ? new[] { back } : new[] { new[] { SiteButton("♻️ تمدید در سایت", f.Page, Green) }, back }), ct, html: true);
            return;
        }
        var rows = here.Select(p => new[] { Btn($"💎 {PlanName(p)} — {Toman(p.FinalPrice)}", $"svc:rp:{order.Id}:{unit.Id}:{p.Id}", Green) })
            .Cast<object[]>().Append(back);
        await ShowAsync(token, at, head + "\n\nپلن تمدید را انتخاب کنید 👇", Inline(rows), ct, html: true);
    }

    private async Task StartRenewalAsync(string token, Screen at, int orderId, int unitId, int planId, CancellationToken ct)
    {
        if (MyService(at.ChatId, orderId, unitId) is not var (order, unit) || FactsOf(unit) is not { } f)
        {
            await ShowServicesAsync(token, at, 1, ct);
            return;
        }
        if (RenewalBlocked(at.ChatId, f.Token, unit.ProductId, planId) is { } why)
        {
            await ShowAsync(token, at, why, Inline(new[] { NavRow($"svc:v:{orderId}:{unitId}") }), ct);
            return;
        }
        await StartPurchaseAsync(token, at, unit.ProductId, planId, ct, renewToken: f.Token, buyerId: order.UserId, back: $"svc:r:{orderId}:{unitId}");
    }
}
