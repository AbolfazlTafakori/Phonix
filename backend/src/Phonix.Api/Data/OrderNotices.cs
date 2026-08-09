using Phonix.Api.Models;

namespace Phonix.Api.Data;

// One notification as the customer will read it.
public readonly record struct Notice(string Title, string Body, string Link);

// The customer-facing copy raised by order events.
//
// The events themselves stay inside the store's transaction on purpose: a refund credits the wallet, writes
// its audit transaction and notifies the buyer as ONE atomic write, so a crash can never leave money moved
// with no record of why. What does not belong down there is the wording — composing Persian sentences is
// presentation, not persistence. The store now asks this for the text and writes what it is handed, which is
// also what makes the copy reviewable in one place instead of buried across a 900-line data file.
public static class OrderNotices
{
    private const string OrdersLink = "/account/orders";
    private const string WalletLink = "/account/wallet";

    public static Notice Ready(Order order) =>
        new("سفارش شما آماده شد", $"سفارش {order.Code} آماده و قابل مشاهده در حساب شماست.", OrdersLink);

    public static Notice Cancelled(Order order) =>
        new("سفارش لغو شد", $"همه‌ی اقلام سفارش {order.Code} رد شد و مبلغ آن‌ها بازگشت داده شد.", OrdersLink);

    public static Notice UnitRefunded(Order order, OrderUnit unit, long refund) =>
        new("بازگشت وجه",
            $"«{unit.Name}» از سفارش {order.Code} رد شد و {refund:N0} تومان به کیف پول شما بازگشت.",
            WalletLink);

    public static Notice RenewalDue(Order order, string expiresFa) =>
        new("یادآوری تمدید اشتراک",
            $"اشتراک سفارش {order.Code} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.",
            OrdersLink);

    // A V2Ray service running low on time, on traffic, or on both. It links straight to the config page,
    // where the live numbers are and where the renew button is — not to the order, which says less.
    public static Notice V2RayRunningOut(string orderCode, string token, string? expiresFa, string? remainingFa)
    {
        var body = (expiresFa, remainingFa) switch
        {
            (not null, not null) => $"سرویس سفارش {orderCode}: اعتبار زمانی تا {expiresFa} و تنها {remainingFa} حجم باقی مانده است.",
            (not null, null) => $"سرویس سفارش {orderCode} در تاریخ {expiresFa} به پایان می‌رسد.",
            (null, not null) => $"از حجم سرویس سفارش {orderCode} تنها {remainingFa} باقی مانده است.",
            _ => $"سرویس سفارش {orderCode} رو به پایان است.",
        };
        return new("سرویس شما رو به پایان است", $"{body} برای جلوگیری از قطع شدن، همین حالا تمدید کنید.", $"/config/{token}");
    }

    public static Notice V2RayRemoved(string orderCode) =>
        new("سرویس شما پایان یافت",
            $"سرویس سفارش {orderCode} تمدید نشد و از سرور حذف شد. برای ادامه، سرویس جدیدی تهیه کنید.",
            OrdersLink);
}
