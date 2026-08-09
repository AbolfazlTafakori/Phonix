using Phonix.Api.Admin;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// The result and argument types IDataStore speaks in. They used to be scattered through the JSON store's
// partials, which made them look like that store's own types; they are the store CONTRACT, so they live in one
// place beside the interface and outlived the implementation that happened to declare them.

// A store operation that either produced a thing or explains, in the customer's language, why it could not.
public record StaffResult(AppUser? User, string? Error);
public record AddCardResult(BankCard? Card, string? Error);
public record DiscountResult(DiscountCode? Code, long Amount, string? Error);
public record WithdrawalResult(Transaction? Tx, string? Error);
public record PlaceOrderResult(Order? Order, string? Error);
public record OrderActionResult(Order? Order, string? Error);

// What the customer supplied for ONE account of an order line at checkout, and for the line as a whole.
public record OrderUnitInfo(List<OrderInputValue> Inputs, string? Note);
// `RenewToken`, when set, marks the line as extending a V2Ray config the buyer already holds rather than
// buying a new one. It is validated before it gets here (see OrdersController.ResolveRenewal), so the store
// only has to carry it onto the unit fulfilment will read it from.
public record OrderLineInfo(IReadOnlyList<OrderUnitInfo>? Units, string? RenewToken = null);

// The out-of-band part of a partly-paid order: the card the buyer sent money to and the proof they attached.
public record RemainderPayment(int? CardId, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);

// One subscription coming up for renewal, as the reminder worker needs it.
public sealed record RenewalReminder(int UserId, string Email, string OrderCode, string ExpiresFa);

// One provisioned V2Ray account flattened out of its order, which is all the monitor needs to pair it with
// what the panel reports and decide whether it is due a warning or due to be cleared away.
public sealed record V2RayServiceRef(
    int OrderId, int UnitId, string OrderCode, int UserId,
    int PanelId, string Email, string Token,
    DateTime? ExpiresAtUtc, long VolumeGb,
    bool ExpiryWarned, bool VolumeWarned);

// Who to tell, once a warning or a removal has actually been claimed. Null is returned instead when another
// pass (or another server in the cluster) got there first, which is what makes each message go out once.
public sealed record V2RayNotifyTarget(int UserId, string Email, string OrderCode, string Token);

// Live "needs attention" counters for the admin sidebar badges.
public sealed record AdminBadgeCounts(
    int PendingOrders,
    int PreparingOrders,
    int PendingTransactions,
    int OpenTickets,
    int PendingKyc,
    int PendingCards,
    int PendingComments,
    int UnreadChats,
    int PendingSeatInfo)
{
    public int For(AdminBadge badge) => badge switch
    {
        AdminBadge.PendingOrders       => PendingOrders,
        AdminBadge.PreparingOrders     => PreparingOrders,
        AdminBadge.PendingTransactions => PendingTransactions,
        AdminBadge.OpenTickets         => OpenTickets,
        AdminBadge.PendingKyc          => PendingKyc,
        AdminBadge.PendingCards        => PendingCards,
        AdminBadge.PendingComments     => PendingComments,
        AdminBadge.UnreadChats         => UnreadChats,
        AdminBadge.PendingSeatInfo     => PendingSeatInfo,
        _ => 0,
    };
}
