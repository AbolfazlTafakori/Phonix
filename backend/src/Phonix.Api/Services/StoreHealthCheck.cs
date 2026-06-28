using Microsoft.Extensions.Diagnostics.HealthChecks;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Liveness/readiness probe: the app is only healthy once the durable store has loaded its data.
// Exposed at /health for Docker healthchecks and external uptime monitors.
public class StoreHealthCheck : IHealthCheck
{
    private readonly IDataStore _store;

    public StoreHealthCheck(IDataStore store) => _store = store;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var users = _store.GetUsers(null, null, null).Count();
        var data = new Dictionary<string, object> { ["users"] = users };
        return Task.FromResult(users > 0
            ? HealthCheckResult.Healthy("store loaded", data)
            : HealthCheckResult.Unhealthy("store has no users", data: data));
    }
}
