using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// V2Ray panels and the mail configuration are the two things an operator cannot re-enter from memory: panel
// credentials and mailbox passwords. They were absent from every backup, so a restore silently lost them.
// These pin that they travel with a backup now — and, just as importantly, that restoring an OLDER backup
// (one taken before they were captured) does not wipe them.
public class BackupV2RayAndMailTests
{
    private static SqliteDataStore FreshStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"));
    }

    private static void SeedV2Ray(IDataStore store)
    {
        store.AddV2RayPanel(new V2RayPanel
        {
            Url = "https://nl.example.com:8080", Username = "admin",
            Password = SensitiveField.Protect("panel-secret"),
            Name = "هلند تانل", Flag = "NL", SubDomain = "sub.example.com", SubPath = "sub",
        });
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "ماهانه", Active = true });
        store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ", PanelId = 1, InboundIds = new() { 1 },
            VolumeGb = 20, DurationDays = 30, IpLimit = 2, Price = 300_000, Active = true,
        });
    }

    private static void SeedMail(IDataStore store) =>
        store.UpdateMailboxSettings(new MailboxSettings
        {
            Enabled = true, ImapHost = "mail.example.com", ImapPort = 993,
            SmtpHost = "mail.example.com", SmtpPort = 465,
            Username = "support@example.com", Password = SensitiveField.Protect("mail-secret"),
        });

    [Fact]
    public void A_full_backup_carries_v2ray_and_mail_across_a_restore()
    {
        var a = FreshStore();
        SeedV2Ray(a);
        SeedMail(a);

        var b = FreshStore();
        b.LoadSnapshot(b.DeserializeSnapshot(a.SerializeSnapshot())!);

        var panel = Assert.Single(b.GetV2RayPanels());
        Assert.Equal("هلند تانل", panel.Name);
        // The credential has to survive too, or the restored shop cannot provision anything.
        Assert.Equal("panel-secret", SensitiveField.Reveal(panel.Password));
        Assert.Single(b.GetV2RayCategories());
        Assert.Single(b.GetV2RayPlans());

        var mail = b.GetMailboxSettings();
        Assert.Equal("mail.example.com", mail.ImapHost);
        Assert.Equal("mail-secret", SensitiveField.Reveal(mail.Password));
    }

    [Fact]
    public void The_v2ray_section_restores_on_its_own()
    {
        var a = FreshStore();
        SeedV2Ray(a);
        var json = a.SerializeSection(BackupSection.V2Ray);

        var b = FreshStore();
        b.RestoreSection(BackupSection.V2Ray, b.DeserializeSnapshot(json)!);

        Assert.Single(b.GetV2RayPanels());
        Assert.Single(b.GetV2RayPlans());
    }

    [Fact]
    public void The_mail_section_restores_on_its_own()
    {
        var a = FreshStore();
        SeedMail(a);
        a.UpdateEmailSettings(new EmailSettings { Host = "smtp.example.com", FromEmail = "no-reply@example.com" });
        var json = a.SerializeSection(BackupSection.Mail);

        var b = FreshStore();
        b.RestoreSection(BackupSection.Mail, b.DeserializeSnapshot(json)!);

        Assert.Equal("mail.example.com", b.GetMailboxSettings().ImapHost);
        Assert.Equal("smtp.example.com", b.GetEmailSettings().Host);
    }

    [Fact]
    public void Restoring_a_backup_taken_before_these_sections_existed_leaves_them_alone()
    {
        var store = FreshStore();
        SeedV2Ray(store);
        SeedMail(store);

        // A snapshot from an older build simply has no such fields.
        var legacy = store.DeserializeSnapshot(store.SerializeSnapshot())!;
        legacy.V2Ray = null;
        legacy.MailboxSettings = null;

        store.LoadSnapshot(legacy);

        // Losing the panel password to an old backup file would strand the operator, so absence must mean
        // "this snapshot says nothing about it", never "clear it".
        Assert.Single(store.GetV2RayPanels());
        Assert.Equal("panel-secret", SensitiveField.Reveal(store.GetV2RayPanels()[0].Password));
        Assert.Equal("mail.example.com", store.GetMailboxSettings().ImapHost);
    }
}
