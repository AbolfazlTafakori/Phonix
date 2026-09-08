using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Closes support tickets that have gone quiet. A ticket stays open only while a conversation is alive; once
// a full day passes with neither side writing, it closes on its own instead of sitting in the queue forever
// making the open-ticket badge meaningless.
//
// Silence is measured from the LAST message, whoever sent it, so answering a customer restarts the clock and
// so does their reply. The customer is told, and replying on a closed ticket reopens it (see ReplyTicket) —
// the close is a tidy-up, not a door shut in their face.
public class TicketAutoCloseWorker : BackgroundService
{
    private readonly IDataStore _store;
    private readonly ILogger<TicketAutoCloseWorker> _logger;

    private static readonly TimeSpan IdleFor = TimeSpan.FromHours(24);
    // Well under the idle window, so a ticket closes within a few minutes of becoming due rather than up to
    // an hour late, and cheap enough to run: one pass over the ticket table.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    public TicketAutoCloseWorker(IDataStore store, ILogger<TicketAutoCloseWorker> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var t in _store.CloseIdleTickets(IdleFor))
                {
                    _store.AddNotification(t.UserId, "تیکت پشتیبانی بسته شد",
                        $"تیکت «{t.Subject}» پس از ۲۴ ساعت بدون پیام جدید به‌صورت خودکار بسته شد. اگر هنوز به کمک نیاز دارید، همان‌جا پاسخ دهید تا دوباره باز شود.",
                        "/account/tickets");
                    _logger.LogInformation("Auto-closed idle ticket {Code}", t.Code);
                }
            }
            catch (Exception ex)
            {
                // never let a bad cycle kill the worker — log and try again next interval.
                _logger.LogError(ex, "Ticket auto-close sweep failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
