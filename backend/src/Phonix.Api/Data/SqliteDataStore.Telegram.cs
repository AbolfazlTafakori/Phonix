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
