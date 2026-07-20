using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Covers the two parts of the admin mailbox that are worth pinning down without a live IMAP server: the way
// the mailbox credential is stored, and the sanitizer that stands between an attacker-authored email body
// and the highest-privileged session in the shop.
public class MailboxSettingsTests
{
    private static MailboxSettings Sample() => new()
    {
        Enabled = true,
        ImapHost = "mail.example.com",
        ImapPort = 993,
        ImapUseSsl = true,
        SmtpHost = "mail.example.com",
        SmtpPort = 587,
        SmtpUseSsl = true,
        Username = "support",
        Password = "s3cret-pass",
        Address = "support@example.com",
        DisplayName = "پشتیبانی",
    };

    [Fact]
    public void Settings_round_trip_through_the_store()
    {
        var store = TestStore.Create();
        store.UpdateMailboxSettings(Sample());

        var loaded = store.GetMailboxSettings();

        Assert.True(loaded.Enabled);
        Assert.Equal("mail.example.com", loaded.ImapHost);
        Assert.Equal(993, loaded.ImapPort);
        Assert.Equal(587, loaded.SmtpPort);
        Assert.Equal("support", loaded.Username);
        Assert.Equal("support@example.com", loaded.Address);
        // Read back decrypted, so it can be handed straight to MailKit.
        Assert.Equal("s3cret-pass", loaded.Password);
    }

    [Fact]
    public void Password_survives_a_restart_and_is_not_stored_as_plaintext()
    {
        var store = TestStore.Create(out var dbPath);
        store.UpdateMailboxSettings(Sample());

        Assert.Equal("s3cret-pass", TestStore.Reopen(dbPath).GetMailboxSettings().Password);
        // The point of the encryption: the credential must not be readable on disk. The WAL file counts —
        // under WAL journaling a just-written row may still live only there.
        Assert.DoesNotContain("s3cret-pass", ReadWhileOpen(dbPath));
        Assert.DoesNotContain("s3cret-pass", ReadWhileOpen(dbPath + "-wal"));
    }

    // The store under test still holds the database open, so the file has to be read with sharing rather
    // than through File.ReadAllText.
    private static string ReadWhileOpen(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        // Latin1 maps every byte to a character, so a UTF-8 credential is still findable and no byte in the
        // binary page data can throw off the decode.
        return System.Text.Encoding.Latin1.GetString(buffer.ToArray());
    }

    [Fact]
    public void An_empty_password_leaves_the_stored_one_alone()
    {
        // The panel is never sent the password, so it cannot send one back. Saving any other field must not
        // silently wipe the credential and break the connection.
        var store = TestStore.Create();
        store.UpdateMailboxSettings(Sample());

        var edit = Sample();
        edit.Password = "";
        edit.DisplayName = "تیم پشتیبانی";
        store.UpdateMailboxSettings(edit);

        var loaded = store.GetMailboxSettings();
        Assert.Equal("s3cret-pass", loaded.Password);
        Assert.Equal("تیم پشتیبانی", loaded.DisplayName);
    }

    [Fact]
    public void A_nonsense_port_falls_back_to_the_standard_one()
    {
        var store = TestStore.Create();
        var input = Sample();
        input.ImapPort = 0;
        input.SmtpPort = 99999;
        store.UpdateMailboxSettings(input);

        var loaded = store.GetMailboxSettings();
        Assert.Equal(993, loaded.ImapPort);
        Assert.Equal(587, loaded.SmtpPort);
    }
}

public class MailHtmlSanitizerTests
{
    [Theory]
    // Every one of these is a real way an email body tries to run code in the reader's session.
    [InlineData("<script>alert(1)</script>", "alert")]
    [InlineData("<img src=x onerror=alert(1)>", "onerror")]
    [InlineData("<div onclick=\"steal()\">hi</div>", "onclick")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>", "iframe")]
    [InlineData("<a href=\"javascript:alert(1)\">tap</a>", "javascript:")]
    [InlineData("<object data=\"evil.swf\"></object>", "object")]
    [InlineData("<form action=\"https://evil.test\"><input name=p></form>", "form")]
    [InlineData("<svg><script>alert(1)</script></svg>", "script")]
    [InlineData("<style>@import url('https://evil.test/x.css')</style>", "import")]
    [InlineData("<body background=\"https://evil.test/pixel.gif\">x</body>", "evil.test")]
    public void Dangerous_markup_does_not_survive(string input, string forbidden)
    {
        var (html, _) = MailHtmlSanitizer.Sanitize(input);
        Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_formatting_is_preserved()
    {
        var (html, _) = MailHtmlSanitizer.Sanitize(
            "<p>سلام <strong>دوست</strong> عزیز</p><ul><li>یک</li></ul><table><tr><td>خانه</td></tr></table>");

        Assert.Contains("<strong>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("<td>", html);
        Assert.Contains("دوست", html);
    }

    [Fact]
    public void Links_survive_but_are_defanged()
    {
        var (html, _) = MailHtmlSanitizer.Sanitize("<a href=\"https://example.com/order\">سفارش</a>");

        Assert.Contains("https://example.com/order", html);
        Assert.Contains("noopener", html);
        Assert.Contains("_blank", html);
    }

    [Fact]
    public void Remote_images_are_stripped_and_reported()
    {
        // A remote image in an email is a read receipt. It has to go, and the reader has to be told it went.
        var (html, hadRemote) = MailHtmlSanitizer.Sanitize("<p>hi</p><img src=\"https://tracker.test/p.gif\">");

        Assert.True(hadRemote);
        Assert.DoesNotContain("tracker.test", html);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", html);
    }

    [Fact]
    public void A_plain_body_reports_no_remote_content()
    {
        var (_, hadRemote) = MailHtmlSanitizer.Sanitize("<p>فقط متن ساده</p>");
        Assert.False(hadRemote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_body_is_handled(string? input)
    {
        var (html, hadRemote) = MailHtmlSanitizer.Sanitize(input);
        Assert.Equal("", html);
        Assert.False(hadRemote);
    }
}
