using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Identity: bank cards and KYC review.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Bank cards ──────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<BankCard> GetAllCards(BankCardStatus? status = null)
    {
        var all = AllJson<BankCard>("Cards");
        if (status is BankCardStatus s) all = all.Where(c => c.Status == s).ToList();
        return all.OrderByDescending(c => c.Id).ToList();
    }
    public IReadOnlyList<BankCard> GetUserCards(int userId) =>
        AllJson<BankCard>("Cards").Where(c => c.UserId == userId).OrderByDescending(c => c.Id).ToList();
    public BankCard? GetCard(int id) => OneJson<BankCard>("Cards", id);

    public AddCardResult AddCard(int userId, string cardNumber, string holderName, string cardImage) =>
        WriteTx<AddCardResult>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return new AddCardResult(null, "کاربر یافت نشد.");
            var digits = InputValidation.DigitsOnly(cardNumber);
            if (digits.Length != 16) return new AddCardResult(null, "شماره کارت باید ۱۶ رقم باشد.");
            if (!InputValidation.PassesLuhn(digits)) return new AddCardResult(null, "شماره کارت نامعتبر است.");
            var name = (holderName ?? "").Trim();
            if (name.Length == 0) return new AddCardResult(null, "نام صاحب کارت را وارد کنید.");
            if (string.IsNullOrWhiteSpace(cardImage)) return new AddCardResult(null, "تصویر کارت بانکی را بارگذاری کنید.");
            var dup = conn.Query<string>("SELECT DataJson FROM Cards WHERE UserId=@userId", new { userId }, tx)
                .Select(j => Deserialize<BankCard>(j)!).Any(c => c.CardNumber == digits);
            if (dup) return new AddCardResult(null, "این کارت قبلاً ثبت شده است.");

            var card = new BankCard
            {
                UserId = userId, UserName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                CardNumber = digits, HolderName = name, CardImage = cardImage.Trim(), Bank = BankFromCard(digits),
                Status = BankCardStatus.Pending, Date = Today(),
            };
            var id = (int)conn.ExecuteScalar<long>(
                "INSERT INTO Cards (UserId, Status, DataJson) VALUES (@UserId,@Status,@DataJson); SELECT last_insert_rowid();",
                new { card.UserId, Status = (int)card.Status, DataJson = Serialize(card) }, tx);
            card.Id = id;
            conn.Execute("UPDATE Cards SET DataJson=@d WHERE Id=@id", new { d = Serialize(card), id }, tx);
            return new AddCardResult(card, null);
        });

    public BankCard? SetCardStatus(int id, BankCardStatus status, string? note) =>
        WriteTx<BankCard?>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Cards WHERE Id=@id", new { id }, tx);
            if (cj is null) return null;
            var card = Deserialize<BankCard>(cj)!;
            card.Status = status; card.Note = note;
            card.RejectionReason = status == BankCardStatus.Rejected ? note : null;
            if (status == BankCardStatus.Approved)
            {
                var owner = LoadUser(conn, tx, card.UserId);
                if (owner is not null && owner.VerificationLevel < 1)
                {
                    owner.VerificationLevel = 1;
                    UpsertUser(conn, tx, owner);
                    AddNotificationTx(conn, tx, owner.Id, "احراز هویت سطح ۱ تأیید شد",
                        "تبریک! احراز هویت سطح یک شما با موفقیت انجام شد و کارت بانکی شما تأیید گردید. اکنون می‌توانید خرید کنید.", "/account/kyc");
                }
            }
            conn.Execute("UPDATE Cards SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)card.Status, d = Serialize(card), id }, tx);
            return card;
        });

    public bool DeleteCard(int id) => DeleteRow("Cards", id);

    private static string BankFromCard(string digits)
    {
        if (digits.Length < 6) return "";
        return digits[..6] switch
        {
            "603799" => "ملی ایران", "589210" => "سپه", "627648" or "207177" => "توسعه صادرات",
            "627961" => "صنعت و معدن", "603770" => "کشاورزی", "628023" => "مسکن", "627760" => "پست بانک",
            "502908" => "توسعه تعاون", "627412" => "اقتصاد نوین", "622106" or "627884" or "639194" => "پارسیان",
            "502229" or "639347" => "پاسارگاد", "627488" or "502910" => "کارآفرین", "621986" => "سامان",
            "639346" => "سینا", "502938" => "دی", "603769" => "صادرات", "610433" or "991975" => "ملت",
            "627353" or "585983" => "تجارت", "589463" => "رفاه", "502806" or "504172" => "شهر",
            "636214" => "آینده", "505785" => "ایران زمین", "636949" => "حکمت ایرانیان", "505416" => "گردشگری",
            "606373" => "قرض‌الحسنه مهر ایران", "628157" => "موسسه اعتباری توسعه", "606256" => "موسسه اعتباری ملل",
            _ => "",
        };
    }


    // ── KYC ─────────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<KycRequest> GetAllKyc(KycStatus? status = null)
    {
        var all = AllJson<KycRequest>("Kyc").AsEnumerable();
        if (status is KycStatus s) all = all.Where(k => k.Status == s);
        return all.OrderByDescending(k => k.Id).ToList();
    }
    public KycRequest? GetKycForUser(int userId) =>
        AllJson<KycRequest>("Kyc").Where(k => k.UserId == userId).OrderByDescending(k => k.Id).FirstOrDefault();

    public KycRequest SubmitKyc(KycRequest input) =>
        WriteTx((conn, tx) =>
        {
            var existingRow = conn.Query("SELECT Id, DataJson FROM Kyc", transaction: tx)
                .FirstOrDefault(r => Deserialize<KycRequest>((string)r.DataJson)!.UserId == input.UserId);
            if (existingRow is null)
            {
                input.Status = KycStatus.Pending; input.Note = null;
                if (string.IsNullOrWhiteSpace(input.Date)) input.Date = Today();
                var id = (int)conn.ExecuteScalar<long>("INSERT INTO Kyc (DataJson) VALUES (@d); SELECT last_insert_rowid();", new { d = Serialize(input) }, tx);
                input.Id = id;
                conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(input), id }, tx);
                return input;
            }
            var existing = Deserialize<KycRequest>((string)existingRow.DataJson)!;
            existing.FullName = input.FullName; existing.NationalId = input.NationalId; existing.BirthDate = input.BirthDate;
            existing.CardImage = input.CardImage; existing.SelfieImage = input.SelfieImage;
            existing.Status = KycStatus.Pending; existing.Note = null; existing.Date = Today();
            conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(existing), id = existing.Id }, tx);
            return existing;
        });

    public KycRequest? SetKycStatus(int id, KycStatus status, string? note) =>
        WriteTx<KycRequest?>((conn, tx) =>
        {
            var kj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Kyc WHERE Id=@id", new { id }, tx);
            if (kj is null) return null;
            var req = Deserialize<KycRequest>(kj)!;
            req.Status = status; req.Note = note;
            req.RejectionReason = status == KycStatus.Rejected ? note : null;
            if (status == KycStatus.Approved)
            {
                var user = LoadUser(conn, tx, req.UserId);
                if (user is not null) { user.VerificationLevel = 2; user.Verified = true; UpsertUser(conn, tx, user); }
            }
            conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(req), id }, tx);
            return req;
        });
}
