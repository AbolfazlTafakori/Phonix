using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Customer accounts linked to the customer Telegram bot. The chat id lives on the user's own JSON row, so a
// backup or a cluster sync carries the link with the account.
public sealed partial class SqliteDataStore
{
    private static List<AppUser> UsersWithTelegramChat(SqliteConnection conn, SqliteTransaction? tx, long chatId) =>
        conn.Query<string>("SELECT DataJson FROM Users WHERE json_extract(DataJson, '$.TelegramChatId') = @chatId",
                new { chatId }, tx)
            .Select(j => Deserialize<AppUser>(j)!)
            .ToList();

    public AppUser? FindUserByTelegramChat(long chatId)
    {
        using var conn = OpenConnection();
        return UsersWithTelegramChat(conn, null, chatId).FirstOrDefault();
    }

    // Lives in the Tokens table beside the other one-time tokens, so it is consumed (deleted on first read)
    // exactly like them. Asking again replaces the code the account already had: an older email stops working.
    public string CreateTelegramLinkCode(int userId, TimeSpan lifetime)
    {
        var purpose = Services.TelegramCustomerBot.CodePurpose;
        var expiresAt = DateTime.UtcNow.Add(lifetime).ToString("o");
        return WriteTx((conn, tx) =>
        {
            conn.Execute("DELETE FROM Tokens WHERE ExpiresAt <= @now OR (UserId = @userId AND Purpose = @purpose)",
                new { now = NowIso(), userId, purpose }, tx);
            while (true)
            {
                var code = string.Create(Services.TelegramCustomerBot.CodeLength, 0,
                    (span, _) => { for (var i = 0; i < span.Length; i++) span[i] = (char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10)); });
                // 10^24 possible codes: a clash is practically impossible, but the key is unique, so make sure.
                if (conn.ExecuteScalar<long>("SELECT COUNT(*) FROM Tokens WHERE Token = @code", new { code }, tx) > 0) continue;
                conn.Execute("INSERT INTO Tokens (Token, UserId, Purpose, ExpiresAt, Data) VALUES (@code, @userId, @purpose, @expiresAt, NULL)",
                    new { code, userId, purpose, expiresAt }, tx);
                return code;
            }
        });
    }

    public bool LinkTelegram(int userId, long chatId, string? username) =>
        WriteTx((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return false;
            // A chat is one person's: linking it here takes it off whatever account had it before.
            foreach (var other in UsersWithTelegramChat(conn, tx, chatId).Where(u => u.Id != userId))
            {
                other.TelegramChatId = null;
                other.TelegramUsername = null;
                other.TelegramLinkedAtUtc = null;
                UpsertUser(conn, tx, other);
            }
            user.TelegramChatId = chatId;
            user.TelegramUsername = string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');
            user.TelegramLinkedAtUtc = DateTime.UtcNow;
            user.TelegramNotify = true;
            UpsertUser(conn, tx, user);
            return true;
        });

    public bool UnlinkTelegram(int userId) =>
        WriteTx((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null || user.TelegramChatId is null) return false;
            user.TelegramChatId = null;
            user.TelegramUsername = null;
            user.TelegramLinkedAtUtc = null;
            UpsertUser(conn, tx, user);
            return true;
        });

    public void UnlinkTelegramChat(long chatId) =>
        WriteTx((conn, tx) =>
        {
            foreach (var user in UsersWithTelegramChat(conn, tx, chatId))
            {
                user.TelegramChatId = null;
                user.TelegramUsername = null;
                user.TelegramLinkedAtUtc = null;
                UpsertUser(conn, tx, user);
            }
            return true;
        });
}
