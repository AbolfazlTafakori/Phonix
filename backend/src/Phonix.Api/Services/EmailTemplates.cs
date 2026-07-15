using System.Net;

namespace Phonix.Api.Services;

// Branded, RTL HTML wrappers for transactional emails. Each builder returns both a plain-text body
// (the fallback) and an HTML body, so callers pass them straight to IEmailSender.SendAsync(to, subj, text, html).
public static class EmailTemplates
{
    private const string Brand = "فونیکس وریفای";
    private const string Site = "phoenixverify.com";
    private const string SupportUrl = "https://phoenixverify.com/support";
    // Light-theme brand palette — mirrors the storefront's warm cream theme (globals.css `.home-light`):
    // red→orange brand gradient, warm off-white surfaces, warm borders and ink.
    private const string Accent = "#ef233c";      // brand red (links / small accents)
    private const string AccentDark = "#ff5a1f";  // brand orange (gradient end)
    private const string PageBg = "#f8f1ea";      // warm cream page background
    private const string Card = "#ffffff";        // white card fill
    private const string Border = "#eadfd4";      // warm hairline border
    private const string Footer = "#fffaf5";      // warm off-white footer
    private const string Ink = "#1f1a17";         // heading ink
    private const string Body = "#5e5248";        // body text
    private const string Muted = "#8c8075";       // muted / secondary text
    private const string Grad = "linear-gradient(135deg,#ef233c 0%,#ff5a1f 100%)";      // header brand band
    private const string CtaGrad = "linear-gradient(135deg,#ff8a2b 0%,#ff5a1f 55%,#ff3d2e 100%)"; // CTA button

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
  <meta name=""supported-color-schemes"" content=""light"">
  <link href=""https://fonts.googleapis.com/css2?family=Vazirmatn:wght@400;500;700&display=swap"" rel=""stylesheet"">
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Vazirmatn:wght@400;500;700&display=swap');
    body {{ margin:0; padding:0; }}
    a {{ text-decoration:none; }}
  </style>
</head>
<body style=""margin:0;padding:0;background:{PageBg};font-family:{FontStack};"">
  <div style=""display:none;max-height:0;overflow:hidden;opacity:0;color:{PageBg};font-size:1px;line-height:1px;"">{WebUtility.HtmlEncode(preheader)}</div>
  <table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:{PageBg};padding:32px 12px;direction:rtl;"">
    <tr><td align=""center"">
      <table role=""presentation"" dir=""rtl"" width=""580"" cellpadding=""0"" cellspacing=""0"" style=""max-width:580px;width:100%;background:{Card};border-radius:20px;overflow:hidden;box-shadow:0 20px 44px -10px rgba(166,102,45,0.16),0 6px 16px -4px rgba(166,102,45,0.10);border:1px solid {Border};direction:rtl;"">
        <tr><td style=""background:{Grad};padding:36px 32px;text-align:center;"">
          <span style=""color:#ffffff;font-family:{FontStack};font-size:24px;font-weight:700;letter-spacing:.3px;"">{Brand}</span>
        </td></tr>
        <tr><td dir=""rtl"" align=""right"" style=""padding:40px 40px 32px;color:{Body};font-family:{FontStack};font-size:18px;line-height:2.15;direction:rtl;text-align:right;"">
          <h1 style=""margin:0 0 22px;font-family:{FontStack};font-size:25px;font-weight:700;color:{Ink};line-height:1.6;text-align:right;"">{WebUtility.HtmlEncode(title)}</h1>
          {innerHtml}
        </td></tr>
        <tr><td style=""padding:24px 40px 32px;background:{Footer};border-top:1px solid {Border};text-align:center;font-family:{FontStack};"">
          <a href=""{SupportUrl}"" style=""color:{Accent};font-size:14px;font-weight:500;"">پشتیبانی</a>
          <span style=""color:{Border};font-size:14px;"">&nbsp;•&nbsp;</span>
          <a href=""https://{Site}"" style=""color:{Accent};font-size:14px;font-weight:500;"">{Site}</a>
          <p style=""margin:14px 0 0;color:{Muted};font-size:13px;line-height:1.95;"">این ایمیل به‌صورت خودکار از سوی {Brand} ارسال شده است؛ لطفاً به آن پاسخ ندهید.<br>© {DateTime.Now.Year} {Brand}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>";

    private static string Button(string text, string url) => $@"<table role=""presentation"" align=""center"" cellpadding=""0"" cellspacing=""0"" style=""margin:28px auto;""><tr><td align=""center"" style=""border-radius:12px;background:{AccentDark};background-image:{CtaGrad};box-shadow:0 12px 26px -10px rgba(255,90,31,0.55);"">
      <a href=""{WebUtility.HtmlEncode(url)}"" style=""display:inline-block;padding:16px 46px;color:#ffffff;font-family:{FontStack};font-size:17px;font-weight:700;border-radius:12px;"">{WebUtility.HtmlEncode(text)}</a>
    </td></tr></table>";

    // Small grey line for showing the raw link as a copy-paste fallback under the button.
    private static string LinkFallback(string url) =>
        $@"<p dir=""rtl"" style=""margin:18px 0 0;color:{Muted};font-size:14px;line-height:1.9;direction:rtl;text-align:right;"">اگر دکمه کار نکرد، این نشانی را در مرورگر باز کنید:</p>
        <p dir=""ltr"" style=""margin:6px 0 0;word-break:break-all;direction:ltr;text-align:left;""><a href=""{WebUtility.HtmlEncode(url)}"" style=""color:{Accent};font-size:14px;"">{WebUtility.HtmlEncode(url)}</a></p>";

    // A soft tinted note box for secondary / security messages under the main content.
    private static string Note(string html) =>
        $@"<table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0 0;direction:rtl;""><tr>
        <td dir=""rtl"" align=""right"" style=""background:#fff7f0;border:1px solid #f1d8c5;border-radius:12px;padding:16px 20px;color:{Body};font-size:15px;line-height:2;direction:rtl;text-align:right;"">{html}</td>
        </tr></table>";

    // Like Note, but a red-tinted alert box for security warnings (e.g. an unexpected password change).
    private static string WarnNote(string html) =>
        $@"<table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0 0;direction:rtl;""><tr>
        <td dir=""rtl"" align=""right"" style=""background:#fdeceb;border:1px solid #f5c6c3;border-radius:12px;padding:16px 20px;color:{Body};font-size:15px;line-height:2;direction:rtl;text-align:right;"">{html}</td>
        </tr></table>";

    public static (string text, string html) VerifyEmail(string link)
    {
        var text = $"به {Brand} خوش آمدید!\n\nتنها یک قدم تا فعال‌سازی حساب شما باقی مانده است. برای تأیید ایمیل و شروع خرید، این نشانی را باز کنید (تا ۱ ساعت معتبر است):\n{link}\n\nاگر شما در {Brand} ثبت‌نام نکرده‌اید، این ایمیل را نادیده بگیرید.";
        var html = Shell("به فونیکس وریفای خوش آمدید 🎉",
            "تنها یک قدم تا فعال‌سازی حساب شما باقی مانده است.",
            "<p style=\"margin:0;\">از اینکه به <b>فونیکس وریفای</b> پیوستید خوشحالیم. تنها یک قدم باقی مانده — برای فعال‌سازی حساب و شروع خرید، روی دکمه‌ی زیر بزنید.</p>"
            + Button("فعال‌سازی حساب", link)
            + LinkFallback(link)
            + Note("این لینک تا <b>۱ ساعت</b> معتبر است. اگر شما در فونیکس وریفای ثبت‌نام نکرده‌اید، می‌توانید با خیال راحت این ایمیل را نادیده بگیرید."));
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

    // Sent right after a successful password change (from the account panel or a reset link). Doubles as a
    // security tripwire: if the owner didn't make the change, this is their first signal the account may be
    // compromised, with a one-tap path to lock the attacker out. `forgotUrl` points at the reset-request page.
    public static (string text, string html) PasswordChanged(string forgotUrl, string whenFa)
    {
        var text = $"گذرواژه‌ی حساب {Brand} شما تغییر کرد.\n\nزمان تغییر: {whenFa}\n\nاگر این تغییر را خودتان انجام داده‌اید، نیازی به هیچ اقدامی نیست.\n\nاگر شما این کار را نکرده‌اید، حساب شما ممکن است در معرض خطر باشد. همین حالا گذرواژه‌ی خود را بازنشانی کنید و با پشتیبانی تماس بگیرید:\n{forgotUrl}";
        var html = Shell("گذرواژه‌ی حساب شما تغییر کرد 🔒",
            "گذرواژه‌ی حساب شما هم‌اکنون تغییر کرد.",
            "<p style=\"margin:0;\">گذرواژه‌ی حساب <b>فونیکس وریفای</b> شما هم‌اکنون تغییر کرد. اگر این تغییر توسط شما انجام شده، نیازی به هیچ اقدامی نیست و می‌توانید این ایمیل را نادیده بگیرید.</p>"
            + $"<p dir=\"rtl\" style=\"margin:14px 0 0;color:{Muted};font-size:15px;line-height:1.9;text-align:right;\">زمان تغییر: <b style=\"color:{Body};\">{WebUtility.HtmlEncode(whenFa)}</b></p>"
            + WarnNote("<b style=\"color:" + Accent + ";\">این کار را شما انجام نداده‌اید؟</b><br>ممکن است حساب شما در معرض خطر باشد. برای محافظت از حساب، همین حالا گذرواژه‌ی خود را بازنشانی کنید و با پشتیبانی تماس بگیرید.")
            + Button("بازنشانی گذرواژه", forgotUrl)
            + LinkFallback(forgotUrl));
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
