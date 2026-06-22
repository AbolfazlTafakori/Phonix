using System.Net;

namespace Phonix.Api.Services;

// Branded, RTL HTML wrappers for transactional emails. Each builder returns both a plain-text body
// (the fallback) and an HTML body, so callers pass them straight to IEmailSender.SendAsync(to, subj, text, html).
public static class EmailTemplates
{
    private const string Brand = "فونیکس ورفای";
    private const string Accent = "#e60053";

    // Wraps inner HTML in a responsive, email-client-safe RTL shell (table layout, inline styles).
    private static string Shell(string title, string innerHtml) => $@"<!DOCTYPE html>
<html lang=""fa"" dir=""rtl"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f3f4f6;font-family:Tahoma,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f3f4f6;padding:24px 0;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0"" style=""max-width:560px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.06);"">
        <tr><td style=""background:{Accent};padding:24px 32px;text-align:center;"">
          <span style=""color:#ffffff;font-size:22px;font-weight:bold;letter-spacing:.5px;"">{Brand}</span>
        </td></tr>
        <tr><td style=""padding:32px;color:#1f2937;font-size:15px;line-height:2;"">
          <h1 style=""margin:0 0 16px;font-size:19px;color:#111827;"">{WebUtility.HtmlEncode(title)}</h1>
          {innerHtml}
        </td></tr>
        <tr><td style=""padding:20px 32px;background:#f9fafb;color:#9ca3af;font-size:12px;text-align:center;border-top:1px solid #f0f0f0;"">
          این ایمیل به‌صورت خودکار از سوی {Brand} ارسال شده است. لطفاً به آن پاسخ ندهید.
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>";

    private static string Button(string text, string url) => $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0;""><tr><td style=""border-radius:10px;background:{Accent};"">
      <a href=""{WebUtility.HtmlEncode(url)}"" style=""display:inline-block;padding:13px 34px;color:#ffffff;font-size:15px;font-weight:bold;text-decoration:none;border-radius:10px;"">{WebUtility.HtmlEncode(text)}</a>
    </td></tr></table>";

    // Small grey line for showing the raw link as a copy-paste fallback under the button.
    private static string LinkFallback(string url) =>
        $@"<p style=""margin:8px 0 0;color:#9ca3af;font-size:12px;word-break:break-all;"">یا این نشانی را کپی کنید:<br>{WebUtility.HtmlEncode(url)}</p>";

    public static (string text, string html) VerifyEmail(string link)
    {
        var text = $"به فونیکس ورفای خوش آمدید!\nبرای فعال‌سازی حساب خود روی این لینک کلیک کنید (تا ۲ روز معتبر است):\n{link}";
        var html = Shell("به فونیکس ورفای خوش آمدید 🎉",
            "<p>برای فعال‌سازی حساب کاربری خود و شروع خرید، روی دکمه‌ی زیر کلیک کنید. این لینک تا ۲ روز معتبر است.</p>"
            + Button("فعال‌سازی حساب", link) + LinkFallback(link));
        return (text, html);
    }

    public static (string text, string html) ResetPassword(string link)
    {
        var text = $"درخواست بازنشانی گذرواژه دریافت شد.\nبرای تعیین گذرواژه جدید روی این لینک کلیک کنید (تا ۱ ساعت معتبر است):\n{link}\nاگر شما این درخواست را نداده‌اید، این ایمیل را نادیده بگیرید.";
        var html = Shell("بازنشانی گذرواژه",
            "<p>درخواستی برای بازنشانی گذرواژه‌ی حساب شما دریافت شد. برای تعیین گذرواژه‌ی جدید روی دکمه‌ی زیر کلیک کنید. این لینک تا ۱ ساعت معتبر است.</p>"
            + Button("تعیین گذرواژه جدید", link) + LinkFallback(link)
            + "<p style=\"margin-top:20px;color:#6b7280;font-size:13px;\">اگر شما این درخواست را نداده‌اید، می‌توانید این ایمیل را نادیده بگیرید.</p>");
        return (text, html);
    }

    public static (string text, string html) OrderDelivered(string orderCode, string accountUrl, string? customMessage)
    {
        var message = string.IsNullOrWhiteSpace(customMessage)
            ? "سفارش شما آماده شد. برای مشاهده‌ی اطلاعات سرویس به حساب کاربری خود، بخش سفارش‌ها مراجعه کنید."
            : customMessage!;
        var text = $"سفارش {orderCode} آماده شد.\n{message}\n{accountUrl}";
        var html = Shell($"سفارش {orderCode} آماده شد ✅",
            $"<p>{WebUtility.HtmlEncode(message).Replace("\n", "<br>")}</p>"
            + Button("مشاهده‌ی سفارش‌ها", accountUrl));
        return (text, html);
    }

    public static (string text, string html) SubscriptionReminder(string orderCode, string expiresFa, string renewUrl)
    {
        var text = $"یادآوری تمدید اشتراک\nاشتراک سفارش {orderCode} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.\n{renewUrl}";
        var html = Shell("یادآوری تمدید اشتراک ⏳",
            $"<p>اشتراک سفارش <b>{WebUtility.HtmlEncode(orderCode)}</b> شما به‌زودی و در تاریخ <b>{WebUtility.HtmlEncode(expiresFa)}</b> منقضی می‌شود. برای اینکه سرویس شما بدون وقفه ادامه پیدا کند، همین حالا آن را تمدید کنید.</p>"
            + Button("تمدید اشتراک", renewUrl));
        return (text, html);
    }
}
