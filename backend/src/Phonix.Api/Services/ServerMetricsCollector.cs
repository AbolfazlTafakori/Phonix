using System.Diagnostics;

namespace Phonix.Api.Services;

// Samples this process's CPU utilisation on a fixed cadence in the background and publishes the latest
// value lock-free. Request threads previously computed CPU% inline against shared static fields, so two
// concurrent dashboard polls corrupted each other's sampling window; here a single owner does the sampling
// and the endpoint performs a cheap atomic read.
public sealed class ServerMetricsCollector : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);

    private double _cpuPercent;

    public double CpuPercent => Interlocked.CompareExchange(ref _cpuPercent, 0d, 0d);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var process = Process.GetCurrentProcess();
        var lastCpuTime = process.TotalProcessorTime;
        var lastSampleUtc = DateTime.UtcNow;

        using var timer = new PeriodicTimer(SampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    process.Refresh();
                    var cpuTime = process.TotalProcessorTime;
                    var nowUtc = DateTime.UtcNow;

                    var wallMs = (nowUtc - lastSampleUtc).TotalMilliseconds;
                    var cpuMs = (cpuTime - lastCpuTime).TotalMilliseconds;
                    lastCpuTime = cpuTime;
                    lastSampleUtc = nowUtc;

                    if (wallMs > 0)
                    {
                        var percent = cpuMs / (wallMs * Environment.ProcessorCount) * 100.0;
                        Interlocked.Exchange(ref _cpuPercent, Math.Clamp(percent, 0, 100));
                    }
                }
                catch
                {
                    // a transient sampling failure must not stop the collector
                }
            }
        }
        catch (OperationCanceledException)
        {
            // host is shutting down
        }
    }
}
