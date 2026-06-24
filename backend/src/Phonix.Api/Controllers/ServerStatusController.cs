using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record ServerStatusDto(
    double CpuPercent,
    long RamUsedMb,
    long RamTotalMb,
    int UptimeDays,
    int UptimeHours,
    string Status);

// Live metrics for the dashboard's "وضعیت سرور" widget. Scoped to the current application process
// (WorkingSet / processor time / start time) so it works the same whether the API runs bare-metal,
// in a container, or behind a reverse proxy. Available to any staff member — it backs the always-on
// dashboard, so it is not gated by an assignable AdminPermission.
[ApiController]
[Route("api/admin/server-status")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class ServerStatusController : ControllerBase
{
    // CPU% is a RATE, not a snapshot: it needs two samples of total processor time across a wall-clock
    // window. The dashboard polls every few seconds, so we keep the previous sample between requests and
    // report the average utilisation over that gap, normalised by core count. Guarded because the static
    // state is shared across concurrent requests.
    private static readonly object _gate = new();
    private static TimeSpan _lastCpuTime;
    private static DateTime _lastSampleUtc;

    [HttpGet]
    public ServerStatusDto Get()
    {
        using var process = Process.GetCurrentProcess();

        var ramUsedMb = process.WorkingSet64 / (1024 * 1024);
        var ramTotalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        if (ramTotalMb <= 0) ramTotalMb = ramUsedMb;

        var uptime = DateTime.Now - process.StartTime;

        return new ServerStatusDto(
            CpuPercent: Math.Round(SampleCpu(process), 1),
            RamUsedMb: ramUsedMb,
            RamTotalMb: ramTotalMb,
            UptimeDays: Math.Max(0, uptime.Days),
            UptimeHours: Math.Max(0, uptime.Hours),
            Status: "Online");
    }

    private static double SampleCpu(Process process)
    {
        lock (_gate)
        {
            var nowUtc = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime;

            // First sample after a (re)start has no window to measure against — seed it and report 0.
            if (_lastSampleUtc == default)
            {
                _lastSampleUtc = nowUtc;
                _lastCpuTime = cpuTime;
                return 0;
            }

            var wallMs = (nowUtc - _lastSampleUtc).TotalMilliseconds;
            var cpuMs = (cpuTime - _lastCpuTime).TotalMilliseconds;

            _lastSampleUtc = nowUtc;
            _lastCpuTime = cpuTime;

            if (wallMs <= 0) return 0;

            var percent = cpuMs / (wallMs * Environment.ProcessorCount) * 100.0;
            return Math.Clamp(percent, 0, 100);
        }
    }
}
