using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Customer-facing transactional mail, in one place. Two reasons this isn't just IEmailSender calls at each
// call site: a decision can arrive from either the admin panel or the Telegram receipt bot and must send the
// same mail either way, and none of these events may ever fail their own request because mail is down — every
// method here swallows and logs instead of throwing, so callers can fire-and-forget.
public interface IUserMailer
{
    Task WelcomeAsync(AppUser user);
    Task LoginNoticeAsync(AppUser user, string ip, string device);
    Task OrderPlacedAsync(Order order);
    // One mail per delivered account, so a five-account order tells the customer about each purchase as it
    // lands rather than in one lump.
    Task OrderUnitDeliveredAsync(Order order, int unitId);
    // The wrap-up once every account in the order is delivered: the exact list + the issued invoice number.
    Task OrderCompletedAsync(Order order);
    // Approved or rejected, wallet top-up or order payment — the transaction's own state picks the mail.
    Task TransactionDecidedAsync(Transaction tx);
    Task TicketRepliedAsync(Ticket ticket);
    Task TicketOpenedByStaffAsync(Ticket ticket);
    Task CardDecidedAsync(BankCard card);
    Task KycDecidedAsync(KycRequest kyc);
    // Only rejection mails: an approved seat needs nothing from the customer, a rejected one needs them back.
    Task SeatInfoRejectedAsync(SeatSubmission submission);
    // A seat reopened for the customer to file new details; message is what staff wrote (or the default).
    Task SeatInfoReopenedAsync(SeatSubmission submission, string message);
    // Staff turned an order down (a rejected receipt, a cancellation from the panel). The reason goes out in full.
    Task OrderCancelledAsync(Order order, string reason, bool refunded);
    // One account of an order was rejected and refunded — from the panel or the Telegram order bot.
    Task OrderUnitRejectedAsync(Order order, int unitId, string reason, long refunded);
    // A message staff sent one customer from the panel's notifications section.
    Task StaffMessageAsync(int userId, string title, string body, string? link);
    // The same, to every customer with a verified address. Paced, and meant to be run in the background:
    // returns once the last one has gone. Returns how many were attempted.
    Task<int> BroadcastStaffMessageAsync(string title, string body, string? link, CancellationToken ct = default);
    // The one-time code for linking Telegram. Email only — never to a chat — and the caller is told whether it
    // went, because without it the customer has nothing to send the bot.
    Task<bool> TelegramLinkCodeAsync(AppUser user, string code, int minutes, string botUsername);
    // Support's answer in the live chat, to the customer's Telegram (linked, or the account they bought with in the
    // bot). Telegram only: someone who wrote from the site reads it there, and the chat isn't email.
    Task SupportReplyAsync(int userId, string body);
}

public sealed class UserMailer : IUserMailer
{
    private readonly IDataStore _store;
    private readonly IEmailSender _email;
    private readonly ILogger<UserMailer> _logger;
    private readonly ITelegramCustomerBot? _telegram;

    public UserMailer(IDataStore store, IEmailSender email, ILogger<UserMailer> logger, ITelegramCustomerBot? telegram = null)
    {
        _store = store;
        _email = email;
        _logger = logger;
        _telegram = telegram;
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    private static string Url(string path) => $"{FrontendUrl}{path}";

    // Where a customer actually reaches us: the site has no /support page, tickets are the support channel.
    private static string SupportUrl => Url("/account/tickets");

    // Resolves the owner's address. Returns null for a user with no email on file (the phone-only signup
    // path), which is a normal skip, not a failure.
    private string? AddressOf(int userId) =>
        _store.GetUser(userId) is { Email: { Length: > 0 } email } ? email : null;

    // One customer, every channel they have: the email, and — when they linked the customer bot and left its
    // notifications on — the same words in Telegram. Each channel fails on its own; neither ever throws.
    // orderId: what the notice is about, so the Telegram copy carries a button that opens that order in the bot.
    private async Task SendToUserAsync(int userId, string subject, (string text, string html) body, int? orderId = null)
    {
        var user = _store.GetUser(userId);
        if (user is null) return;
        await SendAsync(user.Email, subject, body);
        await SendTelegramAsync(user, subject, body.text, orderId);
    }

    private async Task SendTelegramAsync(AppUser user, string subject, string text, int? orderId = null)
    {
        // A linked site account, or the Telegram-only account a customer bought with inside the bot.
        if (_telegram is null || (user.TelegramChatId ?? user.TelegramGuestChatId) is not long chatId || !user.TelegramNotify) return;
        try
        {
            var message = $"📩 {subject.Trim()}\n\n{text.Trim()}";
            if (orderId is int id) await _telegram.NotifyOrderAsync(user, message, id);
            else await _telegram.SendAsync(chatId, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer Telegram message failed: {Subject}", subject);
        }
    }

    private async Task SendAsync(string? to, string subject, (string text, string html) body)
    {
        if (string.IsNullOrWhiteSpace(to)) return;
        try
        {
            await _email.SendAsync(to!, subject, body.text, body.html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer email failed: {Subject}", subject);
        }
    }

    // 6037991234567890 → "6037 99** **** 7890". Enough for the owner to recognise the card, not enough to
    // reconstruct it if the mailbox is later exposed.
    private static string MaskCard(string? card)
    {
        var digits = new string((card ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 16) return string.IsNullOrWhiteSpace(card) ? "-" : card!;
        return $"{digits[..4]} {digits[4..6]}** **** {digits[^4..]}";
    }

    public async Task SupportReplyAsync(int userId, string body)
    {
        if (_store.GetUser(userId) is not { } user) return;
        await SendTelegramAsync(user, "پاسخ پشتیبانی", body);
    }

    public async Task<bool> TelegramLinkCodeAsync(AppUser user, string code, int minutes, string botUsername)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return false;
        var (text, html) = EmailTemplates.TelegramLinkCode(code, minutes, botUsername, $"https://t.me/{botUsername}?start=connect");
        try
        {
            return await _email.SendAsync(user.Email, "کد اتصال حساب فونیکس به تلگرام", text, html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram link code email failed for user {UserId}", user.Id);
            return false;
        }
    }

    public Task WelcomeAsync(AppUser user) =>
        SendToUserAsync(user.Id, $"به فونیکس وریفای خوش آمدید، {user.Name}",
            EmailTemplates.Welcome(user.Name, Url("/products")));

    public Task LoginNoticeAsync(AppUser user, string ip, string device) =>
        SendToUserAsync(user.Id, "ورود به حساب فونیکس شما",
            EmailTemplates.LoginNotice(JalaliDate.NowFa(), ip, device, Url("/change-password")));

    public Task OrderPlacedAsync(Order order) =>
        SendToUserAsync(order.UserId, $"سفارش {order.Code} ثبت شد",
            EmailTemplates.OrderPlaced(order.Code, order.Total, JalaliDate.NowFa(), Url("/account/orders"),
                awaitingPayment: order.Status == OrderStatus.PendingApproval), order.Id);

    public Task OrderUnitDeliveredAsync(Order order, int unitId)
    {
        var unit = order.Units.FirstOrDefault(u => u.Id == unitId);
        if (unit is null) return Task.CompletedTask;
        return SendToUserAsync(order.UserId, $"{unit.Name} آماده شد — سفارش {order.Code}",
            EmailTemplates.OrderUnitDelivered(order.Code, unit.Name, unit.Plan, unit.UnitIndex, order.Units.Count,
                unit.DeliveryContent, Url("/account/orders")), order.Id);
    }

    public Task OrderCompletedAsync(Order order)
    {
        var lines = order.Items.Select(i => (i.Name, i.Plan, i.Quantity)).ToList();
        return SendToUserAsync(order.UserId, $"سفارش {order.Code} تکمیل شد",
            EmailTemplates.OrderCompleted(order.Code, order.InvoiceNumber, lines, Url("/account/orders")), order.Id);
    }

    public Task TransactionDecidedAsync(Transaction tx)
    {
        if (tx.Status == TxStatus.Rejected)
        {
            var kindFa = tx.Type == TxTypes.OrderPayment ? "پرداخت سفارش" : "واریز";
            return SendToUserAsync(tx.UserId, $"{kindFa} شما تأیید نشد",
                EmailTemplates.PaymentRejected(kindFa, tx.Amount, tx.Note, SupportUrl), OrderIdOf(tx));
        }
        if (tx.Status != TxStatus.Approved) return Task.CompletedTask;

        if (tx.Type == TxTypes.WalletTopUp)
        {
            // Read the balance back rather than computing it here, so the mail always states what the store
            // actually holds after the credit.
            var balance = _store.GetUser(tx.UserId)?.Wallet ?? 0;
            return SendToUserAsync(tx.UserId, "کیف پول شما شارژ شد",
                EmailTemplates.WalletToppedUp(tx.Amount, balance, Url("/account/wallet")));
        }
        if (tx.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(tx.OrderCode))
            return SendToUserAsync(tx.UserId, $"پرداخت سفارش {tx.OrderCode} تأیید شد",
                EmailTemplates.OrderPaymentApproved(tx.OrderCode!, tx.Amount, Url("/account/orders")), OrderIdOf(tx));

        return Task.CompletedTask;
    }

    public Task TicketRepliedAsync(Ticket ticket) =>
        SendToUserAsync(ticket.UserId, $"پاسخ پشتیبانی — {ticket.Subject}",
            EmailTemplates.TicketReplied(ticket.Code, ticket.Subject, Url("/account/tickets")));

    public Task TicketOpenedByStaffAsync(Ticket ticket) =>
        SendToUserAsync(ticket.UserId, $"تیکت جدید از پشتیبانی — {ticket.Subject}",
            EmailTemplates.TicketOpenedByStaff(ticket.Code, ticket.Subject, Url("/account/tickets")));

    public Task CardDecidedAsync(BankCard card)
    {
        var masked = MaskCard(card.CardNumber);
        return card.Status switch
        {
            BankCardStatus.Approved => SendToUserAsync(card.UserId, "کارت بانکی شما تأیید شد",
                EmailTemplates.CardApproved(masked, Url("/account/cards"))),
            BankCardStatus.Rejected => SendToUserAsync(card.UserId, "کارت بانکی شما تأیید نشد",
                EmailTemplates.CardRejected(masked, card.RejectionReason ?? card.Note, Url("/account/cards"))),
            _ => Task.CompletedTask,
        };
    }

    public Task SeatInfoRejectedAsync(SeatSubmission s)
    {
        var seat = string.IsNullOrWhiteSpace(s.SeatLabel) ? $"پروفایل {s.SeatIndex + 1}" : s.SeatLabel;
        return SendToUserAsync(s.UserId, $"اطلاعات ارسالی شما تأیید نشد — سفارش {s.OrderCode}",
            EmailTemplates.SeatInfoRejected(s.OrderCode, s.ProductName, seat, s.ReviewNote, Url("/account/orders")));
    }

    public Task OrderCancelledAsync(Order order, string reason, bool refunded) =>
        SendToUserAsync(order.UserId, $"سفارش {order.Code} لغو شد",
            EmailTemplates.OrderCancelled(order.Code, reason, refunded, Url("/account/orders")), order.Id);

    public Task OrderUnitRejectedAsync(Order order, int unitId, string reason, long refunded)
    {
        var name = order.Units.FirstOrDefault(u => u.Id == unitId)?.Name ?? "بخشی از سفارش";
        return SendToUserAsync(order.UserId, $"بخشی از سفارش {order.Code} رد شد",
            EmailTemplates.OrderUnitRejected(order.Code, name, reason, refunded, Url("/account/wallet")), order.Id);
    }

    // The order a payment was for, when it was for one.
    private int? OrderIdOf(Transaction tx) =>
        tx.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(tx.OrderCode)
            ? _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode)?.Id
            : null;

    // The notification's own in-site link when it has one, otherwise the customer's notifications list.
    private static string MessageUrl(string? link) => Url(string.IsNullOrWhiteSpace(link) ? "/account/messages" : link!);

    public Task StaffMessageAsync(int userId, string title, string body, string? link) =>
        SendToUserAsync(userId, title.Trim(), EmailTemplates.StaffMessage(title, body, MessageUrl(link)));

    // A broadcast can reach the whole customer base. Sending it in one burst is how a shop's mail server ends
    // up on a blocklist, and it would hold a request open for minutes — so it is paced, and only addresses the
    // customer proved they own are written to.
    public static readonly TimeSpan BroadcastPace = TimeSpan.FromMilliseconds(400);

    public async Task<int> BroadcastStaffMessageAsync(string title, string body, string? link, CancellationToken ct = default)
    {
        var mail = EmailTemplates.StaffMessage(title, body, MessageUrl(link));
        var recipients = _store.GetUsers()
            .Where(u => u.Role == UserRole.Customer && !u.Blocked && u.EmailVerified && u.Email.Length > 0)
            .Select(u => u.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sent = 0;
        foreach (var to in recipients)
        {
            if (ct.IsCancellationRequested) break;
            await SendAsync(to, title.Trim(), mail);
            sent++;
            try { await Task.Delay(BroadcastPace, ct); } catch (OperationCanceledException) { break; }
        }
        _logger.LogInformation("Broadcast \"{Title}\" emailed to {Sent} of {Total} customers", title, sent, recipients.Count);

        if (_telegram is not null && _telegram.IsActive)
        {
            var linked = _store.GetUsers()
                .Where(u => u.Role == UserRole.Customer && !u.Blocked && u.TelegramChatId is not null && u.TelegramNotify)
                .ToList();
            foreach (var u in linked)
            {
                if (ct.IsCancellationRequested) break;
                await SendTelegramAsync(u, title, mail.text);
                try { await Task.Delay(BroadcastPace, ct); } catch (OperationCanceledException) { break; }
            }
            _logger.LogInformation("Broadcast \"{Title}\" sent to {Count} linked Telegram chats", title, linked.Count);
        }
        return sent;
    }

    public Task SeatInfoReopenedAsync(SeatSubmission s, string message)
    {
        var seat = string.IsNullOrWhiteSpace(s.SeatLabel) ? $"پروفایل {s.SeatIndex + 1}" : s.SeatLabel;
        return SendToUserAsync(s.UserId, $"مشخصات دستگاه خود را به‌روز کنید — سفارش {s.OrderCode}",
            EmailTemplates.SeatInfoReopened(s.OrderCode, s.ProductName, seat, message, Url("/account/orders")));
    }

    public Task KycDecidedAsync(KycRequest kyc) => kyc.Status switch
    {
        KycStatus.Approved => SendToUserAsync(kyc.UserId, "احراز هویت شما تأیید شد",
            EmailTemplates.KycApproved(Url("/account"))),
        KycStatus.Rejected => SendToUserAsync(kyc.UserId, "احراز هویت شما تأیید نشد",
            EmailTemplates.KycRejected(kyc.RejectionReason ?? kyc.Note, Url("/account/kyc"))),
        _ => Task.CompletedTask,
    };
}
