using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Phonix.Api.Controllers;

// Process-wide in-flight request gauge, fed by a middleware that is only registered when diagnostics are
// enabled (see Program.cs). Lock-free so it adds no contention to the path it measures.
public static class InFlightRequestCounter
{
    private static long _current;
    public static long Current => Interlocked.Read(ref _current);
    public static void Increment() => Interlocked.Increment(ref _current);
    public static void Decrement() => Interlocked.Decrement(ref _current);
}

public record StressMetricsDto(
    long TimestampMs,
    long ActiveRequests,
    int ThreadPoolThreadCount,
    long PendingWorkItems,
    long CompletedWorkItems,
    int WorkerThreadsBusy,
    int WorkerThreadsAvailable,
    int WorkerThreadsMax,
    int WorkerThreadsMin,
    int IoThreadsBusy,
    int IoThreadsAvailable,
    bool ThreadPoolStarvationSuspected,
    int ProcessThreadCount,
    int HandleCount,
    long WorkingSetMb,
    long PrivateMemoryMb,
    long GcHeapMb,
    long GcCommittedMb,
    double GcFragmentationMb,
    long GcTotalAllocatedMb,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double GcPauseTimePercent);

// Temporary load-test telemetry. Disabled by default and only mounted when PHONIX_ENABLE_DIAGNOSTICS=true,
// so it never ships live; returns 404 otherwise. Exposes only aggregate runtime counters (no data), which is
// exactly what's needed to watch thread-pool starvation, GC pressure, and connection growth under load.
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PHONIX_ENABLE_DIAGNOSTICS"), "true", StringComparison.OrdinalIgnoreCase);

    [AllowAnonymous]
    [HttpGet("stress")]
    public ActionResult<StressMetricsDto> Stress()
    {
        if (!Enabled) return NotFound();

        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
        ThreadPool.GetAvailableThreads(out var availWorker, out var availIo);
        ThreadPool.GetMinThreads(out var minWorker, out _);
        var busyWorker = maxWorker - availWorker;
        var pending = ThreadPool.PendingWorkItemCount;

        // Worker pool effectively exhausted while work is queueing is the classic starvation signature.
        var starvation = (availWorker <= 0 && pending > 0) || (busyWorker > minWorker && pending > minWorker);

        using var proc = Process.GetCurrentProcess();
        var handles = 0;
        try { handles = proc.HandleCount; } catch { /* not available on every platform */ }

        var gc = GC.GetGCMemoryInfo();

        return new StressMetricsDto(
            TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActiveRequests: InFlightRequestCounter.Current,
            ThreadPoolThreadCount: ThreadPool.ThreadCount,
            PendingWorkItems: pending,
            CompletedWorkItems: ThreadPool.CompletedWorkItemCount,
            WorkerThreadsBusy: busyWorker,
            WorkerThreadsAvailable: availWorker,
            WorkerThreadsMax: maxWorker,
            WorkerThreadsMin: minWorker,
            IoThreadsBusy: maxIo - availIo,
            IoThreadsAvailable: availIo,
            ThreadPoolStarvationSuspected: starvation,
            ProcessThreadCount: proc.Threads.Count,
            HandleCount: handles,
            WorkingSetMb: proc.WorkingSet64 / (1024 * 1024),
            PrivateMemoryMb: proc.PrivateMemorySize64 / (1024 * 1024),
            GcHeapMb: GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
            GcCommittedMb: gc.TotalCommittedBytes / (1024 * 1024),
            GcFragmentationMb: Math.Round(gc.FragmentedBytes / (1024d * 1024d), 1),
            GcTotalAllocatedMb: GC.GetTotalAllocatedBytes(precise: false) / (1024 * 1024),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            GcPauseTimePercent: Math.Round(gc.PauseTimePercentage, 2));
    }
}
