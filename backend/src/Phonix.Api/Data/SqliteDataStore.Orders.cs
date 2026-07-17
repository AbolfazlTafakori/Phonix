using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Orders: reads, atomic PlaceOrder, discounts, status transitions, fulfillment.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Orders (reads) ──────────────────────────────────────────────────────────────────────────────────

    public Order? GetOrder(int id)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id });
        return json is null ? null : Deserialize<Order>(json);
    }

    public IReadOnlyList<Order> GetOrders(OrderStatus? status = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Orders";
        if (status is not null) sql += " WHERE Status = @status";
        sql += " ORDER BY Id DESC;";
        return conn.Query<string>(sql, new { status = status is null ? 0 : (int)status.Value })
            .Select(j => Deserialize<Order>(j)!).ToList();
    }

    public IReadOnlyList<Order> GetUserOrders(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Orders WHERE UserId = @userId ORDER BY Id DESC", new { userId })
            .Select(j => Deserialize<Order>(j)!).ToList();
    }

    // ── PlaceOrder: the fully-atomic high-traffic write ─────────────────────────────────────────────────
    // EVERYTHING — re-reading live stock + wallet, the oversell guard, the wallet debit, the stock
    // decrement, discount consumption, the order row, and the payment transactions — happens inside ONE
    // BEGIN IMMEDIATE transaction. Because the write lock is held for the whole unit of work, two concurrent
    // buyers can NEVER both take the last unit or both spend the same wallet balance: the second is serialized
    // behind the first and re-reads the post-commit state. Any failure rolls the whole thing back — no
    // half-charged wallet, no phantom stock decrement. This is the per-operation replacement for `_gate`.
    public PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items,
        string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null,
        RemainderPayment? payment = null, bool customerCheckout = false, IReadOnlyList<OrderLineInfo>? lineInfo = null)
    {
        var itemList = items.ToList();
        return WriteTx<PlaceOrderResult>((conn, tx) =>
        {
            // Re-read the buyer INSIDE the transaction so wallet/level reflect the latest committed state,
            // never the (possibly stale) object the caller passed in.
            var liveUserJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id = user.Id }, tx);
            if (liveUserJson is null) return new PlaceOrderResult(null, "کاربر یافت نشد.");
            var buyer = Deserialize<AppUser>(liveUserJson)!;

            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);
            var paymentSettings = ReadSingleton<PaymentSettings>(conn, tx, PaymentKey);

            // Load every referenced product once (live row, under the write lock) for validation + mutation.
            var products = new Dictionary<int, Product>();
            foreach (var pid in itemList.Where(i => i.quantity > 0).Select(i => i.productId).Distinct())
            {
                var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @pid", new { pid }, tx);
                if (pj is not null) products[pid] = Deserialize<Product>(pj)!;
            }

            var lines = new List<OrderItem>();
            var units = new List<OrderUnit>();
            for (var idx = 0; idx < itemList.Count; idx++)
            {
                var (productId, quantity, planId) = itemList[idx];
                if (quantity <= 0) continue;
                if (!products.TryGetValue(productId, out var p)) continue;

                ProductPlan? plan = null;
                if (planId is int pid)
                {
                    // a referenced plan must exist and be active; otherwise reject the line rather than
                    // silently charging the base price.
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
                    ProductId = p.Id, Name = p.Name, Image = p.Image, Plan = planLabel,
                    PlanMonths = plan?.Months, UnitPrice = plan?.FinalPrice ?? p.FinalPrice, Quantity = qty,
                });

                var lineUnits = lineInfo is not null && idx < lineInfo.Count ? lineInfo[idx]?.Units : null;
                for (var u = 0; u < qty; u++)
                {
                    var ui = lineUnits is not null && u < lineUnits.Count ? lineUnits[u] : null;
                    units.Add(new OrderUnit
                    {
                        Id = units.Count + 1, ProductId = p.Id, Name = p.Name, Image = p.Image, Plan = planLabel,
                        UnitIndex = u + 1, CustomerInputs = ui?.Inputs ?? new(), CustomerNote = ui?.Note,
                    });
                }
            }

            if (lines.Count == 0) return new PlaceOrderResult(null, "محصولی برای ثبت یافت نشد.");

            // identity-level gate (products default to level 1; a level-0 user can never purchase).
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = products[group.Key];
                if (buyer.VerificationLevel < p.RequiredLevel)
                    return new PlaceOrderResult(null, $"سطح احراز هویت شما برای «{p.Name}» کافی نیست.");
            }

            // oversell guard: check-and-decrement is inside the IMMEDIATE tx, so two buyers can't both win.
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = products[group.Key];
                var needed = group.Sum(l => l.Quantity);
                if (p.Stock < needed) return new PlaceOrderResult(null, $"موجودی «{p.Name}» کافی نیست.");
            }

            var subtotal = lines.Sum(l => l.LineTotal);
            var discount = ResolveDiscountTx(conn, tx, discountCode, subtotal);
            if (discount.Error is not null) return new PlaceOrderResult(null, discount.Error);
            var goodsTotal = subtotal - discount.Amount;

            var vat = settings.VatPercent > 0
                ? (long)Math.Round(goodsTotal * (double)settings.VatPercent / 100.0, MidpointRounding.AwayFromZero)
                : 0;
            var payable = goodsTotal + vat;

            var walletUsed = fromWallet ? Math.Min(buyer.Wallet, payable) : 0;
            var remainder = payable - walletUsed;

            // customer card-to-card for the remainder: validated BEFORE any mutation (nothing is written if it fails).
            BankCard? sourceCard = null;
            if (customerCheckout && remainder > 0)
            {
                if (paymentMethodId is null)
                    return new PlaceOrderResult(null, "برای پرداخت مبلغ باقیمانده، یک روش پرداخت انتخاب کنید.");
                if (payment?.CardId is not int cardId)
                    return new PlaceOrderResult(null, "یک کارت بانکی ثبت‌شده را انتخاب کنید.");
                var cardJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Cards WHERE Id = @cardId", new { cardId }, tx);
                sourceCard = cardJson is null ? null : Deserialize<BankCard>(cardJson);
                if (sourceCard is null || sourceCard.UserId != buyer.Id || sourceCard.Status != BankCardStatus.Approved)
                    return new PlaceOrderResult(null, "کارت انتخاب‌شده معتبر یا تأییدشده نیست.");
                if (string.IsNullOrWhiteSpace(payment.TrackingNumber))
                    return new PlaceOrderResult(null, "شماره پیگیری واریز را وارد کنید.");
                if (string.IsNullOrWhiteSpace(payment.PaymentDate))
                    return new PlaceOrderResult(null, "تاریخ پرداخت را وارد کنید.");
                if (paymentSettings.RequireReceipt && string.IsNullOrWhiteSpace(payment.ReceiptUrl))
                    return new PlaceOrderResult(null, "رسید پرداخت مبلغ باقیمانده را بارگذاری کنید.");
            }

            // gateway fee applies only to the amount paid through the method (its own FeePercent, else global).
            // destMethod is also the destination the buyer paid TO — captured onto the receipt transaction below.
            long fee = 0;
            PaymentMethod? destMethod = null;
            if (paymentMethodId is int methodId && remainder > 0)
            {
                var pmJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM PaymentMethods WHERE Id = @methodId", new { methodId }, tx);
                var pm = pmJson is null ? null : Deserialize<PaymentMethod>(pmJson);
                destMethod = pm;
                if (pm is not null)
                {
                    var feePercent = pm.FeePercent > 0 ? pm.FeePercent : settings.GatewayFeePercent;
                    if (feePercent > 0)
                        fee = (long)Math.Round(remainder * (double)feePercent / 100.0, MidpointRounding.AwayFromZero);
                }
            }

            var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
            var order = new Order
            {
                UserId = buyer.Id, UserName = name, PaymentMethod = paymentMethod, Items = lines, Units = units,
                Subtotal = subtotal, DiscountCode = discount.Code?.Code, DiscountAmount = discount.Amount,
                WalletPaid = walletUsed, VatAmount = vat, FeeAmount = fee, Total = goodsTotal + vat + fee,
                ReceiptUrl = remainder > 0 && !string.IsNullOrWhiteSpace(payment?.ReceiptUrl) ? payment.ReceiptUrl.Trim() : null,
                Date = Today(),
                Status = remainder == 0 ? OrderStatus.Preparing : OrderStatus.PendingApproval,
            };

            // ── mutations (all committed together) ──
            if (discount.Code is not null) ConsumeDiscountTx(conn, tx, discount.Code);

            foreach (var line in lines)
            {
                var p = products[line.ProductId];
                p.Stock = Math.Max(0, p.Stock - line.Quantity);
            }
            foreach (var p in products.Values) UpsertProduct(conn, tx, p); // persist decremented stock

            if (walletUsed > 0)
            {
                buyer.Wallet -= walletUsed;
                InsertTransaction(conn, tx, new Transaction
                {
                    UserId = buyer.Id, UserName = name, Type = TxTypes.Purchase, Amount = -walletUsed,
                    Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "wallet", Date = Today(),
                });
            }

            // insert the order, then stamp the derived Code and rewrite the payload (one transaction → atomic).
            var orderId = conn.ExecuteScalar<long>(@"
INSERT INTO Orders (UserId, Status, Code, Date, DataJson) VALUES (@UserId, @Status, @Code, @Date, @DataJson);
SELECT last_insert_rowid();",
                new { order.UserId, Status = (int)order.Status, order.Code, order.Date, DataJson = Serialize(order) }, tx);
            order.Id = (int)orderId;
            order.Code = $"PX-{100000 + order.Id}";
            conn.Execute("UPDATE Orders SET Code = @Code, DataJson = @DataJson WHERE Id = @Id",
                new { order.Code, DataJson = Serialize(order), order.Id }, tx);

            if (customerCheckout && remainder > 0 && sourceCard is not null)
            {
                InsertTransaction(conn, tx, new Transaction
                {
                    UserId = buyer.Id, UserName = name, Type = TxTypes.OrderPayment,
                    Amount = -(order.Total - order.WalletPaid), Status = TxStatus.Pending, Method = paymentMethod,
                    ReceiptUrl = string.IsNullOrWhiteSpace(payment!.ReceiptUrl) ? null : payment.ReceiptUrl.Trim(),
                    SourceCard = sourceCard.CardNumber, SourceHolder = sourceCard.HolderName,
                    DestinationCard = destMethod?.Value, DestinationHolder = destMethod?.Holder,
                    TrackingNumber = payment.TrackingNumber!.Trim(),
                    PaymentDate = payment.PaymentDate!.Trim(),
                    Description = string.IsNullOrWhiteSpace(payment.Description) ? null : payment.Description.Trim(),
                    OrderCode = order.Code, Date = Today(),
                });
            }

            // recompute and persist the buyer's order stats from the live Orders table (mirrors RefreshUserOrderStats).
            buyer.Orders = conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM Orders WHERE UserId = @id AND Status <> @cancelled",
                new { id = buyer.Id, cancelled = (int)OrderStatus.Cancelled }, tx);
            buyer.TotalSpent = conn.ExecuteScalar<long?>(
                "SELECT SUM(json_extract(DataJson,'$.Total')) FROM Orders WHERE UserId = @id AND Status = @completed",
                new { id = buyer.Id, completed = (int)OrderStatus.Completed }, tx) ?? 0;
            UpsertUser(conn, tx, buyer);

            return new PlaceOrderResult(order, null);
        });
    }

    // ── Discount helpers (transaction-scoped) ───────────────────────────────────────────────────────────

    private static DiscountResult ResolveDiscountTx(SqliteConnection conn, SqliteTransaction? tx, string? code, long subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return new DiscountResult(null, 0, null);
        var json = conn.QueryFirstOrDefault<string>(
            "SELECT DataJson FROM DiscountCodes WHERE Code = @code COLLATE NOCASE LIMIT 1", new { code = code.Trim() }, tx);
        var dc = json is null ? null : Deserialize<DiscountCode>(json);
        if (dc is null || !dc.IsActive) return new DiscountResult(null, 0, "کد تخفیف نامعتبر است.");
        if (dc.ExpiresAt is DateTime exp && DateTime.UtcNow > exp) return new DiscountResult(null, 0, "این کد تخفیف منقضی شده است.");
        if (dc.UsageLimit > 0 && dc.UsedCount >= dc.UsageLimit) return new DiscountResult(null, 0, "ظرفیت این کد تخفیف به پایان رسیده است.");
        if (subtotal < dc.MinOrder) return new DiscountResult(null, 0, "مبلغ سفارش به حد لازم برای این کد نرسیده است.");

        long amount = dc.Type == DiscountType.Percent ? (long)Math.Round(subtotal * dc.Value / 100.0) : dc.Value;
        if (dc.Type == DiscountType.Percent && dc.MaxDiscount > 0) amount = Math.Min(amount, dc.MaxDiscount);
        amount = Math.Clamp(amount, 0, subtotal);
        return new DiscountResult(dc, amount, null);
    }

    private static void ConsumeDiscountTx(SqliteConnection conn, SqliteTransaction tx, DiscountCode dc)
    {
        dc.UsedCount++;
        conn.Execute("UPDATE DiscountCodes SET DataJson = @DataJson WHERE Id = @Id",
            new { DataJson = Serialize(dc), dc.Id }, tx);
    }

    // ── Discount codes (admin CRUD + public resolve) ────────────────────────────────────────────────────
    public IReadOnlyList<DiscountCode> GetDiscountCodes() =>
        AllJson<DiscountCode>("DiscountCodes").OrderByDescending(d => d.Id).ToList();

    public DiscountCode AddDiscountCode(DiscountCode code) =>
        WriteTx((conn, tx) =>
        {
            code.UsedCount = 0;
            var id = (int)conn.ExecuteScalar<long>(
                "INSERT INTO DiscountCodes (Code, DataJson) VALUES (@Code, @DataJson); SELECT last_insert_rowid();",
                new { code.Code, DataJson = Serialize(code) }, tx);
            code.Id = id;
            conn.Execute("UPDATE DiscountCodes SET Code = @Code, DataJson = @d WHERE Id = @id",
                new { code.Code, d = Serialize(code), id }, tx);
            return code;
        });

    // Mirrors StoreData.UpdateDiscountCode: copies the editable fields onto the stored row, preserving UsedCount.
    public bool UpdateDiscountCode(DiscountCode code) =>
        WriteTx((conn, tx) =>
        {
            var ej = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM DiscountCodes WHERE Id = @Id", new { code.Id }, tx);
            if (ej is null) return false;
            var existing = Deserialize<DiscountCode>(ej)!;
            existing.Code = code.Code;
            existing.Type = code.Type;
            existing.Value = code.Value;
            existing.MinOrder = code.MinOrder;
            existing.MaxDiscount = code.MaxDiscount;
            existing.UsageLimit = code.UsageLimit;
            existing.IsActive = code.IsActive;
            existing.ExpiresAt = code.ExpiresAt;
            conn.Execute("UPDATE DiscountCodes SET Code = @Code, DataJson = @d WHERE Id = @id",
                new { existing.Code, d = Serialize(existing), id = existing.Id }, tx);
            return true;
        });

    public bool DeleteDiscountCode(int id) => DeleteRow("DiscountCodes", id);

    // Validates a code against a subtotal WITHOUT consuming it (consumption happens atomically in PlaceOrder).
    // Reuses the transaction-scoped resolver on a plain read connection.
    public DiscountResult ResolveDiscount(string? code, long subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return new DiscountResult(null, 0, null);
        using var conn = OpenConnection();
        return ResolveDiscountTx(conn, null, code, subtotal);
    }

    // ── Order status transitions (atomic refunds + referral earnings) ───────────────────────────────────

    // Stamps the order's invoice number the first time it completes. Random rather than sequential so the
    // number doesn't leak the shop's order count, and re-checked against every existing invoice so it is
    // unique. Runs inside the caller's write transaction, so the number and the Completed status land together.
    private static void EnsureInvoiceNumber(SqliteConnection conn, SqliteTransaction tx, Order o)
    {
        if (!string.IsNullOrWhiteSpace(o.InvoiceNumber)) return; // already issued — never re-issue
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var candidate = NewInvoiceNumber();
            var taken = conn.QueryFirstOrDefault<long?>(
                "SELECT 1 FROM Orders WHERE json_extract(DataJson,'$.InvoiceNumber') = @candidate LIMIT 1",
                new { candidate }, tx);
            if (taken is null) { o.InvoiceNumber = candidate; return; }
        }
        // 16 collisions in a 10^16 space cannot happen by chance; failing loudly beats delivering an order
        // with no invoice number, which the panel's invoice section relies on.
        throw new InvalidOperationException("Could not allocate a unique invoice number.");
    }

    private static string NewInvoiceNumber()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes) % 10_000_000_000_000_000UL;
        return value.ToString("D16", CultureInfo.InvariantCulture);
    }

    private static void UpsertOrder(SqliteConnection conn, SqliteTransaction tx, Order o) =>
        conn.Execute(@"
INSERT INTO Orders (Id, UserId, Status, Code, Date, DataJson)
VALUES (@Id, @UserId, @Status, @Code, @Date, @DataJson)
ON CONFLICT(Id) DO UPDATE SET
    UserId=excluded.UserId, Status=excluded.Status, Code=excluded.Code, Date=excluded.Date, DataJson=excluded.DataJson;",
            new { o.Id, o.UserId, Status = (int)o.Status, o.Code, o.Date, DataJson = Serialize(o) }, tx);

    private static AppUser? LoadUser(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        var j = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id }, tx);
        return j is null ? null : Deserialize<AppUser>(j);
    }

    // Recomputes the buyer's derived order stats from the live Orders table (mirrors RefreshUserOrderStats).
    private static void RefreshUserStats(SqliteConnection conn, SqliteTransaction tx, int userId)
    {
        var u = LoadUser(conn, tx, userId);
        if (u is null) return;
        u.Orders = conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM Orders WHERE UserId = @id AND Status <> @cancelled",
            new { id = userId, cancelled = (int)OrderStatus.Cancelled }, tx);
        u.TotalSpent = conn.ExecuteScalar<long?>(
            "SELECT SUM(json_extract(DataJson,'$.Total')) FROM Orders WHERE UserId = @id AND Status = @completed",
            new { id = userId, completed = (int)OrderStatus.Completed }, tx) ?? 0;
        UpsertUser(conn, tx, u);
    }

    private static void AppendOrderHistory(Order o, OrderStatus from, OrderStatus to, string? changedBy, string? reason) =>
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

    private static void AddNotificationTx(SqliteConnection conn, SqliteTransaction tx, int? userId, string title, string body, string? link)
    {
        var n = new Notification
        {
            UserId = userId, Title = title, Body = body, Link = link, CreatedAtUtc = DateTime.UtcNow.ToString("o"),
        };
        var id = conn.ExecuteScalar<long>(
            "INSERT INTO Notifications (UserId, DataJson) VALUES (@UserId, @DataJson); SELECT last_insert_rowid();",
            new { UserId = userId, DataJson = Serialize(n) }, tx);
        n.Id = (int)id;
        conn.Execute("UPDATE Notifications SET DataJson = @DataJson WHERE Id = @Id", new { DataJson = Serialize(n), n.Id }, tx);
    }

    // Pays the referrer their commission when a referred buyer's order is completed. Runs inside the caller's
    // transaction so the wallet credit + earning record + transaction row commit atomically with the order.
    private static void CreditReferralTx(SqliteConnection conn, SqliteTransaction tx, Order order, PricingSettings settings)
    {
        var buyer = LoadUser(conn, tx, order.UserId);
        if (buyer?.ReferredBy is not int referrerId) return;
        var referrer = LoadUser(conn, tx, referrerId);
        if (referrer is null) return;

        var percent = settings.ReferralCommissionPercent;
        if (percent <= 0) return;
        var commission = (long)Math.Round(order.Total * (double)percent / 100.0, MidpointRounding.AwayFromZero);
        if (commission <= 0) return;

        referrer.Wallet += commission;
        UpsertUser(conn, tx, referrer);

        conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId, @DataJson)",
            new
            {
                ReferrerId = referrerId,
                DataJson = Serialize(new ReferralEarning
                {
                    ReferrerId = referrerId, ReferredName = order.UserName, OrderCode = order.Code,
                    OrderAmount = order.Total, Commission = commission, Date = Today(),
                }),
            }, tx);

        var referrerName = string.IsNullOrWhiteSpace(referrer.Name) ? referrer.Username : referrer.Name;
        InsertTransaction(conn, tx, new Transaction
        {
            UserId = referrerId, UserName = referrerName, Type = TxTypes.Referral, Amount = commission,
            Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "referral", Date = Today(),
        });
    }

    public Order? SetOrderStatus(int id, OrderStatus status, string? changedBy = null, string? reason = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);

            var from = o.Status;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = status;
            if (status == OrderStatus.Completed) { o.DeliveredAtUtc ??= DateTime.UtcNow; EnsureInvoiceNumber(conn, tx, o); }

            // approving the order verifies its linked card-to-card payment too.
            if (status == OrderStatus.Preparing)
            {
                var tj = conn.QueryFirstOrDefault<string>(@"
SELECT DataJson FROM Transactions
WHERE Status = @pending
  AND json_extract(DataJson,'$.OrderCode') = @code
  AND json_extract(DataJson,'$.Type')      = @type
LIMIT 1;",
                    new { pending = (int)TxStatus.Pending, code = o.Code, type = TxTypes.OrderPayment }, tx);
                if (tj is not null)
                {
                    var t = Deserialize<Transaction>(tj)!;
                    t.Status = TxStatus.Approved;
                    conn.Execute("UPDATE Transactions SET Status = @s, DataJson = @d WHERE Id = @Id",
                        new { s = (int)t.Status, d = Serialize(t), t.Id }, tx);
                }
            }

            if (status == OrderStatus.Completed && !wasCompleted) CreditReferralTx(conn, tx, o, settings);
            if (from != status) AppendOrderHistory(o, from, status, changedBy, reason);
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            return o;
        });

    // Records the in-site delivery content for an order and marks it completed (credits referral, stamps the
    // delivery time, notifies the customer) — all atomic.
    public Order? DeliverOrder(int id, string content, string? changedBy = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);

            var from = o.Status;
            o.DeliveryContent = content;
            o.DeliveredAt = Today();
            o.DeliveredAtUtc ??= DateTime.UtcNow;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = OrderStatus.Completed;
            EnsureInvoiceNumber(conn, tx, o);
            if (!wasCompleted) CreditReferralTx(conn, tx, o, settings);
            if (from != OrderStatus.Completed) AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل سفارش");
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            AddNotificationTx(conn, tx, o.UserId, "سفارش شما آماده شد",
                $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
            return o;
        });

    // Cancels an order: restores stock and, if already paid, refunds the wallet minus the cancellation penalty
    // — the stock restore + refund + transaction all commit together (or roll back together).
    public OrderActionResult CancelOrder(int id, string? changedBy = null, string? reason = null) =>
        WriteTx<OrderActionResult>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return new OrderActionResult(null, "سفارش یافت نشد.");
            var o = Deserialize<Order>(oj)!;
            if (o.Status == OrderStatus.Cancelled) return new OrderActionResult(null, "این سفارش قبلاً لغو شده است.");
            if (o.Status == OrderStatus.Completed) return new OrderActionResult(null, "سفارش تکمیل‌شده قابل لغو نیست.");
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);
            var from = o.Status;

            // restore stock
            foreach (var line in o.Items)
            {
                var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @pid", new { pid = line.ProductId }, tx);
                if (pj is null) continue;
                var p = Deserialize<Product>(pj)!;
                p.Stock += line.Quantity;
                UpsertProduct(conn, tx, p);
            }

            // refund what was actually collected (full total once approved, else just the wallet portion).
            var collected = o.Status == OrderStatus.Preparing ? o.Total : o.WalletPaid;
            if (collected > 0)
            {
                var buyer = LoadUser(conn, tx, o.UserId);
                if (buyer is not null)
                {
                    var penalty = settings.CancellationPenaltyPercent;
                    var penaltyAmount = (long)Math.Round(collected * (double)penalty / 100.0, MidpointRounding.AwayFromZero);
                    var refund = Math.Max(0, collected - penaltyAmount);
                    buyer.Wallet += refund;
                    UpsertUser(conn, tx, buyer);

                    var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
                    InsertTransaction(conn, tx, new Transaction
                    {
                        UserId = buyer.Id, UserName = name, Type = TxTypes.Refund, Amount = refund,
                        Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "refund", Date = Today(),
                    });
                }
            }

            o.Status = OrderStatus.Cancelled;
            AppendOrderHistory(o, from, OrderStatus.Cancelled, changedBy, reason ?? "لغو سفارش");
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            return new OrderActionResult(o, null);
        });


    // ── Orders: remaining ───────────────────────────────────────────────────────────────────────────────
    public void RefreshAllUserOrderStats() =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var uid in conn.Query<int>("SELECT Id FROM Users", transaction: tx).ToList())
                RefreshUserStats(conn, tx, uid);
            return null;
        });

    public Order? SaveUnitDraft(int orderId, int unitId, string content, string? changedBy = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id=@orderId", new { orderId }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var unit = o.Units.FirstOrDefault(u => u.Id == unitId);
            if (unit is null || unit.Delivered) return null;
            unit.DeliveryContent = content; unit.HandledBy = changedBy;
            UpsertOrder(conn, tx, o);
            return o;
        });

    public (Order? order, bool justCompleted) DeliverUnit(int orderId, int unitId, string content, string? changedBy = null) =>
        WriteTx<(Order?, bool)>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id=@orderId", new { orderId }, tx);
            if (oj is null) return (null, false);
            var o = Deserialize<Order>(oj)!;
            var unit = o.Units.FirstOrDefault(u => u.Id == unitId);
            if (unit is null) return (null, false);

            unit.DeliveryContent = content; unit.HandledBy = changedBy;
            if (!unit.Delivered) { unit.Delivered = true; unit.DeliveredAt = Today(); unit.DeliveredAtUtc = DateTime.UtcNow; }

            var justCompleted = false;
            if (o.Units.Count > 0 && o.Units.All(u => u.Delivered) && o.Status != OrderStatus.Completed)
            {
                var from = o.Status;
                o.DeliveryContent = string.Join("\n\n", o.Units.OrderBy(u => u.UnitIndex)
                    .Select(u => o.Units.Count > 1 ? $"اکانت {u.UnitIndex}:\n{u.DeliveryContent}" : u.DeliveryContent));
                o.DeliveredAt = Today(); o.DeliveredAtUtc ??= DateTime.UtcNow; o.Status = OrderStatus.Completed;
                EnsureInvoiceNumber(conn, tx, o);
                CreditReferralTx(conn, tx, o, ReadSingleton<PricingSettings>(conn, tx, PricingKey));
                AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل همه‌ی اکانت‌ها");
                UpsertOrder(conn, tx, o);
                RefreshUserStats(conn, tx, o.UserId);
                AddNotificationTx(conn, tx, o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
                justCompleted = true;
            }
            else UpsertOrder(conn, tx, o);
            return (o, justCompleted);
        });

    // Atomic claim: the stamp is written inside the write transaction, so two concurrent approvals of the same
    // order can never both win and post the accounts twice.
    public bool TryClaimOrderBotNotification(int orderId) =>
        WriteTx((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id = orderId }, tx);
            if (oj is null) return false;
            var o = Deserialize<Order>(oj)!;
            if (o.OrderBotNotifiedAtUtc is not null) return false;
            o.OrderBotNotifiedAtUtc = DateTime.UtcNow;
            UpsertOrder(conn, tx, o);
            return true;
        });

    public IReadOnlyList<RenewalReminder> CollectDueRenewalReminders(int hoursBefore)
    {
        var due = new List<RenewalReminder>();
        if (hoursBefore <= 0) return due;
        WriteTx<object?>((conn, tx) =>
        {
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromHours(hoursBefore);
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.Completed }, tx).ToList())
            {
                var o = Deserialize<Order>((string)row.DataJson)!;
                if (o.DeliveredAtUtc is not DateTime delivered) continue;
                if (o.RenewalReminderSentUtc is not null) continue;
                var months = o.Items.Where(i => i.PlanMonths is int m && m > 0).Select(i => i.PlanMonths!.Value).DefaultIfEmpty(0).Max();
                if (months <= 0) continue;
                var expires = delivered.AddMonths(months);
                var remaining = expires - now;
                if (remaining <= TimeSpan.Zero || remaining > window) continue;

                var user = LoadUser(conn, tx, o.UserId);
                if (user is null) continue;

                o.RenewalReminderSentUtc = now;
                var expiresFa = JalaliDate(expires);
                conn.Execute("UPDATE Orders SET DataJson=@d WHERE Id=@id", new { d = Serialize(o), id = (long)row.Id }, tx);
                AddNotificationTx(conn, tx, user.Id, "یادآوری تمدید اشتراک",
                    $"اشتراک سفارش {o.Code} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.", "/account/orders");
                due.Add(new RenewalReminder(user.Id, user.Email, o.Code, expiresFa));
            }
            return null;
        });
        return due;
    }

    public IReadOnlyList<ReferralEarning> GetReferralEarnings(int referrerId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM ReferralEarnings WHERE ReferrerId=@referrerId", new { referrerId })
            .Select(j => Deserialize<ReferralEarning>(j)!).OrderByDescending(e => e.Date).ToList();
    }

    public int CountReferredUsers(int referrerId)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Users WHERE ReferredBy=@referrerId", new { referrerId });
    }

    private static string JalaliDate(DateTime dt)
    {
        var pc = new System.Globalization.PersianCalendar();
        var s = $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }
}
