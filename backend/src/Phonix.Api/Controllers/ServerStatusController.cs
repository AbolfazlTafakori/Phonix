using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record ServerStatusDto(
    double CpuPercent,
    long RamUsedMb,
    long RamTotalMb,
    int UptimeDays,
    int UptimeHours,
    int UptimeMinutes,
    string Status);

// Live metrics for the dashboard's "وضعیت سرور" widget. Memory and uptime are read straight from the
// current process (cheap, snapshot values); CPU% is a rate that needs two samples over a window, so it is
// produced by ServerMetricsCollector in the background and read here lock-free. Available to any staff
// member — it backs the always-on dashboard, so it is not gated by an assignable AdminPermission.
[ApiController]
[Route("api/admin/server-status")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class ServerStatusController : ControllerBase
{
    private readonly ServerMetricsCollector _metrics;
    public ServerStatusController(ServerMetricsCollector metrics) => _metrics = metrics;

    [HttpGet]
    public ServerStatusDto Get()
    {
        using var process = Process.GetCurrentProcess();

        var ramUsedMb = process.WorkingSet64 / (1024 * 1024);
        var ramTotalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        if (ramTotalMb <= 0) ramTotalMb = ramUsedMb;

        // How long this API process has been up. Minutes are reported too: without them the widget reads
        // "۰ روز و ۰ ساعت" for a whole hour after every restart, which looks broken rather than fresh.
        var uptime = DateTime.Now - process.StartTime;
        if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;

        return new ServerStatusDto(
            CpuPercent: Math.Round(_metrics.CpuPercent, 1),
            RamUsedMb: ramUsedMb,
            RamTotalMb: ramTotalMb,
            UptimeDays: uptime.Days,
            UptimeHours: uptime.Hours,
            UptimeMinutes: uptime.Minutes,
            Status: "Online");
    }
}
