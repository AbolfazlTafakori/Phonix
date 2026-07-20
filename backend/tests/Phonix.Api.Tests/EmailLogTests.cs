using Phonix.Api.Data;
using Xunit;

namespace Phonix.Api.Tests;

// The record of outbound email. info@ receives nothing, so this log is the only trace a send leaves — which
// makes the FAILURES the part that matters: an email that never left is the one the shop has to act on.
public class EmailLogTests
{
    private static EmailLogStore NewLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("PHONIX_EMAIL_LOG_FILE", Path.Combine(dir, Guid.NewGuid() + ".json"));
        return new EmailLogStore();
    }

    [Fact]
    public void A_send_is_recorded_with_its_outcome()
    {
        var log = NewLog();
        log.Record("buyer@example.com", "اطلاعات سرویس شما", success: true);

        var entry = Assert.Single(log.Get(null, null, null, null, 1, 20).Items);
        Assert.Equal("buyer@example.com", entry.To);
        Assert.Equal("اطلاعات سرویس شما", entry.Subject);
        Assert.True(entry.Success);
        Assert.Null(entry.Error);
    }

    [Fact]
    public void A_failure_keeps_the_reason_it_failed()
    {
        var log = NewLog();
        log.Record("buyer@example.com", "بازیابی رمز", success: false, error: "SMTP timeout");

        var entry = Assert.Single(log.Get(null, null, null, null, 1, 20).Items);
        Assert.False(entry.Success);
        Assert.Equal("SMTP timeout", entry.Error);
        Assert.Equal(1, log.FailedCount());
    }

    [Fact]
    public void Failures_can_be_isolated_from_the_noise()
    {
        var log = NewLog();
        log.Record("a@example.com", "ok", success: true);
        log.Record("b@example.com", "broken", success: false, error: "refused");
        log.Record("c@example.com", "ok", success: true);

        var failed = log.Get(null, success: false, null, null, 1, 20);
        Assert.Equal(1, failed.Total);
        Assert.Equal("b@example.com", failed.Items[0].To);
        Assert.Equal(3, log.Get(null, null, null, null, 1, 20).Total);
    }

    [Fact]
    public void Searching_finds_a_customer_by_address_or_subject()
    {
        var log = NewLog();
        log.Record("reza@example.com", "فاکتور خرید", success: true);
        log.Record("sara@example.com", "تأیید ایمیل", success: true);

        Assert.Equal("reza@example.com", Assert.Single(log.Get("reza", null, null, null, 1, 20).Items).To);
        Assert.Equal("sara@example.com", Assert.Single(log.Get("تأیید", null, null, null, 1, 20).Items).To);
        Assert.Empty(log.Get("nobody", null, null, null, 1, 20).Items);
    }

    [Fact]
    public void The_newest_send_is_the_first_one_an_admin_sees()
    {
        var log = NewLog();
        log.Record("first@example.com", "one", success: true);
        log.Record("second@example.com", "two", success: true);

        Assert.Equal("second@example.com", log.Get(null, null, null, null, 1, 20).Items[0].To);
    }

    [Fact]
    public void The_log_survives_a_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid() + ".json");
        Environment.SetEnvironmentVariable("PHONIX_EMAIL_LOG_FILE", path);

        var log = new EmailLogStore();
        log.Record("buyer@example.com", "اطلاعات سرویس شما", success: true);
        log.Save();

        var reopened = new EmailLogStore(); // same file, fresh instance — a restart
        Assert.Equal("buyer@example.com", Assert.Single(reopened.Get(null, null, null, null, 1, 20).Items).To);
    }
}
