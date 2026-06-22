using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record AddCardResult(BankCard? Card, string? Error);

public partial class StoreData
{
    private readonly List<BankCard> _cards = new();
    private int _cardSeq;

    public IReadOnlyList<BankCard> GetAllCards(BankCardStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<BankCard> q = _cards;
            if (status is BankCardStatus s) q = q.Where(c => c.Status == s);
            return q.OrderByDescending(c => c.Id).ToList();
        }
    }

    public IReadOnlyList<BankCard> GetUserCards(int userId)
    {
        lock (_gate) return _cards.Where(c => c.UserId == userId).OrderByDescending(c => c.Id).ToList();
    }

    public BankCard? GetCard(int id)
    {
        lock (_gate) return _cards.FirstOrDefault(c => c.Id == id);
    }

    // Registers a bank card (the level-1 identity step). The user supplies the card number, the name on
    // the card, and a photo of it; the card stays Pending until an admin approves it — and approval lifts
    // the owner to level 1. Any logged-in user can register a card (it's how a level-0 user upgrades).
    public AddCardResult AddCard(int userId, string cardNumber, string holderName, string cardImage)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return new AddCardResult(null, "کاربر یافت نشد.");

            var digits = new string((cardNumber ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length != 16) return new AddCardResult(null, "شماره کارت باید ۱۶ رقم باشد.");
            var name = (holderName ?? "").Trim();
            if (name.Length == 0) return new AddCardResult(null, "نام صاحب کارت را وارد کنید.");
            if (string.IsNullOrWhiteSpace(cardImage)) return new AddCardResult(null, "تصویر کارت بانکی را بارگذاری کنید.");
            if (_cards.Any(c => c.UserId == userId && c.CardNumber == digits))
                return new AddCardResult(null, "این کارت قبلاً ثبت شده است.");

            var card = new BankCard
            {
                Id = ++_cardSeq,
                UserId = userId,
                UserName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                CardNumber = digits,
                HolderName = name,
                CardImage = cardImage.Trim(),
                Bank = BankFromCard(digits),
                Status = BankCardStatus.Pending,
                Date = Today(),
            };
            _cards.Add(card);
            return new AddCardResult(card, null);
        }
    }

    public BankCard? SetCardStatus(int id, BankCardStatus status, string? note)
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(c => c.Id == id);
            if (card is null) return null;
            card.Status = status;
            card.Note = note;
            // surface an explicit rejection reason to the user; clear it when not a rejection.
            card.RejectionReason = status == BankCardStatus.Rejected ? note : null;
            // approving a bank card lifts the owner to level 1 (permanent — never lowered) and, on that
            // first rise, sends them a private congratulation notification (per-user, not a broadcast).
            if (status == BankCardStatus.Approved)
            {
                var owner = _users.FirstOrDefault(u => u.Id == card.UserId);
                if (owner is not null && owner.VerificationLevel < 1)
                {
                    owner.VerificationLevel = 1;
                    AddNotification(owner.Id, "احراز هویت سطح ۱ تأیید شد",
                        "تبریک! احراز هویت سطح یک شما با موفقیت انجام شد و کارت بانکی شما تأیید گردید. اکنون می‌توانید خرید کنید.",
                        "/account/kyc");
                }
            }
            return card;
        }
    }

    public bool DeleteCard(int id)
    {
        lock (_gate)
        {
            var card = _cards.FirstOrDefault(c => c.Id == id);
            if (card is null) return false;
            _cards.Remove(card);
            return true;
        }
    }

    // Resolves the issuing bank from the card's 6-digit BIN. Best-effort over the common Iranian banks;
    // unknown prefixes return empty (the card number alone is still enough for staff to verify).
    private static string BankFromCard(string digits)
    {
        if (digits.Length < 6) return "";
        var bin = digits[..6];
        return bin switch
        {
            "603799" => "ملی ایران",
            "589210" => "سپه",
            "627648" or "207177" => "توسعه صادرات",
            "627961" => "صنعت و معدن",
            "603770" => "کشاورزی",
            "628023" => "مسکن",
            "627760" => "پست بانک",
            "502908" => "توسعه تعاون",
            "627412" => "اقتصاد نوین",
            "622106" or "627884" or "639194" => "پارسیان",
            "502229" or "639347" => "پاسارگاد",
            "627488" or "502910" => "کارآفرین",
            "621986" => "سامان",
            "639346" => "سینا",
            "502938" => "دی",
            "603769" => "صادرات",
            "610433" or "991975" => "ملت",
            "627353" or "585983" => "تجارت",
            "589463" => "رفاه",
            "502806" or "504172" => "شهر",
            "636214" => "آینده",
            "505785" => "ایران زمین",
            "636949" => "حکمت ایرانیان",
            "505416" => "گردشگری",
            "606373" => "قرض‌الحسنه مهر ایران",
            "628157" => "موسسه اعتباری توسعه",
            "606256" => "موسسه اعتباری ملل",
            _ => "",
        };
    }
}
