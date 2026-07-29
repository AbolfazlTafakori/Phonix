using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Phonix.Api.Tests;

// Three settings endpoints used to hand live credentials straight back to the browser: the SMTP password, and
// all three Telegram bot tokens (backup — which receives every section of the database; receipts — customers'
// bank receipts; orders — the approve buttons). A token IS the bot and the SMTP password IS the mail account,
// so every panel page load put them in page memory, in the devtools network log, and in reach of anything
// that ever runs script on that origin.
//
// The fix is the pattern MailboxSettingsDto and V2RayPanelDto already used: absent BY TYPE, presence reported
// as a bool, and blank-on-update meaning "keep". These tests pin the round-trip, because the failure mode of a
// regression here is silent — the form still works perfectly while leaking again.
[Collection("api")]
public class SecretRedactionTests : IClassFixture<PhonixAppFactory>
{
    private readonly HttpClient _client;
    public SecretRedactionTests(PhonixAppFactory factory) => _client = factory.CreateClient();

    private async Task<string> AdminTokenAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "reza", password = "1234", admin = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<LoginBody>())!.Token!;
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", $"Bearer {token}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private record LoginBody(string? Token);

    [Fact]
    public async Task The_smtp_password_is_stored_but_never_returned()
    {
        var admin = await AdminTokenAsync();
        const string secret = "sup3r-secret-smtp-pw";

        var saved = await _client.SendAsync(Authed(HttpMethod.Put, "/api/email-settings", admin, new
        {
            enabled = true, host = "smtp.example.com", port = 587, username = "info@example.com",
            password = secret, fromEmail = "info@example.com", fromName = "Phoenix", useSsl = true,
        }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(secret, await saved.Content.ReadAsStringAsync());

        var fetched = await _client.SendAsync(Authed(HttpMethod.Get, "/api/email-settings", admin));
        var body = await fetched.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body);
        Assert.Contains("\"hasPassword\":true", body);
        Assert.Contains("smtp.example.com", body); // the non-secret settings still round-trip

        // Blank means keep — the form can save the other fields without ever holding the password.
        var again = await _client.SendAsync(Authed(HttpMethod.Put, "/api/email-settings", admin, new
        {
            enabled = true, host = "smtp2.example.com", port = 465, username = "info@example.com",
            password = "", fromEmail = "info@example.com", fromName = "Phoenix", useSsl = true,
        }));
        var afterBody = await again.Content.ReadAsStringAsync();
        Assert.Contains("\"hasPassword\":true", afterBody);
        Assert.Contains("smtp2.example.com", afterBody);
        Assert.DoesNotContain(secret, afterBody);
    }

    [Fact]
    public async Task Telegram_bot_tokens_are_stored_but_never_returned()
    {
        var admin = await AdminTokenAsync();
        const string backup = "111:backup-bot-token";
        const string receipt = "222:receipt-bot-token";
        const string order = "333:order-bot-token";

        var saved = await _client.SendAsync(Authed(HttpMethod.Put, "/api/backup/telegram", admin, new
        {
            backupEnabled = true, alertsEnabled = true, receiptBotEnabled = true, orderBotEnabled = true,
            botToken = backup, chatId = "-100100",
            receiptBotToken = receipt, receiptChatId = "-100200",
            orderBotToken = order, orderChatId = "-100300",
            intervalHours = 24,
        }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var fetched = await _client.SendAsync(Authed(HttpMethod.Get, "/api/backup/telegram", admin));
        var body = await fetched.Content.ReadAsStringAsync();
        Assert.DoesNotContain(backup, body);
        Assert.DoesNotContain(receipt, body);
        Assert.DoesNotContain(order, body);
        Assert.Contains("\"hasBotToken\":true", body);
        Assert.Contains("\"hasReceiptBotToken\":true", body);
        Assert.Contains("\"hasOrderBotToken\":true", body);
        Assert.Contains("-100200", body); // chat ids are addresses, not secrets — the form still needs them

        // Each bot has its own panel page, and each saves the whole settings object. Blank tokens must
        // therefore leave the OTHER two bots untouched, or saving the orders page would silently unconfigure
        // the backup bot.
        var partial = await _client.SendAsync(Authed(HttpMethod.Put, "/api/backup/telegram", admin, new
        {
            backupEnabled = true, alertsEnabled = true, receiptBotEnabled = true, orderBotEnabled = true,
            chatId = "-100100", receiptChatId = "-100200",
            orderBotToken = "", orderChatId = "-100999",
            intervalHours = 12,
        }));
        var after = await partial.Content.ReadAsStringAsync();
        Assert.Contains("\"hasBotToken\":true", after);
        Assert.Contains("\"hasReceiptBotToken\":true", after);
        Assert.Contains("\"hasOrderBotToken\":true", after);
        Assert.Contains("-100999", after);
    }

    // Sending the identity-document archive to Telegram always required admin + a fresh 2FA code + the
    // server's backup key, because it moves those documents off the server. Downloading the very same archive
    // required only the admin session, so one stolen cookie bought every customer's national ID card, selfie,
    // bank-card photo and deposit receipt in a single request. Both paths now cost the same three factors.
    [Fact]
    public async Task Bulk_identity_exports_refuse_a_bare_admin_session()
    {
        var admin = await AdminTokenAsync();

        // The old one-click GETs must not exist any more — a route left behind is the whole bug.
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await _client.SendAsync(Authed(HttpMethod.Get, "/api/backup/media/sensitive", admin))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await _client.SendAsync(Authed(HttpMethod.Get, "/api/backup/full", admin))).StatusCode);

        // And an authenticated admin without the other two factors is refused.
        foreach (var path in new[] { "/api/backup/media/sensitive", "/api/backup/full" })
        {
            var req = Authed(HttpMethod.Post, path, admin);
            req.Content = new MultipartFormDataContent
            {
                { new StringContent(""), "backupKey" },
                { new StringContent("000000"), "twoFactorCode" },
            };
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(req)).StatusCode);
        }

        // Public site imagery holds no personal data and stays a plain download.
        Assert.Equal(HttpStatusCode.OK,
            (await _client.SendAsync(Authed(HttpMethod.Get, "/api/backup/media/public", admin))).StatusCode);
    }

    [Fact]
    public async Task The_payment_telegram_token_is_stored_but_never_returned()
    {
        var admin = await AdminTokenAsync();
        const string secret = "444:payment-bot-token";

        await _client.SendAsync(Authed(HttpMethod.Put, "/api/payment-settings", admin, new
        {
            telegramEnabled = true, telegramBotToken = secret, telegramChatId = "-100400",
            requireReceipt = true, autoApproveUnder = 0,
        }));

        var fetched = await _client.SendAsync(Authed(HttpMethod.Get, "/api/payment-settings", admin));
        var body = await fetched.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body);
        Assert.Contains("\"hasTelegramBotToken\":true", body);
        Assert.Contains("-100400", body);
    }
}
