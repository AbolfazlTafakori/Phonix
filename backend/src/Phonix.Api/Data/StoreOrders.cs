using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record PlaceOrderResult(Order? Order, string? Error);
public record OrderActionResult(Order? Order, string? Error);
// the card-to-card payment details for an order's gateway remainder (collected at checkout).
public record RemainderPayment(int? CardId, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);
// Customer info for a single account/unit, captured at checkout (already validated + sensitive values
// encrypted by the controller).
public record OrderUnitInfo(List<OrderInputValue> Inputs, string? Note);
// Per-line customer info captured at checkout: one entry per account the customer is buying on that line.
// Aligned by position to the `items` sequence passed to PlaceOrder; entries may be null.
public record OrderLineInfo(IReadOnlyList<OrderUnitInfo>? Units);

// A subscription due for a renewal reminder. ExpiresFa is the Jalali expiry date for display. (Top-level so
// the IDataStore contract doesn't have to reference a type nested inside the concrete StoreData class.)
public sealed record RenewalReminder(int UserId, string Email, string OrderCode, string ExpiresFa);

public partial class StoreData
{
    private readonly List<Order> _orders = new();
    private readonly List<Ticket> _tickets = new();
    private readonly List<ReferralEarning> _referralEarnings = new();
    private int _orderSeq;
    private int _ticketSeq;

    // orders

    public IReadOnlyList<Order> GetOrders(OrderStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<Order> q = _orders;
            if (status is OrderStatus s) q = q.Where(o => o.Status == s);
            return q.OrderByDescending(o => o.Id).ToList();
        }
    }

    public IReadOnlyList<Order> GetUserOrders(int userId)
    {
        lock (_gate) return _orders.Where(o => o.UserId == userId).OrderByDescending(o => o.Id).ToList();
    }

    public Order? GetOrder(int id)
    {
        lock (_gate) return _orders.FirstOrDefault(o => o.Id == id);
    }

    // The per-user "orders count" and "total spent" shown in the admin panel are derived from the
    // orders themselves (the single source of truth) and recomputed on every change, so they can never
    // drift the way a hand-maintained counter does. Callers below already hold _gate.
    private void ApplyUserOrderStats(AppUser user)
    {
        user.Orders = _orders.Count(o => o.UserId == user.Id && o.Status != OrderStatus.Cancelled);
        user.TotalSpent = _orders.Where(o => o.UserId == user.Id && o.Status == OrderStatus.Completed).Sum(o => o.Total);
    }

    private void RefreshUserOrderStats(int userId)
    {
        var user = UserById(userId);
        if (user is not null) ApplyUserOrderStats(user);
    }

    // Recomputes every user's stats. Used right after seeding and after loading a snapshot, since an
    // older store.json may carry historical drift that we want to heal on startup.
    public void RefreshAllUserOrderStats()
    {
        lock (_gate)
            foreach (var u in _users) ApplyUserOrderStats(u);
    }

    // Placing an order moves money (debits the wallet for the covered part) and decrements stock. Persist
    // synchronously once it succeeds so neither the charge nor the stock decrement can be lost on a crash.
    public PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items, string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null, RemainderPayment? payment = null, bool customerCheckout = false, IReadOnlyList<OrderLineInfo>? lineInfo = null)
    {
        var result = PlaceOrderCore(user, items, paymentMethod, fromWallet, discountCode, paymentMethodId, payment, customerCheckout, lineInfo);
        if (result.Error is null) PersistNow();
        return result;
    }

    private PlaceOrderResult PlaceOrderCore(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items, string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null, RemainderPayment? payment = null, bool customerCheckout = false, IReadOnlyList<OrderLineInfo>? lineInfo = null)
    {
        lock (_gate)
        {
            var lines = new List<OrderItem>();
            var units = new List<OrderUnit>();
            // Indexed walk so a per-line info entry (aligned to the original items order) can be attached even
            // though invalid lines are skipped.
            var itemList = items.ToList();
            for (var idx = 0; idx < itemList.Count; idx++)
            {
                var (productId, quantity, planId) = itemList[idx];
                if (quantity <= 0) continue;
                var p = _products.FirstOrDefault(x => x.Id == productId);
                if (p is null) continue;

                ProductPlan? plan = null;
                if (planId is int pid)
                {
                    // a referenced plan must exist and be active; otherwise reject the line
                    // rather than silently charging the base price.
                    plan = p.Plans.FirstOrDefault(x => x.Id == pid && x.IsActive);
                    if (plan is null) continue;
                }
                // A customer must pick a plan whenever the product offers one: the plan carries the real price
                // AND the "collect info from the customer" step, so a plan-less checkout would both charge the
                // bare base price and skip the account details the plan requires. Staff/internal placement is
                // unaffected — it may still order at the base price.
                else if (customerCheckout && p.Plans.Any(x => x.IsActive))
                    return new PlaceOrderResult(null, $"برای «{p.Name}» باید یک پلن انتخاب کنید.");

                var qty = Math.Min(quantity, 100);
                var planLabel = plan is null ? null : $"{plan.Type} · {plan.Months} ماهه";
                lines.Add(new OrderItem
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    Image = p.Image,
                    Plan = planLabel,
                    PlanMonths = plan?.Months,   // machine-readable duration → drives renewal-reminder expiry
                    UserCount = plan?.UserCount ?? 0,
                    UnitPrice = plan?.FinalPrice ?? p.FinalPrice,
                    Quantity = qty,
                });

                // One deliverable unit per quantity, each carrying the info the customer supplied for it.
                // A slot-fulfilled product is the exception: its quantity is USERS ON ONE SHARED ACCOUNT
                // (consecutive slots), so the whole line is a single deliverable no matter the quantity.
                var unitCount = p.SlotFulfillment ? 1 : qty;
                var lineUnits = lineInfo is not null && idx < lineInfo.Count ? lineInfo[idx]?.Units : null;
                for (var u = 0; u < unitCount; u++)
                {
                    var ui = lineUnits is not null && u < lineUnits.Count ? lineUnits[u] : null;
                    units.Add(new OrderUnit
                    {
                        Id = units.Count + 1,
                        ProductId = p.Id,
                        Name = p.Name,
                        Image = p.Image,
                        Plan = planLabel,
                        UserCount = plan?.UserCount ?? 0,
                        UnitIndex = u + 1,
                        CustomerInputs = ui?.Inputs ?? new(),
                        CustomerNote = ui?.Note,
                    });
                }
            }

            if (lines.Count == 0) return new PlaceOrderResult(null, "محصولی برای ثبت یافت نشد.");

            // identity-level gate: the buyer must meet each product's required level. Products default to
            // level 1, so a level-0 (registered-only) user can never purchase. Runs before any mutation.
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = _products.First(x => x.Id == group.Key);
                if (user.VerificationLevel < p.RequiredLevel)
                    return new PlaceOrderResult(null, $"سطح احراز هویت شما برای «{p.Name}» کافی نیست.");
            }

            // prevent overselling: the whole check-and-decrement runs under _gate, so two
            // concurrent buyers can never both take the last unit.
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = _products.First(x => x.Id == group.Key);
                var needed = group.Sum(l => l.Quantity);
                if (p.Stock < needed)
                    return new PlaceOrderResult(null, $"موجودی «{p.Name}» کافی نیست.");
            }

            var subtotal = lines.Sum(l => l.LineTotal);

            var discount = ResolveDiscount(discountCode, subtotal);
            if (discount.Error is not null) return new PlaceOrderResult(null, discount.Error);
            var goodsTotal = subtotal - discount.Amount;

            // VAT is charged on the discounted goods; payable is what the customer actually owes for the order.
            var vat = _settings.VatPercent > 0
                ? (long)Math.Round(goodsTotal * (double)_settings.VatPercent / 100.0, MidpointRounding.AwayFromZero)
                : 0;
            var payable = goodsTotal + vat;

            // wallet can cover the order fully or partially; the rest is paid by the chosen method.
            var walletUsed = fromWallet ? Math.Min(user.Wallet, payable) : 0;
            var remainder = payable - walletUsed;

            // When a real customer checks out and the wallet doesn't cover the whole order, the leftover is
            // a card-to-card payment from one of their own approved cards: they must pick a method, choose
            // a registered card, and attach the tracking + date + (when required) receipt BEFORE the order
            // is filed — nothing is mutated until these pass. Seeding and tests call PlaceOrder directly
            // without this flag, so they are unaffected.
            BankCard? sourceCard = null;
            if (customerCheckout && remainder > 0)
            {
                if (paymentMethodId is null)
                    return new PlaceOrderResult(null, "برای پرداخت مبلغ باقیمانده، یک روش پرداخت انتخاب کنید.");
                if (payment?.CardId is not int cardId)
                    return new PlaceOrderResult(null, "یک کارت بانکی ثبت‌شده را انتخاب کنید.");
                sourceCard = _cards.FirstOrDefault(c => c.Id == cardId);
                if (sourceCard is null || sourceCard.UserId != user.Id || sourceCard.Status != BankCardStatus.Approved)
                    return new PlaceOrderResult(null, "کارت انتخاب‌شده معتبر یا تأییدشده نیست.");
                if (string.IsNullOrWhiteSpace(payment.TrackingNumber))
                    return new PlaceOrderResult(null, "شماره پیگیری واریز را وارد کنید.");
                if (string.IsNullOrWhiteSpace(payment.PaymentDate))
                    return new PlaceOrderResult(null, "تاریخ پرداخت را وارد کنید.");
                if (_paymentSettings.RequireReceipt && string.IsNullOrWhiteSpace(payment.ReceiptUrl))
                    return new PlaceOrderResult(null, "رسید پرداخت مبلغ باقیمانده را بارگذاری کنید.");
            }

            // the gateway tax/fee only applies to the amount paid through that method. Each method may set
            // its own FeePercent; when it doesn't, the global GatewayFeePercent from pricing settings applies.
            long fee = 0;
            PaymentMethod? destMethod = null;
            if (paymentMethodId is int methodId && remainder > 0)
            {
                var pm = _paymentMethods.FirstOrDefault(x => x.Id == methodId);
                destMethod = pm;
                if (pm is not null)
                {
                    var feePercent = pm.FeePercent > 0 ? pm.FeePercent : _settings.GatewayFeePercent;
                    if (feePercent > 0)
                        // AwayFromZero matches the frontend's Math.round so the charged fee equals the
                        // amount shown at checkout to the toman (no banker's-rounding off-by-one).
                        fee = (long)Math.Round(remainder * (double)feePercent / 100.0, MidpointRounding.AwayFromZero);
                }
            }

            var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            var order = new Order
            {
                Id = ++_orderSeq,
                UserId = user.Id,
                UserName = name,
                PaymentMethod = paymentMethod,
                Items = lines,
                Units = units,
                Subtotal = subtotal,
                DiscountCode = discount.Code?.Code,
                DiscountAmount = discount.Amount,
                WalletPaid = walletUsed,
                VatAmount = vat,
                FeeAmount = fee,
                Total = goodsTotal + vat + fee,
                ReceiptUrl = remainder > 0 && !string.IsNullOrWhiteSpace(payment?.ReceiptUrl) ? payment.ReceiptUrl.Trim() : null,
                Date = Today(),
            };
            order.Code = $"PX-{100000 + order.Id}";
            if (discount.Code is not null) ConsumeDiscount(discount.Code.Id);

            foreach (var line in lines)
            {
                var p = _products.First(x => x.Id == line.ProductId);
                p.Stock = Math.Max(0, p.Stock - line.Quantity);
            }

            if (walletUsed > 0)
            {
                user.Wallet -= walletUsed;
                AddTransaction(new Transaction { UserId = user.Id, UserName = name, Type = TxTypes.Purchase, Amount = -walletUsed, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "wallet", Date = Today() });
            }

            // fully covered by wallet → paid; otherwise the remainder still needs approval.
            order.Status = remainder == 0 ? OrderStatus.Preparing : OrderStatus.PendingApproval;

            _orders.Add(order);

            // The card-to-card remainder is recorded as its own pending transaction (with the receipt) so
            // staff verify and approve it in the transactions panel — approving it advances this order.
            if (customerCheckout && remainder > 0 && sourceCard is not null)
            {
                AddTransaction(new Transaction
                {
                    UserId = user.Id,
                    UserName = name,
                    Type = TxTypes.OrderPayment,
                    Amount = -(order.Total - order.WalletPaid),   // remainder + gateway fee
                    Status = TxStatus.Pending,
                    Method = paymentMethod,
                    ReceiptUrl = string.IsNullOrWhiteSpace(payment!.ReceiptUrl) ? null : payment.ReceiptUrl.Trim(),
                    SourceCard = sourceCard.CardNumber, SourceHolder = sourceCard.HolderName,
                    DestinationCard = destMethod?.Value, DestinationHolder = destMethod?.Holder,
                    TrackingNumber = payment.TrackingNumber!.Trim(),
                    PaymentDate = payment.PaymentDate!.Trim(),
                    Description = string.IsNullOrWhiteSpace(payment.Description) ? null : payment.Description.Trim(),
                    OrderCode = order.Code,
                    Date = Today(),
                });
            }

            RefreshUserOrderStats(user.Id);
            return new PlaceOrderResult(order, null);
        }
    }

    public Order? SetOrderStatus(int id, OrderStatus status, string? changedBy = null, string? reason = null)
    {
        var o = SetOrderStatusCore(id, status, changedBy, reason);
        if (o is not null) PersistNow();
        return o;
    }

    private Order? SetOrderStatusCore(int id, OrderStatus status, string? changedBy = null, string? reason = null)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return null;
            var from = o.Status;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = status;
            // stamp the real delivery moment the first time the order is completed (drives expiry math).
            if (status == OrderStatus.Completed) { o.DeliveredAtUtc ??= DateTime.UtcNow; EnsureInvoiceNumber(o); }
            // keep the linked card-to-card payment in sync: approving the order marks its payment verified too.
            if (status == OrderStatus.Preparing)
            {
                var tx = _transactions.FirstOrDefault(t => t.OrderCode == o.Code && t.Type == TxTypes.OrderPayment && t.Status == TxStatus.Pending);
                if (tx is not null) tx.Status = TxStatus.Approved;
            }
            if (status == OrderStatus.Completed && !wasCompleted) CreditReferral(o);
            if (from != status) AppendOrderHistory(o, from, status, changedBy, reason);
            RefreshUserOrderStats(o.UserId);
            return o;
        }
    }

    // records the in-site delivery content for an order and marks it completed.
    public Order? DeliverOrder(int id, string content, string? changedBy = null)
    {
        var o = DeliverOrderCore(id, content, changedBy);
        if (o is not null) PersistNow();
        return o;
    }

    // Saves an in-progress delivery for a single unit WITHOUT delivering it: the content is kept so the admin
    // can leave the panel and finish later, and a second admin sees who's working on it. Returns the order.
    public Order? SaveUnitDraft(int orderId, int unitId, string content, string? changedBy = null)
    {
        Order? snapshot = null;
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            var unit = o?.Units.FirstOrDefault(u => u.Id == unitId);
            if (o is null || unit is null || unit.Delivered) return null;
            unit.DeliveryContent = content;
            unit.HandledBy = changedBy;
            snapshot = o;
        }
        if (snapshot is not null) PersistNow();
        return snapshot;
    }

    // Delivers a single unit. When every unit of the order is delivered the order itself is completed (which
    // credits referral, stamps the delivery time used for subscription expiry, and notifies the customer).
    // Returns (order, justCompleted) so the caller can send the completion email only on the final unit.
    public (Order? order, bool justCompleted) DeliverUnit(int orderId, int unitId, string content, string? changedBy = null)
    {
        Order? snapshot = null;
        var justCompleted = false;
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            var unit = o?.Units.FirstOrDefault(u => u.Id == unitId);
            if (o is null || unit is null) return (null, false);

            unit.DeliveryContent = content;
            unit.HandledBy = changedBy;
            if (!unit.Delivered)
            {
                unit.Delivered = true;
                unit.DeliveredAt = Today();
                unit.DeliveredAtUtc = DateTime.UtcNow;
            }

            // Complete the order once every unit is settled — delivered, or rejected-and-refunded — and it
            // isn't already completed. A rejected account must not hold the order open forever.
            if (o.Units.Count > 0 && o.Units.All(u => u.Delivered || u.Rejected) && o.Units.Any(u => u.Delivered)
                && o.Status != OrderStatus.Completed)
            {
                var from = o.Status;
                // The customer-facing order summary keeps a combined view of every DELIVERED unit's content —
                // a rejected one has none, and listing it would show the buyer an empty account.
                o.DeliveryContent = string.Join("\n\n", o.Units
                    .Where(u => u.Delivered)
                    .OrderBy(u => u.UnitIndex)
                    .Select(u => o.Units.Count > 1 ? $"اکانت {u.UnitIndex}:\n{u.DeliveryContent}" : u.DeliveryContent));
                o.DeliveredAt = Today();
                o.DeliveredAtUtc ??= DateTime.UtcNow;
                o.Status = OrderStatus.Completed;
                EnsureInvoiceNumber(o);
                CreditReferral(o);
                AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل همه‌ی اکانت‌ها");
                RefreshUserOrderStats(o.UserId);
                AddNotification(o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
                justCompleted = true;
            }
            snapshot = o;
        }
        if (snapshot is not null) PersistNow();
        return (snapshot, justCompleted);
    }

    private Order? DeliverOrderCore(int id, string content, string? changedBy = null)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return null;
            var from = o.Status;
            o.DeliveryContent = content;
            o.DeliveredAt = Today();
            o.DeliveredAtUtc ??= DateTime.UtcNow; // real timestamp for subscription expiry
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = OrderStatus.Completed;
            EnsureInvoiceNumber(o);
            if (!wasCompleted) CreditReferral(o);
            if (from != OrderStatus.Completed) AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل سفارش");
            RefreshUserOrderStats(o.UserId);
            AddNotification(o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
            return o;
        }
    }

    // cancels an order: restores stock and, if it was already paid, refunds the wallet
    // minus the configured cancellation penalty. `applyPenalty: false` is for a cancellation the CUSTOMER did
    // not choose (staff rejecting a receipt or an order) — penalising them for our decision would be wrong.
    public OrderActionResult CancelOrder(int id, string? changedBy = null, string? reason = null, bool applyPenalty = true)
    {
        var result = CancelOrderCore(id, changedBy, reason, applyPenalty);
        if (result.Error is null) PersistNow(); // stock restored + (possible) refund credited — persist now.
        return result;
    }

    private OrderActionResult CancelOrderCore(int id, string? changedBy = null, string? reason = null, bool applyPenalty = true)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return new OrderActionResult(null, "سفارش یافت نشد.");
            if (o.Status == OrderStatus.Cancelled) return new OrderActionResult(null, "این سفارش قبلاً لغو شده است.");
            if (o.Status == OrderStatus.Completed) return new OrderActionResult(null, "سفارش تکمیل‌شده قابل لغو نیست.");
            // Delivered accounts can't be taken back, so an order whose every unit is already delivered has
            // nothing left to cancel.
            if (o.Units.Count > 0 && o.Units.All(u => u.Delivered))
                return new OrderActionResult(null, "همه‌ی اکانت‌های این سفارش تحویل شده‌اند و قابل لغو نیست.");
            var from = o.Status;

            // Restore stock only for the seats NOT yet handed over — a delivered unit keeps its stock spent.
            foreach (var line in o.Items)
            {
                var p = _products.FirstOrDefault(x => x.Id == line.ProductId);
                if (p is not null) p.Stock += UndeliveredQuantity(o, line);
            }
            // Put back any seats still merely reserved for an undelivered unit (no-op for the item pool).
            foreach (var u in o.Units.Where(u => !u.Delivered)) ReleaseStockSlots(o.Id, u.Id);

            // The buyer keeps whatever was already delivered, so its value is NOT refundable.
            var deliveredValue = o.Units.Where(u => u.Delivered).Sum(u => UnitRefundAmount(o, u));
            // refund what was actually collected (full total once Preparing, else the wallet portion taken for
            // a partially-paid order) MINUS the value of the accounts already delivered.
            var collected = Math.Max(0, (o.Status == OrderStatus.Preparing ? o.Total : o.WalletPaid) - deliveredValue);
            if (collected > 0)
            {
                var buyer = UserById(o.UserId);
                if (buyer is not null)
                {
                    var penalty = applyPenalty ? _settings.CancellationPenaltyPercent : 0;
                    // Compute the penalty then subtract (rounded AwayFromZero) so the refund matches the
                    // figure shown to the user in the cancel dialog exactly.
                    var penaltyAmount = (long)Math.Round(collected * (double)penalty / 100.0, MidpointRounding.AwayFromZero);
                    var refund = Math.Max(0, collected - penaltyAmount);
                    buyer.Wallet += refund;
                    var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
                    AddTransaction(new Transaction { UserId = buyer.Id, UserName = name, Type = TxTypes.Refund, Amount = refund, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "refund", Date = Today() });
                }
            }

            o.Status = OrderStatus.Cancelled;
            AppendOrderHistory(o, from, OrderStatus.Cancelled, changedBy, reason ?? "لغو سفارش");
            RefreshUserOrderStats(o.UserId);
            return new OrderActionResult(o, null);
        }
    }

    // Claims the right to announce this order to the orders group. Under _gate, so two concurrent approvals
    // can never both win and post the accounts twice.
    public bool TryClaimOrderBotNotification(int orderId)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            if (o is null || o.OrderBotNotifiedAtUtc is not null) return false;
            o.OrderBotNotifiedAtUtc = DateTime.UtcNow;
            PersistNow();
            return true;
        }
    }

    // What the buyer actually paid for ONE account: its plan price minus that account's share of the order's
    // discount. VAT and the gateway fee are deliberately excluded — a rejected account refunds the service
    // price only. Sensitive to rounding, so the share is computed from the order's own subtotal.
    // The part of a line's quantity whose units are still undelivered — the stock a cancellation returns to the
    // shelf. A line with no units (legacy orders) counts as fully undelivered.
    internal static long UndeliveredQuantity(Order order, OrderItem line)
    {
        var lineUnits = order.Units.Where(u => u.ProductId == line.ProductId && (u.Plan ?? "") == (line.Plan ?? "")).ToList();
        if (lineUnits.Count == 0) return line.Quantity;
        return (long)line.Quantity * lineUnits.Count(u => !u.Delivered) / lineUnits.Count;
    }

    private static long UnitRefundAmount(Order order, OrderUnit unit)
    {
        var item = order.Items.FirstOrDefault(i => i.ProductId == unit.ProductId && (i.Plan ?? "") == (unit.Plan ?? ""));
        if (item is null) return 0;
        // A line normally fans out into Quantity units (one unit = one UnitPrice), but a slot-fulfilled line
        // is a SINGLE unit covering the whole quantity — its refund is the line's share, not one seat's.
        var unitsOfLine = Math.Max(1, order.Units.Count(u =>
            u.ProductId == unit.ProductId && (u.Plan ?? "") == (unit.Plan ?? "")));
        var price = (long)Math.Round(item.UnitPrice * (double)item.Quantity / unitsOfLine, MidpointRounding.AwayFromZero);
        if (order.DiscountAmount <= 0 || order.Subtotal <= 0) return price;
        // This account's slice of the discount, proportional to its price within the order.
        var share = (long)Math.Round(order.DiscountAmount * (double)price / order.Subtotal, MidpointRounding.AwayFromZero);
        return Math.Max(0, price - share);
    }

    // Rejects ONE account of an order: refunds what the buyer paid for it, returns its stock, and leaves the
    // rest of the order alone. Once every account is either delivered or rejected the order settles itself —
    // cancelled when nothing survived, completed otherwise.
    public (Order? order, long refunded, string? error) RejectUnit(int orderId, int unitId, string? reason, string? changedBy = null)
    {
        (Order? order, long refunded, string? error) result;
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            if (o is null) return (null, 0, "سفارش یافت نشد.");
            var unit = o.Units.FirstOrDefault(u => u.Id == unitId);
            if (unit is null) return (null, 0, "اکانت یافت نشد.");
            if (unit.Delivered) return (null, 0, "این اکانت قبلاً تحویل شده است.");
            if (unit.Rejected) return (null, 0, "این اکانت قبلاً رد شده است.");

            var refund = UnitRefundAmount(o, unit);
            unit.Rejected = true;
            unit.RejectionReason = reason;
            unit.RejectedAtUtc = DateTime.UtcNow;
            unit.HandledBy = changedBy;
            unit.RefundedAmount = refund;

            // The account was never handed over, so its stock goes back on the shelf. A slot-fulfilled line is
            // one unit for the whole quantity, so it returns every seat it had claimed.
            var p = _products.FirstOrDefault(x => x.Id == unit.ProductId);
            if (p is not null)
            {
                var line = o.Items.FirstOrDefault(i => i.ProductId == unit.ProductId && (i.Plan ?? "") == (unit.Plan ?? ""));
                var unitsOfLine = Math.Max(1, o.Units.Count(u =>
                    u.ProductId == unit.ProductId && (u.Plan ?? "") == (unit.Plan ?? "")));
                p.Stock += Math.Max(1, (line?.Quantity ?? 1) / unitsOfLine);
            }
            // Any slots still held for this unit go back into rotation (no-op for item-pool products;
            // _gate is reentrant, so the nested lock inside is harmless).
            ReleaseStockSlots(orderId, unitId);

            if (refund > 0)
            {
                var buyer = UserById(o.UserId);
                if (buyer is not null)
                {
                    buyer.Wallet += refund;
                    var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
                    AddTransaction(new Transaction
                    {
                        UserId = buyer.Id, UserName = name, Type = TxTypes.Refund, Amount = refund,
                        Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "reject-unit",
                        OrderCode = o.Code, Date = Today(),
                    });
                    AddNotification(buyer.Id, "بازگشت وجه",
                        $"«{unit.Name}» از سفارش {o.Code} رد شد و {refund:N0} تومان به کیف پول شما بازگشت.", "/account/wallet");
                }
            }

            SettleUnitsIfDone(o, changedBy);
            result = (o, refund, null);
        }
        PersistNow();
        return result;
    }

    // Closes an order once no account is still pending: all rejected → cancelled, otherwise completed (which
    // is what issues the invoice). Caller holds _gate.
    private void SettleUnitsIfDone(Order o, string? changedBy)
    {
        if (o.Units.Count == 0 || o.Status is OrderStatus.Completed or OrderStatus.Cancelled) return;
        if (!o.Units.All(u => u.Delivered || u.Rejected)) return;

        var from = o.Status;
        if (o.Units.All(u => u.Rejected))
        {
            o.Status = OrderStatus.Cancelled;
            AppendOrderHistory(o, from, OrderStatus.Cancelled, changedBy, "رد همه‌ی اکانت‌ها");
            AddNotification(o.UserId, "سفارش لغو شد", $"همه‌ی اقلام سفارش {o.Code} رد شد و مبلغ آن‌ها بازگشت داده شد.", "/account/orders");
        }
        else
        {
            o.DeliveryContent = string.Join("\n\n", o.Units.Where(u => u.Delivered).OrderBy(u => u.UnitIndex)
                .Select(u => o.Units.Count > 1 ? $"اکانت {u.UnitIndex}:\n{u.DeliveryContent}" : u.DeliveryContent));
            o.DeliveredAt = Today();
            o.DeliveredAtUtc ??= DateTime.UtcNow;
            o.Status = OrderStatus.Completed;
            EnsureInvoiceNumber(o);
            CreditReferral(o);
            AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تعیین تکلیف همه‌ی اکانت‌ها");
            AddNotification(o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
        }
        RefreshUserOrderStats(o.UserId);
    }

    // Stamps the order's 16-digit invoice number the first time it completes, unique across every order.
    // Random rather than sequential so it doesn't leak the shop's order count. Caller holds _gate.
    private void EnsureInvoiceNumber(Order o)
    {
        if (!string.IsNullOrWhiteSpace(o.InvoiceNumber)) return; // already issued — never re-issue
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var candidate = NewInvoiceNumber();
            if (!_orders.Any(x => x.InvoiceNumber == candidate)) { o.InvoiceNumber = candidate; return; }
        }
        throw new InvalidOperationException("Could not allocate a unique invoice number.");
    }

    private static string NewInvoiceNumber()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes) % 10_000_000_000_000_000UL;
        return value.ToString("D16", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Appends one audit entry for an order status transition. Caller holds _gate. The id is unique within
    // the order's own history list.
    private static void AppendOrderHistory(Order o, OrderStatus from, OrderStatus to, string? changedBy, string? reason)
    {
        o.History.Add(new OrderStatusHistory
        {
            Id = (o.History.Count == 0 ? 0 : o.History.Max(h => h.Id)) + 1,
            OrderId = o.Id,
            ChangedByUsername = string.IsNullOrWhiteSpace(changedBy) ? "سیستم" : changedBy!.Trim(),
            FromStatus = from,
            ToStatus = to,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim(),
            ChangedAtUtc = DateTime.UtcNow,
        });
    }

    // Formats a UTC moment as a Persian-digit Jalali date (yyyy/MM/dd), matching how dates display elsewhere.
    private static string JalaliDate(DateTime dt)
    {
        var pc = new System.Globalization.PersianCalendar();
        var s = $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }

    // Atomically finds completed time-based orders whose subscription expires within `hoursBefore` hours and
    // hasn't been reminded yet: marks each as reminded, fires the in-app bell notification, and returns the
    // list so the caller (the background worker) can send the emails outside the lock. The
    // RenewalReminderSentUtc flag guarantees a given order is never reminded twice, and the result is
    // persisted immediately so a restart can't resend.
    public IReadOnlyList<RenewalReminder> CollectDueRenewalReminders(int hoursBefore)
    {
        var due = new List<RenewalReminder>();
        if (hoursBefore <= 0) return due;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromHours(hoursBefore);
            foreach (var o in _orders)
            {
                if (o.Status != OrderStatus.Completed) continue;
                if (o.DeliveredAtUtc is not DateTime delivered) continue;    // legacy/undelivered → skip
                if (o.RenewalReminderSentUtc is not null) continue;           // already reminded
                // the order stays "active" until its longest time-based plan expires.
                var months = o.Items.Where(i => i.PlanMonths is int m && m > 0).Select(i => i.PlanMonths!.Value).DefaultIfEmpty(0).Max();
                if (months <= 0) continue;                                    // not a time-based subscription
                var expires = delivered.AddMonths(months);
                var remaining = expires - now;
                if (remaining <= TimeSpan.Zero || remaining > window) continue; // expired, or not yet in window

                var user = UserById(o.UserId);
                if (user is null) continue;

                o.RenewalReminderSentUtc = now;
                var expiresFa = JalaliDate(expires);
                AddNotification(user.Id, "یادآوری تمدید اشتراک",
                    $"اشتراک سفارش {o.Code} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.",
                    "/account/orders");
                due.Add(new RenewalReminder(user.Id, user.Email, o.Code, expiresFa));
            }
        }
        if (due.Count > 0) PersistNow();
        return due;
    }

    // pays the referrer their commission once a referred buyer's order is completed.
    private void CreditReferral(Order order)
    {
        var buyer = UserById(order.UserId);
        if (buyer?.ReferredBy is not int referrerId) return;
        var referrer = UserById(referrerId);
        if (referrer is null) return;

        var percent = _settings.ReferralCommissionPercent;
        if (percent <= 0) return;
        var commission = (long)Math.Round(order.Total * (double)percent / 100.0, MidpointRounding.AwayFromZero);
        if (commission <= 0) return;

        referrer.Wallet += commission;
        _referralEarnings.Add(new ReferralEarning
        {
            ReferrerId = referrerId,
            ReferredName = order.UserName,
            OrderCode = order.Code,
            OrderAmount = order.Total,
            Commission = commission,
            Date = Today(),
        });
        var referrerName = string.IsNullOrWhiteSpace(referrer.Name) ? referrer.Username : referrer.Name;
        AddTransaction(new Transaction { UserId = referrerId, UserName = referrerName, Type = TxTypes.Referral, Amount = commission, Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "referral", Date = Today() });
    }

    public IReadOnlyList<ReferralEarning> GetReferralEarnings(int referrerId)
    {
        lock (_gate) return _referralEarnings.Where(e => e.ReferrerId == referrerId).OrderByDescending(e => e.Date).ToList();
    }

    public int CountReferredUsers(int referrerId)
    {
        lock (_gate) return _users.Count(u => u.ReferredBy == referrerId);
    }

    // tickets

    public IReadOnlyList<Ticket> GetTickets(TicketStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<Ticket> q = _tickets;
            if (status is TicketStatus s) q = q.Where(t => t.Status == s);
            return q.OrderByDescending(t => t.Id).ToList();
        }
    }

    public IReadOnlyList<Ticket> GetUserTickets(int userId)
    {
        lock (_gate) return _tickets.Where(t => t.UserId == userId).OrderByDescending(t => t.Id).ToList();
    }

    public Ticket? GetTicket(int id)
    {
        lock (_gate) return _tickets.FirstOrDefault(t => t.Id == id);
    }

    public Ticket CreateTicket(int userId, string userName, string subject, string department, string body,
        TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        lock (_gate)
        {
            var t = new Ticket
            {
                Id = ++_ticketSeq,
                UserId = userId,
                UserName = userName,
                Subject = subject,
                Department = department,
                Priority = priority,
                Attachment = attachment ?? "",
                Status = TicketStatus.Open,
                Date = Today(),
            };
            t.Code = $"T-{5800 + t.Id}";
            t.Messages.Add(new TicketMessage { Author = userName, Body = body, IsAdmin = false, Date = Today() });
            _tickets.Add(t);
            return t;
        }
    }

    // Opens a ticket ON BEHALF OF a user: support starts the conversation, so the opening message is from
    // staff and the ticket lands in the user's account already "Answered". The owner is notified so the
    // thread surfaces for them just like a reply would.
    public Ticket CreateTicketForUser(int userId, string userName, string subject, string department, string body,
        string authorName, TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        Ticket t;
        lock (_gate)
        {
            t = new Ticket
            {
                Id = ++_ticketSeq,
                UserId = userId,
                UserName = userName,
                Subject = subject,
                Department = department,
                Priority = priority,
                Status = TicketStatus.Answered,
                Date = Today(),
            };
            t.Code = $"T-{5800 + t.Id}";
            // The opening file rides on the support message so it appears inline in the conversation.
            t.Messages.Add(new TicketMessage { Author = authorName, Body = body, IsAdmin = true, Date = Today(), Attachment = attachment ?? "" });
            _tickets.Add(t);
            AddNotification(userId, "تیکت جدید از پشتیبانی", $"پشتیبانی فونیکس برای شما تیکت «{subject}» باز کرد.", "/account/tickets");
        }
        PersistNow();
        return t;
    }

    public Ticket? ReplyTicket(int id, string author, string body, bool isAdmin, string? attachment = null)
    {
        lock (_gate)
        {
            var t = _tickets.FirstOrDefault(x => x.Id == id);
            if (t is null) return null;
            t.Messages.Add(new TicketMessage { Author = author, Body = body, IsAdmin = isAdmin, Date = Today(), Attachment = attachment ?? "" });
            t.Status = isAdmin ? TicketStatus.Answered : TicketStatus.Open;
            // a staff reply notifies the ticket's owner.
            if (isAdmin) AddNotification(t.UserId, "پاسخ تیکت پشتیبانی", $"به تیکت «{t.Subject}» پاسخ داده شد.", "/account/tickets");
            return t;
        }
    }

    public bool SetTicketStatus(int id, TicketStatus status)
    {
        lock (_gate)
        {
            var t = _tickets.FirstOrDefault(x => x.Id == id);
            if (t is null) return false;
            t.Status = status;
            return true;
        }
    }

    private void SeedOrders()
    {
        // Seeding runs inside the constructor before the initial Save(); call the core (non-flushing)
        // variants so we don't write the half-built store to disk on every seeded order.
        var u1 = _users.First(u => u.Id == 1);
        var u2 = _users.First(u => u.Id == 2);
        var o1 = PlaceOrderCore(u1, new (int, int, int?)[] { (1, 1, null) }, "کارت بانکی", false).Order!;
        SetOrderStatusCore(o1.Id, OrderStatus.Completed);
        var o2 = PlaceOrderCore(u2, new (int, int, int?)[] { (2, 1, null), (5, 1, null) }, "کارت بانکی", false).Order!;
        SetOrderStatusCore(o2.Id, OrderStatus.Preparing);
        PlaceOrderCore(u1, new (int, int, int?)[] { (3, 1, null) }, "کارت بانکی", false);

        var t1 = CreateTicket(1, "علی محمدی", "مشکل در فعال‌سازی اکانت نتفلیکس", "فنی", "سلام، اکانت من بعد از خرید فعال نشده. لطفاً بررسی کنید.");
        ReplyTicket(t1.Id, "پشتیبانی فونیکس", "سلام، در حال بررسی هستیم و تا ساعاتی دیگر اطلاع می‌دهیم.", true);
        CreateTicket(2, "زهرا کریمی", "درخواست بازگشت وجه", "مالی", "سفارش من اشتباه ثبت شده و درخواست بازگشت وجه دارم.");
    }
}
