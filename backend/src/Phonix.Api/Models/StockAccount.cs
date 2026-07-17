using System.Text;

namespace Phonix.Api.Models;

// One numbered seat on a shared inventory account. Statuses reuse StockItemStatus so the whole stock domain
// speaks one lifecycle (Available → Reserved → Delivered, Disabled out of rotation).
public class StockSlot
{
    public int Id { get; set; }            // unique within its account; used to address it from the panel
    public int Index { get; set; }         // 0-based position — consecutive allocation runs over this
    public string Label { get; set; } = ""; // display identifier ("A0", "B7", …), minted once at creation
    public StockItemStatus Status { get; set; } = StockItemStatus.Available;
    // When Reserved/Delivered: the order unit that consumed it, so support can trace "which seats did this
    // buyer get" straight from the pool.
    public int? OrderId { get; set; }
    public int? UnitId { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}

// A multi-seat inventory account (a shared subscription): one set of credentials serving `Capacity` numbered
// slots. The admin enters the account once; the slots are generated automatically and each one lives its own
// lifecycle. Fulfillment always seats one purchase on CONSECUTIVE free slots of a single account.
public class StockAccount
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Username { get; set; } = "";
    // Stored encrypted (SensitiveField) — live credentials get the same at-rest protection as StockItem
    // payloads, so plain backups never carry them.
    public string Password { get; set; } = "";
    public string Plan { get; set; } = "";
    public int Capacity { get; set; }
    public int Months { get; set; }
    public bool Disabled { get; set; }     // takes the whole account out of rotation without losing history
    public List<StockSlot> Slots { get; set; } = new();
    public string? AddedBy { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    // Slot identifier for a 0-based index: a letter block per ten seats, then the digit — A0…A9, B0…B9, …
    // Past "Z9" the letters roll to two characters (AA0…) so ANY capacity keeps a unique, ordered label.
    public static string SlotLabel(int index)
    {
        var letters = new StringBuilder();
        var block = index / 10;
        do
        {
            letters.Insert(0, (char)('A' + block % 26));
            block = block / 26 - 1;
        } while (block >= 0);
        return $"{letters}{index % 10}";
    }

    public static List<StockSlot> GenerateSlots(int capacity) =>
        Enumerable.Range(0, capacity)
            .Select(i => new StockSlot { Id = i + 1, Index = i, Label = SlotLabel(i) })
            .ToList();
}
