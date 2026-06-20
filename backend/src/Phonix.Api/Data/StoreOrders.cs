using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record PlaceOrderResult(Order? Order, string? Error);
public record OrderActionResult(Order? Order, string? Error);

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

    public PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items, string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null)
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

            // the gateway tax/fee (if any) only applies to the amount paid through that method.
            long fee = 0;
            if (paymentMethodId is int methodId && remainder > 0)
            {
                var pm = _paymentMethods.FirstOrDefault(x => x.Id == methodId);
                if (pm is not null && pm.FeePercent > 0)
                    fee = (long)Math.Round(remainder * (double)pm.FeePercent / 100.0);
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
                AddTransaction(new Transaction { UserName = name, Type = "خرید", Amount = -walletUsed, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "wallet", Date = Today() });
            }

            // fully covered by wallet → paid; otherwise the remainder still needs approval.
            order.Status = remainder == 0 ? OrderStatus.Preparing : OrderStatus.PendingApproval;

            _orders.Add(order);
            return new PlaceOrderResult(order, null);
        }
    }

    public Order? SetOrderStatus(int id, OrderStatus status)
    {
        lock (_gate)
        {
            var o = _orders.FirstOrDefault(x => x.Id == id);
            if (o is null) return null;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = status;
            if (status == OrderStatus.Completed && !wasCompleted) CreditReferral(o);
            return o;
        }
    }

    // records the in-site delivery content for an order and marks it completed.
    public Order? DeliverOrder(int id, string content)
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
            return o;
        }
    }

    // cancels an order: restores stock and, if it was already paid, refunds the wallet
    // minus the configured cancellation penalty.
    public OrderActionResult CancelOrder(int id)
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
                    var refund = (long)Math.Round(collected * (double)(100m - penalty) / 100.0);
                    if (refund < 0) refund = 0;
                    buyer.Wallet += refund;
                    var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
                    AddTransaction(new Transaction { UserName = name, Type = "بازگشت وجه", Amount = refund, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "refund", Date = Today() });
                }
            }

            o.Status = OrderStatus.Cancelled;
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
        var commission = (long)Math.Round(order.Total * (double)percent / 100.0);
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
        AddTransaction(new Transaction { UserName = referrerName, Type = "پورسانت", Amount = commission, Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "referral", Date = Today() });
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
        var u1 = _users.First(u => u.Id == 1);
        var u2 = _users.First(u => u.Id == 2);
        var o1 = PlaceOrder(u1, new (int, int, int?)[] { (1, 1, null) }, "کارت بانکی", false).Order!;
        SetOrderStatus(o1.Id, OrderStatus.Completed);
        var o2 = PlaceOrder(u2, new (int, int, int?)[] { (2, 1, null), (5, 1, null) }, "کارت بانکی", false).Order!;
        SetOrderStatus(o2.Id, OrderStatus.Preparing);
        PlaceOrder(u1, new (int, int, int?)[] { (3, 1, null) }, "کارت بانکی", false);

        var t1 = CreateTicket(1, "علی محمدی", "مشکل در فعال‌سازی اکانت نتفلیکس", "فنی", "سلام، اکانت من بعد از خرید فعال نشده. لطفاً بررسی کنید.");
        ReplyTicket(t1.Id, "پشتیبانی فونیکس", "سلام، در حال بررسی هستیم و تا ساعاتی دیگر اطلاع می‌دهیم.", true);
        CreateTicket(2, "زهرا کریمی", "درخواست بازگشت وجه", "مالی", "سفارش من اشتباه ثبت شده و درخواست بازگشت وجه دارم.");
    }
}
