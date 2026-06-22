using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record PlaceOrderResult(Order? Order, string? Error);
public record OrderActionResult(Order? Order, string? Error);
// the card-to-card payment details for an order's gateway remainder (collected at checkout).
public record RemainderPayment(int? CardId, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);

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
        var user = _users.FirstOrDefault(u => u.Id == userId);
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
    public PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items, string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null, RemainderPayment? payment = null, bool customerCheckout = false)
    {
        var result = PlaceOrderCore(user, items, paymentMethod, fromWallet, discountCode, paymentMethodId, payment, customerCheckout);
        if (result.Error is null) PersistNow();
        return result;
    }

    private PlaceOrderResult PlaceOrderCore(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items, string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null, RemainderPayment? payment = null, bool customerCheckout = false)
    {
        lock (_gate)
        {
            var lines = new List<OrderItem>();
            foreach (var (productId, quantity, planId) in items)
            {
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

                lines.Add(new OrderItem
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    Image = p.Image,
                    Plan = plan is null ? null : $"{plan.Type} · {plan.Months} ماهه",
                    UnitPrice = plan?.FinalPrice ?? p.FinalPrice,
                    Quantity = Math.Min(quantity, 100),
                });
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

            // wallet can cover the order fully or partially; the rest is paid by the chosen method.
            var walletUsed = fromWallet ? Math.Min(user.Wallet, goodsTotal) : 0;
            var remainder = goodsTotal - walletUsed;

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

            // the gateway tax/fee (if any) only applies to the amount paid through that method.
            long fee = 0;
            if (paymentMethodId is int methodId && remainder > 0)
            {
                var pm = _paymentMethods.FirstOrDefault(x => x.Id == methodId);
                if (pm is not null && pm.FeePercent > 0)
                    // AwayFromZero matches the frontend's Math.round so the charged fee equals the
                    // amount shown at checkout to the toman (no banker's-rounding off-by-one).
                    fee = (long)Math.Round(remainder * (double)pm.FeePercent / 100.0, MidpointRounding.AwayFromZero);
            }

            var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            var order = new Order
            {
                Id = ++_orderSeq,
                UserId = user.Id,
                UserName = name,
                PaymentMethod = paymentMethod,
                Items = lines,
                Subtotal = subtotal,
                DiscountCode = discount.Code?.Code,
                DiscountAmount = discount.Amount,
                WalletPaid = walletUsed,
                FeeAmount = fee,
                Total = goodsTotal + fee,
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
                    SourceCard = sourceCard.CardNumber,
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

    public Order? SetOrderStatus(int id, OrderStatus status)
    {
        var o = SetOrderStatusCore(id, status);
        if (o is not null) PersistNow();
        return o;
    }

    private Order? SetOrderStatusCore(int id, OrderStatus status)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return null;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = status;
            // keep the linked card-to-card payment in sync: approving the order marks its payment verified too.
            if (status == OrderStatus.Preparing)
            {
                var tx = _transactions.FirstOrDefault(t => t.OrderCode == o.Code && t.Type == TxTypes.OrderPayment && t.Status == TxStatus.Pending);
                if (tx is not null) tx.Status = TxStatus.Approved;
            }
            if (status == OrderStatus.Completed && !wasCompleted) CreditReferral(o);
            RefreshUserOrderStats(o.UserId);
            return o;
        }
    }

    // records the in-site delivery content for an order and marks it completed.
    public Order? DeliverOrder(int id, string content)
    {
        var o = DeliverOrderCore(id, content);
        if (o is not null) PersistNow();
        return o;
    }

    private Order? DeliverOrderCore(int id, string content)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return null;
            o.DeliveryContent = content;
            o.DeliveredAt = Today();
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = OrderStatus.Completed;
            if (!wasCompleted) CreditReferral(o);
            RefreshUserOrderStats(o.UserId);
            AddNotification(o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
            return o;
        }
    }

    // cancels an order: restores stock and, if it was already paid, refunds the wallet
    // minus the configured cancellation penalty.
    public OrderActionResult CancelOrder(int id)
    {
        var result = CancelOrderCore(id);
        if (result.Error is null) PersistNow(); // stock restored + (possible) refund credited — persist now.
        return result;
    }

    private OrderActionResult CancelOrderCore(int id)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return new OrderActionResult(null, "سفارش یافت نشد.");
            if (o.Status == OrderStatus.Cancelled) return new OrderActionResult(null, "این سفارش قبلاً لغو شده است.");
            if (o.Status == OrderStatus.Completed) return new OrderActionResult(null, "سفارش تکمیل‌شده قابل لغو نیست.");

            foreach (var line in o.Items)
            {
                var p = _products.FirstOrDefault(x => x.Id == line.ProductId);
                if (p is not null) p.Stock += line.Quantity;
            }

            // refund what was actually collected: the full total once approved (Preparing),
            // otherwise just the wallet portion already taken for a partially-paid order.
            var collected = o.Status == OrderStatus.Preparing ? o.Total : o.WalletPaid;
            if (collected > 0)
            {
                var buyer = _users.FirstOrDefault(u => u.Id == o.UserId);
                if (buyer is not null)
                {
                    var penalty = _settings.CancellationPenaltyPercent;
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
            RefreshUserOrderStats(o.UserId);
            return new OrderActionResult(o, null);
        }
    }

    // pays the referrer their commission once a referred buyer's order is completed.
    private void CreditReferral(Order order)
    {
        var buyer = _users.FirstOrDefault(u => u.Id == order.UserId);
        if (buyer?.ReferredBy is not int referrerId) return;
        var referrer = _users.FirstOrDefault(u => u.Id == referrerId);
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

    public Ticket CreateTicket(int userId, string userName, string subject, string department, string body)
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
                Status = TicketStatus.Open,
                Date = Today(),
            };
            t.Code = $"T-{5800 + t.Id}";
            t.Messages.Add(new TicketMessage { Author = userName, Body = body, IsAdmin = false, Date = Today() });
            _tickets.Add(t);
            return t;
        }
    }

    public Ticket? ReplyTicket(int id, string author, string body, bool isAdmin)
    {
        lock (_gate)
        {
            var t = _tickets.FirstOrDefault(x => x.Id == id);
            if (t is null) return null;
            t.Messages.Add(new TicketMessage { Author = author, Body = body, IsAdmin = isAdmin, Date = Today() });
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
