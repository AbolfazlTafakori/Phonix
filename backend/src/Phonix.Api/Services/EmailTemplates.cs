using System.Net;

namespace Phonix.Api.Services;

// Branded, RTL HTML wrappers for transactional emails. Each builder returns both a plain-text body
// (the fallback) and an HTML body, so callers pass them straight to IEmailSender.SendAsync(to, subj, text, html).
public static class EmailTemplates
{
    private const string Brand = "فونیکس ورفای";
    private const string Site = "phoenixverify.com";
    private const string SupportUrl = "https://phoenixverify.com/support";
    private const string Accent = "#e60053";
    private const string AccentDark = "#b80043";

    // Email-safe font stack. Clients that allow web fonts (Apple Mail, some others) pull Vazirmatn for a
    // clean Persian look; Gmail/Outlook fall back to the best installed Persian-friendly system font.
    private const string FontStack = "'Vazirmatn','Segoe UI','IRANSansX',-apple-system,BlinkMacSystemFont,'Helvetica Neue',Tahoma,Arial,sans-serif";

    // Wraps inner HTML in a responsive, email-client-safe RTL shell (table layout, inline styles).
    // preheader is the short grey snippet inbox lists show next to the subject.
    private static string Shell(string title, string preheader, string innerHtml) => $@"<!DOCTYPE html>
<html lang=""fa"" dir=""rtl"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""color-scheme"" content=""light"">
  <link href=""https://fonts.googleapis.com/css2?family=Vazirmatn:wght@400;500;700&display=swap"" rel=""stylesheet"">
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Vazirmatn:wght@400;500;700&display=swap');
    body {{ margin:0; padding:0; }}
    a {{ text-decoration:none; }}
  </style>
</head>
<body style=""margin:0;padding:0;background:#eef1f5;font-family:{FontStack};"">
  <div style=""display:none;max-height:0;overflow:hidden;opacity:0;color:#eef1f5;font-size:1px;line-height:1px;"">{WebUtility.HtmlEncode(preheader)}</div>
  <table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef1f5;padding:32px 12px;direction:rtl;"">
    <tr><td align=""center"">
      <table role=""presentation"" dir=""rtl"" width=""580"" cellpadding=""0"" cellspacing=""0"" style=""max-width:580px;width:100%;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 6px 32px rgba(17,24,39,0.08);direction:rtl;"">
        <tr><td style=""background:linear-gradient(135deg,{Accent},{AccentDark});padding:36px 32px;text-align:center;"">
          <span style=""color:#ffffff;font-family:{FontStack};font-size:24px;font-weight:700;letter-spacing:.3px;"">{Brand}</span>
        </td></tr>
        <tr><td dir=""rtl"" align=""right"" style=""padding:40px 40px 32px;color:#374151;font-family:{FontStack};font-size:16px;line-height:2.1;direction:rtl;text-align:right;"">
          <h1 style=""margin:0 0 20px;font-family:{FontStack};font-size:22px;font-weight:700;color:#111827;line-height:1.6;text-align:right;"">{WebUtility.HtmlEncode(title)}</h1>
          {innerHtml}
        </td></tr>
        <tr><td style=""padding:24px 40px 32px;background:#f8fafc;border-top:1px solid #eef1f5;text-align:center;font-family:{FontStack};"">
          <a href=""{SupportUrl}"" style=""color:{Accent};font-size:13px;font-weight:500;"">پشتیبانی</a>
          <span style=""color:#d1d5db;font-size:13px;"">&nbsp;•&nbsp;</span>
          <a href=""https://{Site}"" style=""color:{Accent};font-size:13px;font-weight:500;"">{Site}</a>
          <p style=""margin:14px 0 0;color:#9ca3af;font-size:12px;line-height:1.9;"">این ایمیل به‌صورت خودکار از سوی {Brand} ارسال شده است؛ لطفاً به آن پاسخ ندهید.<br>© {DateTime.Now.Year} {Brand}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>";

    private static string Button(string text, string url) => $@"<table role=""presentation"" align=""center"" cellpadding=""0"" cellspacing=""0"" style=""margin:28px auto;""><tr><td align=""center"" style=""border-radius:12px;background:{Accent};box-shadow:0 4px 14px rgba(230,0,83,0.30);"">
      <a href=""{WebUtility.HtmlEncode(url)}"" style=""display:inline-block;padding:15px 44px;color:#ffffff;font-family:{FontStack};font-size:16px;font-weight:700;border-radius:12px;"">{WebUtility.HtmlEncode(text)}</a>
    </td></tr></table>";

    // Small grey line for showing the raw link as a copy-paste fallback under the button.
    private static string LinkFallback(string url) =>
        $@"<p dir=""rtl"" style=""margin:18px 0 0;color:#9ca3af;font-size:13px;line-height:1.9;direction:rtl;text-align:right;"">اگر دکمه کار نکرد، این نشانی را در مرورگر باز کنید:</p>
        <p dir=""ltr"" style=""margin:6px 0 0;word-break:break-all;direction:ltr;text-align:left;""><a href=""{WebUtility.HtmlEncode(url)}"" style=""color:{Accent};font-size:13px;"">{WebUtility.HtmlEncode(url)}</a></p>";

    // A soft tinted note box for secondary / security messages under the main content.
    private static string Note(string html) =>
        $@"<table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0 0;direction:rtl;""><tr>
        <td dir=""rtl"" align=""right"" style=""background:#f8fafc;border:1px solid #eef1f5;border-radius:12px;padding:14px 18px;color:#6b7280;font-size:13px;line-height:1.95;direction:rtl;text-align:right;"">{html}</td>
        </tr></table>";

    public static (string text, string html) VerifyEmail(string link)
    {
        var text = $"به {Brand} خوش آمدید!\n\nتنها یک قدم تا فعال‌سازی حساب شما باقی مانده است. برای تأیید ایمیل و شروع خرید، این نشانی را باز کنید (تا ۱ ساعت معتبر است):\n{link}\n\nاگر شما در {Brand} ثبت‌نام نکرده‌اید، این ایمیل را نادیده بگیرید.";
        var html = Shell("به فونیکس ورفای خوش آمدید 🎉",
            "تنها یک قدم تا فعال‌سازی حساب شما باقی مانده است.",
            "<p style=\"margin:0;\">از اینکه به <b>فونیکس ورفای</b> پیوستید خوشحالیم. تنها یک قدم باقی مانده — برای فعال‌سازی حساب و شروع خرید، روی دکمه‌ی زیر بزنید.</p>"
            + Button("فعال‌سازی حساب", link)
            + LinkFallback(link)
            + Note("این لینک تا <b>۱ ساعت</b> معتبر است. اگر شما در فونیکس ورفای ثبت‌نام نکرده‌اید، می‌توانید با خیال راحت این ایمیل را نادیده بگیرید."));
        return (text, html);
    }

    public static (string text, string html) ResetPassword(string link)
    {
        var text = $"بازنشانی گذرواژه {Brand}\n\nدرخواستی برای تعیین گذرواژه‌ی جدید حساب شما دریافت شد. برای ادامه این نشانی را باز کنید (تا ۱ ساعت معتبر است):\n{link}\n\nاگر شما این درخواست را نداده‌اید، این ایمیل را نادیده بگیرید؛ گذرواژه‌ی شما تغییری نمی‌کند.";
        var html = Shell("بازنشانی گذرواژه",
            "درخواست تعیین گذرواژه‌ی جدید برای حساب شما.",
            "<p style=\"margin:0;\">درخواستی برای بازنشانی گذرواژه‌ی حساب شما دریافت کردیم. برای تعیین گذرواژه‌ی جدید روی دکمه‌ی زیر بزنید.</p>"
            + Button("تعیین گذرواژه جدید", link)
            + LinkFallback(link)
            + Note("این لینک تا <b>۱ ساعت</b> معتبر است. اگر شما این درخواست را نداده‌اید، نگران نباشید — کافی است این ایمیل را نادیده بگیرید و گذرواژه‌ی شما بدون تغییر می‌ماند."));
        return (text, html);
    }

    public static (string text, string html) OrderDelivered(string orderCode, string accountUrl, string? customMessage)
    {
        var message = string.IsNullOrWhiteSpace(customMessage)
            ? "سفارش شما آماده شد. برای مشاهده‌ی اطلاعات سرویس، به بخش سفارش‌ها در حساب کاربری خود مراجعه کنید."
            : customMessage!;
        var text = $"سفارش {orderCode} آماده شد.\n\n{message}\n{accountUrl}";
        var html = Shell($"سفارش {orderCode} آماده شد ✅",
            "سفارش شما آماده‌ی استفاده است.",
            $"<p style=\"margin:0;\">{WebUtility.HtmlEncode(message).Replace("\n", "<br>")}</p>"
            + Button("مشاهده‌ی سفارش‌ها", accountUrl));
        return (text, html);
    }

    public static (string text, string html) SubscriptionReminder(string orderCode, string expiresFa, string renewUrl)
    {
        var text = $"یادآوری تمدید اشتراک\n\nاشتراک سفارش {orderCode} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.\n{renewUrl}";
        var html = Shell("یادآوری تمدید اشتراک ⏳",
            $"اشتراک سفارش {orderCode} شما به‌زودی منقضی می‌شود.",
            $"<p style=\"margin:0;\">اشتراک سفارش <b>{WebUtility.HtmlEncode(orderCode)}</b> شما در تاریخ <b>{WebUtility.HtmlEncode(expiresFa)}</b> منقضی می‌شود. برای اینکه سرویس شما بدون وقفه ادامه پیدا کند، همین حالا آن را تمدید کنید.</p>"
            + Button("تمدید اشتراک", renewUrl));
        return (text, html);
    }
}
