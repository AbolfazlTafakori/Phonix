using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Turns approved V2Ray purchases into real accounts on the panel, and keeps trying until they exist.
//
// Approval fires provisioning off but never waits for the panel (see V2RayFulfillmentService), so a server
// that is briefly down, slow, or rate-limiting can't fail an order the customer has already paid for. This
// worker is the safety net behind that: it sweeps the orders still waiting and retries them. Once an account
// is created the unit is delivered, the customer is notified through the ordinary order pipeline, and the
// orders group gets its one delivery message.
//
// The per-order work — the attempts cap, provisioning, and the group message — belongs to the fulfillment
// service, so that an account reaches the customer the same way whether this sweep or the approval got there
// first. All this class decides is when to run.
public class V2RayProvisionWorker : BackgroundService
{
    // Fast enough that a normal purchase is provisioned within a minute, slow enough that a panel which is
    // down isn't hammered.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<V2RayProvisionWorker> _logger;

    public V2RayProvisionWorker(IServiceScopeFactory scopes, ILogger<V2RayProvisionWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "V2Ray provisioning sweep failed; will retry on the next cycle.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var fulfil = scope.ServiceProvider.GetRequiredService<IV2RayFulfillmentService>();

        foreach (var order in store.GetOrdersAwaitingV2Ray())
        {
            if (ct.IsCancellationRequested) return;
            await fulfil.ProvisionOrderAsync(order, ct);
        }
    }
}
