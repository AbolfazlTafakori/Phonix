using Phonix.Api.Admin;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Live "needs attention" counters for the admin sidebar badges.
public sealed record AdminBadgeCounts(
    int PendingOrders,
    int PendingTransactions,
    int OpenTickets,
    int PendingKyc,
    int PendingCards,
    int PendingComments)
{
    public int For(AdminBadge badge) => badge switch
    {
        AdminBadge.PendingOrders       => PendingOrders,
        AdminBadge.PendingTransactions => PendingTransactions,
        AdminBadge.OpenTickets         => OpenTickets,
        AdminBadge.PendingKyc          => PendingKyc,
        AdminBadge.PendingCards        => PendingCards,
        AdminBadge.PendingComments     => PendingComments,
        _ => 0,
    };
}

public partial class StoreData
{
    // One cheap pass under the existing gate — computed server-side and shipped WITH the menu so the
    // sidebar makes a single round-trip (no per-item count queries).
    public AdminBadgeCounts GetAdminBadgeCounts()
    {
        lock (_gate)
            return new AdminBadgeCounts(
                PendingOrders:       _orders.Count(o => o.Status == OrderStatus.PendingApproval),
                PendingTransactions: _transactions.Count(t => t.Status == TxStatus.Pending),
                OpenTickets:         _tickets.Count(t => t.Status == TicketStatus.Open),
                PendingKyc:          _kyc.Count(k => k.Status == KycStatus.Pending),
                PendingCards:        _cards.Count(c => c.Status == BankCardStatus.Pending),
                PendingComments:     _comments.Count(c => c.Status == CommentStatus.Pending));
    }
}
