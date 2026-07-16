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

    // A label/value detail block (order code, amount, date …). Rows with a blank value are dropped, so a
    // caller can pass an optional field without branching.
    private static string Rows(params (string label, string? value)[] rows)
    {
        var cells = string.Concat(rows.Where(r => !string.IsNullOrWhiteSpace(r.value)).Select(r =>
            $@"<tr>
              <td dir=""rtl"" align=""right"" style=""padding:7px 0;color:{Muted};font-size:15px;white-space:nowrap;"">{WebUtility.HtmlEncode(r.label)}</td>
              <td dir=""rtl"" align=""left"" style=""padding:7px 0;color:{Ink};font-size:15px;font-weight:700;"">{WebUtility.HtmlEncode(r.value)}</td>
            </tr>"));
        if (cells.Length == 0) return "";
        return $@"<table role=""presentation"" dir=""rtl"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:22px 0 0;background:{Footer};border:1px solid {Border};border-radius:12px;padding:8px 20px;direction:rtl;"">{cells}</table>";
    }

    // Money as the storefront writes it: grouped thousands + the unit, e.g. "۴۹۰,۰۰۰ تومان".
    private static string Toman(long amount) =>
        JalaliDate.ToPersianDigits(Math.Abs(amount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)) + " تومان";

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

    // Sent once, right after the address is confirmed — the verification mail itself is a chore, this is the
    // actual greeting and the first push toward a first purchase.
    public static (string text, string html) Welcome(string name, string shopUrl)
    {
        var text = $"{name} عزیز، به {Brand} خوش آمدید!\n\nایمیل شما با موفقیت تأیید شد و حساب‌تان فعال است. برای شروع خرید به فروشگاه سر بزنید:\n{shopUrl}";
        var html = Shell($"{name} عزیز، خوش آمدید 🎉",
            "ایمیل شما تأیید شد و حساب‌تان فعال است.",
            "<p style=\"margin:0;\">ایمیل شما با موفقیت تأیید شد و حساب <b>فونیکس وریفای</b> شما فعال است. از این پس می‌توانید سفارش ثبت کنید، کیف پول‌تان را شارژ کنید و از پشتیبانی ۲۴ ساعته استفاده کنید.</p>"
            + Button("شروع خرید", shopUrl)
            + Note("برای خرید، ابتدا کارت بانکی خود را در حساب کاربری ثبت و تأیید کنید. هر سؤالی داشتید، پشتیبانی ما در تمام ساعات شبانه‌روز پاسخگوی شماست."));
        return (text, html);
    }

    // Sent on every successful sign-in. Same shape as PasswordChanged: the owner ignores it, a victim gets an
    // early signal plus a one-tap path to lock the attacker out.
    public static (string text, string html) LoginNotice(string whenFa, string ip, string device, string passwordUrl)
    {
        var text = $"ورود به حساب {Brand} شما\n\nزمان: {whenFa}\nنشانی IP: {ip}\nدستگاه: {device}\n\nاگر این ورود کار خودتان بوده، نیازی به هیچ اقدامی نیست.\n\nاگر شما نبوده‌اید، همین حالا گذرواژه‌ی خود را تغییر دهید:\n{passwordUrl}";
        var html = Shell("ورود به حساب شما 🔑",
            $"ورودی به حساب شما در {whenFa} ثبت شد.",
            "<p style=\"margin:0;\">ورودی به حساب <b>فونیکس وریفای</b> شما ثبت شد. اگر این ورود کار خودتان بوده، این ایمیل را نادیده بگیرید.</p>"
            + Rows(("زمان ورود", whenFa), ("نشانی IP", ip), ("دستگاه", device))
            + WarnNote($"<b style=\"color:{Accent};\">این ورود کار شما نبوده؟</b><br>همین حالا گذرواژه‌ی خود را تغییر دهید تا همه‌ی دستگاه‌های دیگر از حساب شما خارج شوند، و با پشتیبانی تماس بگیرید.")
            + Button("تغییر گذرواژه", passwordUrl));
        return (text, html);
    }

    // The checkout receipt. An order paid entirely from the wallet goes straight to preparing; one with a
    // card-to-card remainder waits on receipt review first. The status line must match the order's real
    // state, so the caller passes which it is — promising delivery on an unapproved receipt would be a lie.
    // One delivered account. A multi-account order sends one of these per account as it lands, so the customer
    // is told about each purchase separately rather than waiting for the whole basket.
    public static (string text, string html) OrderUnitDelivered(
        string orderCode, string serviceName, string? plan, int unitIndex, int unitCount, string content, string accountUrl)
    {
        var which = unitCount > 1 ? $" (اکانت {unitIndex} از {unitCount})" : "";
        var title = $"{serviceName}{(string.IsNullOrWhiteSpace(plan) ? "" : $" — {plan}")}";
        var text = $"سرویس شما آماده شد.\n\n{title}{which}\nکد سفارش: {orderCode}\n\n{content}\n\n{accountUrl}";
        var html = Shell($"{serviceName} آماده شد ✅",
            $"یکی از سرویس‌های سفارش {orderCode} تحویل داده شد.",
            Rows(("سرویس", title), ("کد سفارش", orderCode))
            + (unitCount > 1 ? Note($"این ایمیل مربوط به <b>اکانت {unitIndex} از {unitCount}</b> این سفارش است؛ بقیه‌ی اکانت‌ها جداگانه ارسال می‌شوند.") : "")
            + $"<p dir=\"rtl\" style=\"margin:18px 0 0;white-space:pre-wrap;text-align:right;\">{WebUtility.HtmlEncode(content).Replace("\n", "<br>")}</p>"
            + Button("مشاهده‌ی سفارش‌ها", accountUrl));
        return (text, html);
    }

    // The wrap-up once every account in the order has been delivered: the exact list, and that it is complete.
    public static (string text, string html) OrderCompleted(
        string orderCode, string? invoiceNumber, IReadOnlyList<(string name, string? plan, int quantity)> lines, string accountUrl)
    {
        var listText = string.Join("\n", lines.Select(l =>
            $"• {l.name}{(string.IsNullOrWhiteSpace(l.plan) ? "" : $" — {l.plan}")} × {l.quantity}"));
        var text = $"سفارش {orderCode} تکمیل شد.\n\n{listText}\n"
                 + (string.IsNullOrWhiteSpace(invoiceNumber) ? "" : $"\nشماره فاکتور: {invoiceNumber}\n")
                 + $"\n{accountUrl}";

        var rows = string.Join("", lines.Select(l =>
            $"<tr><td dir=\"rtl\" align=\"right\" style=\"padding:8px 0;color:{Body};font-size:15px;border-bottom:1px solid #f0e2d6;\">"
            + $"{WebUtility.HtmlEncode(l.name)}{(string.IsNullOrWhiteSpace(l.plan) ? "" : $" <span style=\"color:{Muted};\">— {WebUtility.HtmlEncode(l.plan!)}</span>")}</td>"
            + $"<td align=\"left\" style=\"padding:8px 0;color:{Muted};font-size:14px;border-bottom:1px solid #f0e2d6;\">×{l.quantity}</td></tr>"));

        var html = Shell($"سفارش {orderCode} تکمیل شد 🎉",
            "همه‌ی سرویس‌های این سفارش تحویل داده شدند.",
            "<p style=\"margin:0;\">تمام اقلام سفارش شما تحویل داده شد. فهرست کامل سفارش:</p>"
            + $"<table role=\"presentation\" dir=\"rtl\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:16px 0 0;direction:rtl;\">{rows}</table>"
            + (string.IsNullOrWhiteSpace(invoiceNumber) ? "" : Rows(("شماره فاکتور", invoiceNumber!)))
            + Button("مشاهده‌ی سفارش‌ها", accountUrl));
        return (text, html);
    }

    public static (string text, string html) OrderPlaced(string orderCode, long total, string dateFa, string ordersUrl, bool awaitingPayment)
    {
        var statusFa = awaitingPayment ? "در انتظار تأیید پرداخت" : "در حال آماده‌سازی";
        var lead = awaitingPayment
            ? "سفارش شما با موفقیت ثبت شد. رسید پرداخت شما هم‌اکنون در حال بررسی است؛ به‌محض تأیید، سفارش وارد مرحله‌ی آماده‌سازی می‌شود و نتیجه را به شما اطلاع می‌دهیم."
            : "سفارش شما با موفقیت ثبت شد و مبلغ آن از کیف پول شما پرداخت شد. سفارش هم‌اکنون در حال آماده‌سازی است و به‌محض آماده شدن، اطلاعات سرویس را برای شما ارسال می‌کنیم.";
        var text = $"سفارش {orderCode} ثبت شد.\n\nکد سفارش: {orderCode}\nمبلغ: {Toman(total)}\nتاریخ: {dateFa}\nوضعیت: {statusFa}\n\n{lead}\n\nپیگیری سفارش:\n{ordersUrl}";
        var html = Shell($"سفارش {orderCode} ثبت شد 🧾",
            awaitingPayment ? "سفارش شما ثبت شد و رسید پرداخت در حال بررسی است." : "سفارش شما ثبت شد و در حال آماده‌سازی است.",
            $"<p style=\"margin:0;\">{lead}</p>"
            + Rows(("کد سفارش", orderCode), ("مبلغ", Toman(total)), ("تاریخ ثبت", dateFa), ("وضعیت", statusFa))
            + Button("پیگیری سفارش", ordersUrl)
            + (awaitingPayment
                ? Note("بررسی رسید معمولاً کوتاه است، اما در ساعات شلوغ ممکن است کمی طول بکشد. نیازی به ثبت دوباره‌ی سفارش نیست.")
                : ""));
        return (text, html);
    }

    public static (string text, string html) WalletToppedUp(long amount, long balance, string walletUrl)
    {
        var text = $"کیف پول شما شارژ شد.\n\nمبلغ شارژ: {Toman(amount)}\nموجودی جدید: {Toman(balance)}\n\nمشاهده‌ی کیف پول:\n{walletUrl}";
        var html = Shell("کیف پول شما شارژ شد 💳",
            $"رسید شما تأیید و {Toman(amount)} به کیف پول‌تان اضافه شد.",
            "<p style=\"margin:0;\">رسید واریز شما تأیید شد و مبلغ آن به کیف پول شما اضافه شد. از این موجودی می‌توانید برای خرید هر سرویسی استفاده کنید.</p>"
            + Rows(("مبلغ شارژ", Toman(amount)), ("موجودی جدید", Toman(balance)))
            + Button("مشاهده‌ی کیف پول", walletUrl));
        return (text, html);
    }

    public static (string text, string html) OrderPaymentApproved(string orderCode, long amount, string ordersUrl)
    {
        var text = $"پرداخت سفارش {orderCode} تأیید شد.\n\nکد سفارش: {orderCode}\nمبلغ: {Toman(amount)}\nوضعیت: در حال آماده‌سازی\n\nپیگیری سفارش:\n{ordersUrl}";
        var html = Shell("پرداخت شما تأیید شد ✅",
            $"پرداخت سفارش {orderCode} تأیید و سفارش در حال آماده‌سازی است.",
            "<p style=\"margin:0;\">رسید پرداخت سفارش شما تأیید شد و سفارش وارد مرحله‌ی آماده‌سازی شد. به‌محض آماده شدن، اطلاعات سرویس را برای شما ارسال می‌کنیم.</p>"
            + Rows(("کد سفارش", orderCode), ("مبلغ پرداختی", Toman(amount)), ("وضعیت", "در حال آماده‌سازی"))
            + Button("پیگیری سفارش", ordersUrl));
        return (text, html);
    }

    // A rejected receipt. The rejection note is the whole point of the mail — without it the customer only
    // sees money that never arrived and has no idea what to fix, so it leads and never gets truncated.
    public static (string text, string html) PaymentRejected(string kindFa, long amount, string? reason, string supportUrl)
    {
        var reasonFa = string.IsNullOrWhiteSpace(reason) ? "دلیلی ثبت نشده است." : reason!.Trim();
        var text = $"{kindFa} شما تأیید نشد.\n\nمبلغ: {Toman(amount)}\nدلیل: {reasonFa}\n\nاگر فکر می‌کنید اشتباهی رخ داده یا سؤالی دارید، با پشتیبانی در تماس باشید:\n{supportUrl}";
        var html = Shell($"{kindFa} شما تأیید نشد",
            $"رسید {Toman(amount)} شما تأیید نشد.",
            $"<p style=\"margin:0;\">متأسفانه رسید {WebUtility.HtmlEncode(kindFa)} شما پس از بررسی <b>تأیید نشد</b> و مبلغ آن به حساب شما اضافه نشد.</p>"
            + Rows(("مبلغ", Toman(amount)))
            + WarnNote($"<b style=\"color:{Accent};\">دلیل رد شدن</b><br>{WebUtility.HtmlEncode(reasonFa)}")
            + Note("اگر مبلغ از حساب شما کسر شده، نگران نباشید — وجهی که به مقصد نرسیده باشد نزد بانک شما باقی می‌ماند. در غیر این صورت با ارائه‌ی شماره پیگیری با پشتیبانی تماس بگیرید تا موضوع را بررسی کنیم.")
            + Button("تماس با پشتیبانی", supportUrl));
        return (text, html);
    }

    public static (string text, string html) TicketReplied(string code, string subject, string ticketsUrl)
    {
        var text = $"پشتیبانی به تیکت شما پاسخ داد.\n\nتیکت: {subject} ({code})\n\nمشاهده‌ی پاسخ:\n{ticketsUrl}";
        var html = Shell("پاسخ پشتیبانی 💬",
            $"به تیکت «{subject}» شما پاسخ داده شد.",
            "<p style=\"margin:0;\">پشتیبانی فونیکس وریفای به تیکت شما پاسخ داد. برای خواندن پاسخ و ادامه‌ی گفتگو، به بخش تیکت‌ها در حساب کاربری خود مراجعه کنید.</p>"
            + Rows(("موضوع", subject), ("کد تیکت", code))
            + Button("مشاهده‌ی پاسخ", ticketsUrl));
        return (text, html);
    }

    public static (string text, string html) TicketOpenedByStaff(string code, string subject, string ticketsUrl)
    {
        var text = $"پشتیبانی برای شما تیکت باز کرد.\n\nتیکت: {subject} ({code})\n\nمشاهده‌ی تیکت:\n{ticketsUrl}";
        var html = Shell("پشتیبانی برای شما پیام گذاشت 💬",
            $"تیکت «{subject}» از سوی پشتیبانی برای شما باز شد.",
            "<p style=\"margin:0;\">پشتیبانی فونیکس وریفای درباره‌ی حساب یا سفارش شما تیکتی باز کرد. لطفاً آن را بخوانید و در صورت نیاز پاسخ دهید.</p>"
            + Rows(("موضوع", subject), ("کد تیکت", code))
            + Button("مشاهده‌ی تیکت", ticketsUrl));
        return (text, html);
    }

    public static (string text, string html) CardApproved(string cardMasked, string cardsUrl)
    {
        var text = $"کارت بانکی شما تأیید شد.\n\nکارت: {cardMasked}\n\nاز این پس می‌توانید با این کارت واریز کنید:\n{cardsUrl}";
        var html = Shell("کارت بانکی شما تأیید شد ✅",
            $"کارت {cardMasked} تأیید شد.",
            "<p style=\"margin:0;\">کارت بانکی شما تأیید شد. از این پس می‌توانید واریزهای کارت‌به‌کارت خود را از این کارت انجام دهید.</p>"
            + Rows(("شماره کارت", cardMasked))
            + Button("مشاهده‌ی کارت‌ها", cardsUrl));
        return (text, html);
    }

    public static (string text, string html) CardRejected(string cardMasked, string? reason, string cardsUrl)
    {
        var reasonFa = string.IsNullOrWhiteSpace(reason) ? "دلیلی ثبت نشده است." : reason!.Trim();
        var text = $"کارت بانکی شما تأیید نشد.\n\nکارت: {cardMasked}\nدلیل: {reasonFa}\n\nپس از رفع مورد بالا می‌توانید دوباره کارت خود را ثبت کنید:\n{cardsUrl}";
        var html = Shell("کارت بانکی شما تأیید نشد",
            $"کارت {cardMasked} تأیید نشد.",
            "<p style=\"margin:0;\">کارت بانکی ثبت‌شده‌ی شما پس از بررسی تأیید نشد. پس از رفع مورد زیر می‌توانید دوباره آن را ثبت کنید.</p>"
            + Rows(("شماره کارت", cardMasked))
            + WarnNote($"<b style=\"color:{Accent};\">دلیل رد شدن</b><br>{WebUtility.HtmlEncode(reasonFa)}")
            + Button("ثبت دوباره‌ی کارت", cardsUrl));
        return (text, html);
    }

    public static (string text, string html) KycApproved(string accountUrl)
    {
        var text = $"احراز هویت شما تأیید شد.\n\nحساب شما اکنون کاملاً فعال است و می‌توانید از همه‌ی خدمات {Brand} استفاده کنید:\n{accountUrl}";
        var html = Shell("احراز هویت شما تأیید شد ✅",
            "حساب شما اکنون کاملاً فعال است.",
            "<p style=\"margin:0;\">مدارک احراز هویت شما بررسی و <b>تأیید</b> شد. حساب شما اکنون کاملاً فعال است و محدودیتی برای استفاده از خدمات فونیکس وریفای ندارید.</p>"
            + Button("مشاهده‌ی حساب کاربری", accountUrl));
        return (text, html);
    }

    public static (string text, string html) KycRejected(string? reason, string kycUrl)
    {
        var reasonFa = string.IsNullOrWhiteSpace(reason) ? "دلیلی ثبت نشده است." : reason!.Trim();
        var text = $"احراز هویت شما تأیید نشد.\n\nدلیل: {reasonFa}\n\nپس از رفع مورد بالا می‌توانید مدارک خود را دوباره ارسال کنید:\n{kycUrl}";
        var html = Shell("احراز هویت شما تأیید نشد",
            "مدارک احراز هویت شما نیاز به اصلاح دارد.",
            "<p style=\"margin:0;\">مدارک احراز هویت شما پس از بررسی تأیید نشد. نگران نباشید — پس از رفع مورد زیر می‌توانید مدارک خود را دوباره ارسال کنید.</p>"
            + WarnNote($"<b style=\"color:{Accent};\">دلیل رد شدن</b><br>{WebUtility.HtmlEncode(reasonFa)}")
            + Button("ارسال دوباره‌ی مدارک", kycUrl));
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
