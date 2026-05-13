using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Api.Controllers;

/// <summary>
/// Stress endpoints — forces CPU and memory consumption intentionally.
/// Use this to demonstrate the difference between containers
/// running with and without resource limits (cgroups).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class StressController : ControllerBase
{
    private readonly ILogger<StressController> _logger;

    // In-memory list to simulate memory leak / high usage
    private static readonly List<byte[]> _memoryStore = new();
    private static readonly object _lock = new();

    public StressController(ILogger<StressController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Allocates memory blocks to simulate high memory usage.
    /// WHY: Without --memory limit, this will consume all host memory.
    /// With limit, the kernel (via cgroups) will kill the container.
    /// </summary>
    [HttpPost("memory")]
    [ProducesResponseType(typeof(MemoryStressResult), StatusCodes.Status200OK)]
    public IActionResult StressMemory([FromQuery] int megabytes = 50)
    {
        if (megabytes <= 0 || megabytes > 512)
            return BadRequest(new { error = "megabytes must be between 1 and 512" });

        _logger.LogWarning("⚠ Memory stress started: allocating {MB}MB", megabytes);

        var before = GC.GetTotalMemory(false) / 1024 / 1024;

        lock (_lock)
        {
            // Allocate requested MB
            var block = new byte[megabytes * 1024 * 1024];
            // Fill with data to prevent GC optimization
            Array.Fill(block, (byte)1);
            _memoryStore.Add(block);
        }

        var after = GC.GetTotalMemory(false) / 1024 / 1024;

        _logger.LogWarning("⚠ Memory allocated: {Before}MB → {After}MB", before, after);

        return Ok(new MemoryStressResult(
            AllocatedMb: megabytes,
            TotalAllocatedMb: _memoryStore.Sum(b => b.Length) / 1024 / 1024,
            ProcessMemoryMb: (int)(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024),
            Message: "Memory allocated. Run 'docker stats' to see the impact."
        ));
    }

    /// <summary>
    /// Releases all allocated memory blocks.
    /// </summary>
    [HttpDelete("memory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ReleaseMemory()
    {
        lock (_lock)
        {
            _memoryStore.Clear();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        _logger.LogInformation("✓ Memory released");

        return Ok(new { message = "Memory released", processMemoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024 });
    }

    /// <summary>
    /// Runs CPU-intensive work for a given duration.
    /// WHY: Without --cpus limit, this uses 100% of all available cores.
    /// With limit, cgroups throttles the CPU usage.
    /// </summary>
    [HttpPost("cpu")]
    [ProducesResponseType(typeof(CpuStressResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> StressCpu([FromQuery] int seconds = 10, [FromQuery] int threads = 4)
    {
        if (seconds <= 0 || seconds > 60)
            return BadRequest(new { error = "seconds must be between 1 and 60" });

        if (threads <= 0 || threads > 16)
            return BadRequest(new { error = "threads must be between 1 and 16" });

        _logger.LogWarning("⚠ CPU stress started: {Threads} threads for {Seconds}s", threads, seconds);

        var sw = Stopwatch.StartNew();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

        var tasks = Enumerable.Range(0, threads).Select(_ =>
            Task.Run(() =>
            {
                long counter = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    // Pure CPU work
                    counter++;
                    Math.Sqrt(counter);
                }
                return counter;
            }, cts.Token)
        ).ToList();

        long[] results;
        try
        {
            results = await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            results = Array.Empty<long>();
        }

        sw.Stop();

        _logger.LogWarning("⚠ CPU stress finished: {Elapsed}ms", sw.ElapsedMilliseconds);

        return Ok(new CpuStressResult(
            Threads: threads,
            DurationSeconds: seconds,
            ElapsedMs: sw.ElapsedMilliseconds,
            TotalIterations: results.Sum(),
            Message: "CPU stress complete. Check 'docker stats' output during stress."
        ));
    }

    /// <summary>
    /// Returns current resource usage of this process.
    /// Use this to monitor memory and CPU in real time.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ContainerStats), StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var process = Process.GetCurrentProcess();

        return Ok(new ContainerStats(
            ProcessMemoryMb: (int)(process.WorkingSet64 / 1024 / 1024),
            GcMemoryMb: (int)(GC.GetTotalMemory(false) / 1024 / 1024),
            AllocatedBlocksMb: _memoryStore.Sum(b => b.Length) / 1024 / 1024,
            CpuCores: Environment.ProcessorCount,
            MachineName: Environment.MachineName,
            OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Tip: "Run 'docker stats' on your host to see real-time container resource usage"
        ));
    }
}

public sealed record MemoryStressResult(
    int AllocatedMb,
    long TotalAllocatedMb,
    long ProcessMemoryMb,
    string Message
);

public sealed record CpuStressResult(
    int Threads,
    int DurationSeconds,
    long ElapsedMs,
    long TotalIterations,
    string Message
);

public sealed record ContainerStats(
    long ProcessMemoryMb,
    long GcMemoryMb,
    long AllocatedBlocksMb,
    int CpuCores,
    string MachineName,
    string OsDescription,
    string Tip
);
