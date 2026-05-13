using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Api.Controllers;

/// <summary>
/// Returns container and environment information.
/// Useful to verify which mode (limits / no-limits) is running.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class InfoController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ContainerInfo), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var process = Process.GetCurrentProcess();

        // Read cgroup memory limit if running in Linux container
        var cgroupMemoryLimit = ReadCgroupMemoryLimit();
        var cgroupCpuLimit = ReadCgroupCpuLimit();

        return Ok(new ContainerInfo(
            MachineName: Environment.MachineName,
            OsDescription: RuntimeInformation.OSDescription,
            ProcessorCount: Environment.ProcessorCount,
            ProcessMemoryMb: (int)(process.WorkingSet64 / 1024 / 1024),
            CgroupMemoryLimitMb: cgroupMemoryLimit,
            CgroupCpuLimit: cgroupCpuLimit,
            DotnetVersion: Environment.Version.ToString(),
            Mode: cgroupMemoryLimit.HasValue ? "WITH LIMITS (cgroups active)" : "NO LIMITS (unrestricted)",
            Tip: "Compare memory and CPU usage with 'docker stats' on your host"
        ));
    }

    /// <summary>
    /// Reads memory limit from cgroup v2 (Linux containers).
    /// Returns null if no limit is set or running on Windows.
    /// </summary>
    private static long? ReadCgroupMemoryLimit()
    {
        try
        {
            // cgroup v2
            var path = "/sys/fs/cgroup/memory.max";
            if (System.IO.File.Exists(path))
            {
                var content = System.IO.File.ReadAllText(path).Trim();
                if (content != "max" && long.TryParse(content, out var bytes))
                    return bytes / 1024 / 1024;
            }

            // cgroup v1
            var pathV1 = "/sys/fs/cgroup/memory/memory.limit_in_bytes";
            if (System.IO.File.Exists(pathV1))
            {
                var content = System.IO.File.ReadAllText(pathV1).Trim();
                if (long.TryParse(content, out var bytes) && bytes < long.MaxValue / 2)
                    return bytes / 1024 / 1024;
            }
        }
        catch { /* not in a Linux container */ }

        return null;
    }

    /// <summary>
    /// Reads CPU quota from cgroup v2.
    /// Returns null if no limit is set.
    /// </summary>
    private static string? ReadCgroupCpuLimit()
    {
        try
        {
            // cgroup v2
            var path = "/sys/fs/cgroup/cpu.max";
            if (System.IO.File.Exists(path))
            {
                var content = System.IO.File.ReadAllText(path).Trim();
                if (content != "max 100000")
                    return content;
            }
        }
        catch { /* not in a Linux container */ }

        return null;
    }
}

public sealed record ContainerInfo(
    string MachineName,
    string OsDescription,
    int ProcessorCount,
    long ProcessMemoryMb,
    long? CgroupMemoryLimitMb,
    string? CgroupCpuLimit,
    string DotnetVersion,
    string Mode,
    string Tip
);
