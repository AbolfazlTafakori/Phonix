using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// One seat of one DELIVERED account, as staff see it: which order and buyer it belongs to, which block of
// which shared account it is, and whatever the buyer has filed (or not) for it.
//
// The seat identity is the position in the delivered content — exactly what the customer's panel derives when
// it lists the profiles — so a row here and a submission the buyer saved address the same seat.
public sealed record SeatRow(
    int OrderId,
    string OrderCode,
    // The order's own state: a cancelled purchase is still searchable (staff look old cases up) but nothing
    // is ever chased on it.
    OrderStatus OrderStatus,
    int UserId,
    string UserName,
    string? UserEmail,
    string? UserPhone,
    int UnitId,
    int UnitIndex,
    int UnitCount,
    int ProductId,
    string ProductName,
    string? Plan,
    int SeatIndex,
    string SeatLabel,
    // The shared account this seat sits on, i.e. the login the block was delivered with. Empty for a
    // delivery that isn't in the seat format.
    string AccountUsername,
    string? DeliveredAt,
    // A delivery recorded on the ORDER rather than on an account of it — the shape orders had before a
    // purchase was split into accounts. UnitId is 0 for these, and index 0 addresses the whole thing.
    bool LegacyOrderDelivery,
    // Whether the plan this unit was sold under asks the buyer for per-seat details at all, and the wording
    // it asks with. A seat that collects nothing can never be «ثبت نشده».
    bool CollectsInfo,
    string SeatInfoHint,
    // What the buyer entered for this account at checkout (device model and the like), decrypted — the other
    // place staff look for "whose device is this".
    IReadOnlyList<OrderInputValue> DeviceInfo,
    SeatSubmission? Submission);

// One seat as it was delivered: its position in the content, its own label («A - 8»), and the account it is a
// block of.
public sealed record DeliveredSeat(int Index, string Label, string AccountUsername);

public interface ISeatRosterService
{
    // Every seat of every delivered account, newest order first.
    IReadOnlyList<SeatRow> All();
    // Seats matching a free-text term: order code, buyer name/email/phone, product, plan, seat label, the
    // shared account's username, what the buyer filed, and what they entered at checkout.
    IReadOnlyList<SeatRow> Search(string term);
    // Seats that collect details and are waiting on the BUYER: nothing ever filed, or staff reopened the
    // entry for a correction and the buyer has not saved since.
    IReadOnlyList<SeatRow> Unregistered();
    // Seats taken out of warranty, still out.
    IReadOnlyList<SeatRow> Revoked();
    // The seats of one delivered unit (used to offer «which user should be banned» on a shared account).
    IReadOnlyList<SeatRow> ForUnit(int orderId, int unitId);
    // The plan a delivered unit was sold under, when that plan collects per-seat details (else null).
    ProductPlan? SeatInfoPlan(Order order, OrderUnit unit);
    // Seats of one unit as the delivery content itself defines them.
    IReadOnlyList<DeliveredSeat> SeatsOf(OrderUnit unit);
}

public sealed class SeatRosterService : ISeatRosterService
{
    private readonly IDataStore _store;
    public SeatRosterService(IDataStore store) => _store = store;

    // Which plan's terms govern this delivered unit, when that plan collects per-seat details.
    //
    // An order sold a year ago still has to answer this, and its plan may well have been edited, renamed or
    // deleted since — so the unit's own plan is looked for three ways, narrowest first:
    //   1. by the plan id the unit recorded (exact, and immune to every later rename),
    //   2. by the plan's display shape — type + subscription months — which is what older units carry,
    //   3. and when the unit's plan is simply GONE, by any plan of the product that collects details.
    // The third step is a fallback for a missing plan only: a plan that is still there and deliberately says
    // «this one collects nothing» is honoured as the no it is.
    public ProductPlan? SeatInfoPlan(Order order, OrderUnit unit)
    {
        if (_store.GetProduct(unit.ProductId) is not { } product) return null;
        var type = StockFulfillmentService.PlanType(unit.Plan);
        var months = StockFulfillmentService.SubscriptionMonths(order, unit);

        if (unit.PlanId is int planId && product.Plans.FirstOrDefault(pl => pl.Id == planId) is { } byId)
            return byId.CollectSeatInfo ? byId : null;
        if (product.Plans.FirstOrDefault(pl => pl.Type == type && (months <= 0 || pl.Months == months)) is { } byShape)
            return byShape.CollectSeatInfo ? byShape : null;

        // The plan this was sold under no longer exists. The product's own current answer is the best one
        // available, and it is what keeps a years-old purchase inside the rules rather than invisible to them.
        return product.Plans.FirstOrDefault(pl => pl.CollectSeatInfo && pl.Type == type)
               ?? product.Plans.FirstOrDefault(pl => pl.CollectSeatInfo);
    }

    public IReadOnlyList<DeliveredSeat> SeatsOf(OrderUnit unit) => SeatContent.Parse(unit.DeliveryContent);

    public IReadOnlyList<SeatRow> All() => Build(_store.GetOrders());

    public IReadOnlyList<SeatRow> ForUnit(int orderId, int unitId)
    {
        var order = _store.GetOrder(orderId);
        if (order is null) return Array.Empty<SeatRow>();
        return Build(new[] { order }).Where(r => r.UnitId == unitId).ToList();
    }

    public IReadOnlyList<SeatRow> Unregistered() =>
        All().Where(r => r.CollectsInfo && Live(r) && WaitingOnBuyer(r.Submission)).ToList();

    // A purchase that was cancelled (and refunded) is no longer a service anyone owes details for, so it stays
    // out of the chase list — it is still findable by search, where staff look old cases up.
    private static bool Live(SeatRow r) => r.OrderStatus != OrderStatus.Cancelled;

    public IReadOnlyList<SeatRow> Revoked() =>
        All().Where(r => r.Submission?.Revoked == true).ToList();

    // Waiting on the buyer: no entry at all, a staff-side stub with nothing filed, or an entry staff handed
    // back for a correction (ReopenedForEdit, which the buyer's next save clears) that they never saved again.
    // Two exclusions: a seat out of warranty — it has its own section and nothing is expected of the buyer
    // while it is closed — and a REJECTED one, which is in the review queue's own «رد شده» list.
    private static bool WaitingOnBuyer(SeatSubmission? s) =>
        s is null
        || (!s.Revoked && s.Status != SeatSubmissionStatus.Rejected && (!s.Filed || s.Editable));

    public IReadOnlyList<SeatRow> Search(string term)
    {
        term = (term ?? "").Trim();
        if (term.Length == 0) return Array.Empty<SeatRow>();
        // Staff type numbers on whichever keyboard they have, so «۱۲۳» has to find "123".
        var digits = InputValidation.DigitsOnly(term);
        return All().Where(r => Matches(r, term, digits)).ToList();
    }

    private static bool Matches(SeatRow r, string term, string digits)
    {
        bool Has(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value!.Contains(term, StringComparison.OrdinalIgnoreCase);

        if (Has(r.OrderCode) || Has(r.UserName) || Has(r.UserEmail) || Has(r.ProductName) || Has(r.Plan)
            || Has(r.SeatLabel) || Has(r.AccountUsername) || Has(r.Submission?.Text)
            || Has(r.Submission?.ReviewNote) || Has(r.Submission?.RevokeReason))
            return true;
        // A phone is stored in one shape and typed in another, so it is matched digit-to-digit.
        if (digits.Length > 2 && InputValidation.DigitsOnly(r.UserPhone ?? "").Contains(digits, StringComparison.Ordinal))
            return true;
        if (digits.Length > 0 && InputValidation.DigitsOnly(r.OrderCode).Contains(digits, StringComparison.Ordinal))
            return true;
        // The device the buyer named at checkout: both the label and the value, so «iPhone» and «مدل دستگاه»
        // both land.
        return r.DeviceInfo.Any(d => Has(d.Label) || Has(d.Value));
    }

    // Walks the orders into one row per delivered seat. Submissions are read per unit, which is one indexed
    // lookup per delivered account rather than a scan of the whole table.
    private List<SeatRow> Build(IEnumerable<Order> orders)
    {
        var rows = new List<SeatRow>();
        var users = new Dictionary<int, AppUser?>();

        foreach (var order in orders.OrderByDescending(o => o.Id))
        {
            var delivered = DeliveredPackages(order);
            if (delivered.Count == 0) continue;

            if (!users.TryGetValue(order.UserId, out var user))
                users[order.UserId] = user = _store.GetUser(order.UserId);

            foreach (var (unit, legacy) in delivered)
            {
                var plan = SeatInfoPlan(order, unit);
                var filed = _store.GetSeatSubmissionsForUnit(order.Id, unit.Id);
                var deviceInfo = DeviceInfoOf(order, unit);

                foreach (var seat in SeatContent.ParseOrWhole(unit.DeliveryContent))
                    rows.Add(new SeatRow(
                        order.Id, order.Code, order.Status, order.UserId, order.UserName, user?.Email, user?.Phone,
                        unit.Id, unit.UnitIndex, Math.Max(1, order.Units.Count), unit.ProductId, unit.Name, unit.Plan,
                        seat.Index, seat.Label, seat.AccountUsername, unit.DeliveredAt, legacy,
                        plan is not null, plan?.SeatInfoHint ?? "", deviceInfo,
                        filed.FirstOrDefault(s => s.SeatIndex == seat.Index)));
            }
        }
        return rows;
    }

    // Everything of this order that has actually been handed to the buyer, as units.
    //
    // Normally that is the order's own accounts. An order from before a purchase was split into accounts
    // recorded its delivery on the ORDER instead, and those buyers hold a live service just the same — so the
    // order-level content is served here as ONE unit with id 0, which is how staff address it (and how a
    // warranty decision on it is stored) without a second code path for old data.
    internal static List<(OrderUnit Unit, bool Legacy)> DeliveredPackages(Order order)
    {
        var units = order.Units
            .Where(u => u.Delivered && !u.Rejected && !string.IsNullOrWhiteSpace(u.DeliveryContent))
            .Select(u => (u, false))
            .ToList();
        if (units.Count > 0 || string.IsNullOrWhiteSpace(order.DeliveryContent)) return units;

        var item = order.Items.FirstOrDefault();
        return new List<(OrderUnit, bool)>
        {
            (new OrderUnit
            {
                Id = 0,
                ProductId = item?.ProductId ?? 0,
                Name = item?.Name ?? "",
                Plan = item?.Plan,
                PlanId = item?.PlanId,
                UnitIndex = 1,
                Delivered = true,
                DeliveredAt = order.DeliveredAt,
                DeliveryContent = order.DeliveryContent,
                CustomerInputs = item?.CustomerInputs ?? new(),
            }, true),
        };
    }

    // What the buyer typed for this account at checkout, decrypted. Older orders captured it on the order LINE
    // rather than on the account, so the line is the fallback — otherwise the device an old buyer named would
    // be invisible to the very search meant to find it.
    private static List<OrderInputValue> DeviceInfoOf(Order order, OrderUnit unit)
    {
        var source = unit.CustomerInputs.Count > 0
            ? unit.CustomerInputs
            : order.Items.FirstOrDefault(i => i.ProductId == unit.ProductId)?.CustomerInputs ?? new();
        return source
            .Select(v => new OrderInputValue
            {
                Label = v.Label,
                Value = v.Sensitive ? SensitiveField.Reveal(v.Value) : v.Value,
                Sensitive = v.Sensitive,
            })
            .ToList();
    }
}
