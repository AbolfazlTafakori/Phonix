using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Turns approved V2Ray purchases into real accounts on the panel, and keeps trying until they exist.
//
// Approval itself never waits for the panel (see V2RayFulfillmentService), so a server that is briefly down,
// slow, or rate-limiting can't fail an order the customer has already paid for. This worker sweeps the
// orders that are waiting and provisions them; once an account is created the unit is delivered and the
// customer is notified through the ordinary order pipeline.
public class V2RayProvisionWorker : BackgroundService
{
    // Fast enough that a normal purchase is provisioned within a minute, slow enough that a panel which is
    // down isn't hammered.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    // A unit that keeps failing is left for staff rather than retried forever.
    private const int MaxAttempts = 20;

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
            foreach (var unit in order.Units.Where(u => !u.Delivered && !u.Rejected).ToList())
            {
                if (ct.IsCancellationRequested) return;
                if ((unit.V2Ray?.Attempts ?? 0) >= MaxAttempts) continue;
                if (!fulfil.Handles(unit)) continue;
                await fulfil.ProvisionAsync(order, unit, ct);
            }
        }
    }
}
