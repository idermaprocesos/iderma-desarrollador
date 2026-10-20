using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal sealed class SystemMetricsService
{
    public static SystemMetricsService Instance { get; } = new();
    private readonly CpuTracker _cpu = new();

    public SystemSnapshot Capture(IReadOnlyList<ProcessRow> processes, IReadOnlyList<ServiceRow>? services = null)
    {
        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref mem);

        return new SystemSnapshot
        {
            CpuPercent = _cpu.UpdateSystem(),
            MemoryLoad = mem.dwMemoryLoad,
            TotalPhys = mem.ullTotalPhys,
            AvailPhys = mem.ullAvailPhys,
            ProcessCount = processes.Count,
            ThreadCount = processes.Sum(p => p.ThreadCount),
            HandleEstimate = processes.Sum(p => p.HandleCount),
            ServicesRunning = services?.Count(s => s.Status == "En ejecución") ?? 0,
            ServicesStopped = services?.Count(s => s.Status == "Detenido") ?? 0,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };
    }
}
