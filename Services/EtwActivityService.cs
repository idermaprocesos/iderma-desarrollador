using System.Collections.Concurrent;
using Consola.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Consola.Services;

internal sealed class EtwActivityService : IDisposable
{
    public static EtwActivityService Instance { get; } = new();

    private TraceEventSession? _session;
    private Task? _pump;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<SyscallEvent> _queue = new();
    private readonly ConcurrentQueue<SyscallEvent> _recent = new();
    private readonly ConcurrentDictionary<int, string> _names = new();

    public bool FileIo { get; set; } = true;
    public bool Registry { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool Image { get; set; } = true;
    public bool Process { get; set; } = true;
    public bool Exceptions { get; set; } = true;
    public bool Syscalls { get; set; }
    public int? FilterPid { get; set; }
    public int MaxBuffered { get; set; } = 8000;

    public bool IsRunning => _session != null;
    public string? LastError { get; private set; }

    public event Action<SyscallEvent>? EventReceived;

    public bool Start()
    {
        Stop();
        LastError = null;
        try
        {
            var name = "Consola-ETW-" + Guid.NewGuid().ToString("N")[..8];
            _session = new TraceEventSession(name);
            _cts = new CancellationTokenSource();

            var keywords = KernelTraceEventParser.Keywords.None;
            if (FileIo) keywords |= KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.DiskFileIO;
            if (Registry) keywords |= KernelTraceEventParser.Keywords.Registry;
            if (Network) keywords |= KernelTraceEventParser.Keywords.NetworkTCPIP;
            if (Image) keywords |= KernelTraceEventParser.Keywords.ImageLoad;
            if (Process) keywords |= KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread;
            if (Syscalls) keywords |= KernelTraceEventParser.Keywords.SystemCall;

            _session.EnableKernelProvider(keywords);

            try
            {
                _session.EnableProvider("Microsoft-Windows-DotNETRuntime", TraceEventLevel.Informational,
                    0x1 | 0x8 | 0x10 | 0x8000); // GC, Contention, Exception, AllocationTick
            }
            catch
            {
                // proveedor .NET opcional
            }

            var source = _session.Source;
            source.Kernel.FileIOCreate += data => Enqueue("Archivo", data.ProcessID, data.ThreadID, data.ProcessName, "Create", data.FileName, "");
            source.Kernel.FileIORead += data => Enqueue("Archivo", data.ProcessID, data.ThreadID, data.ProcessName, "Read", $"{data.FileName}  {data.IoSize} B", "");
            source.Kernel.FileIOWrite += data => Enqueue("Archivo", data.ProcessID, data.ThreadID, data.ProcessName, "Write", $"{data.FileName}  {data.IoSize} B", "");
            source.Kernel.FileIODelete += data => Enqueue("Archivo", data.ProcessID, data.ThreadID, data.ProcessName, "Delete", data.FileName, "");
            source.Kernel.RegistryOpen += data => Enqueue("Registro", data.ProcessID, data.ThreadID, data.ProcessName, "Open", data.KeyName, Status(data.Status));
            source.Kernel.RegistryQueryValue += data => Enqueue("Registro", data.ProcessID, data.ThreadID, data.ProcessName, "QueryValue", $"{data.KeyName}\\{data.ValueName}", Status(data.Status));
            source.Kernel.RegistrySetValue += data => Enqueue("Registro", data.ProcessID, data.ThreadID, data.ProcessName, "SetValue", $"{data.KeyName}\\{data.ValueName}", Status(data.Status));
            source.Kernel.RegistryDelete += data => Enqueue("Registro", data.ProcessID, data.ThreadID, data.ProcessName, "DeleteKey", data.KeyName, Status(data.Status));
            source.Kernel.TcpIpConnect += data => Enqueue("Red", data.ProcessID, data.ThreadID, data.ProcessName, "TCP Connect", $"{data.daddr}:{data.dport} ← {data.saddr}:{data.sport}", "");
            source.Kernel.TcpIpAccept += data => Enqueue("Red", data.ProcessID, data.ThreadID, data.ProcessName, "TCP Accept", $"{data.daddr}:{data.dport}", "");
            source.Kernel.TcpIpRecv += data => Enqueue("Red", data.ProcessID, data.ThreadID, data.ProcessName, "TCP Recv", $"{data.daddr}:{data.dport}  {data.size} B", "");
            source.Kernel.TcpIpSend += data => Enqueue("Red", data.ProcessID, data.ThreadID, data.ProcessName, "TCP Send", $"{data.daddr}:{data.dport}  {data.size} B", "");
            source.Kernel.ImageLoad += data => Enqueue("Módulo", data.ProcessID, data.ThreadID, data.ProcessName, "Load", data.FileName, "");
            source.Kernel.ImageUnload += data => Enqueue("Módulo", data.ProcessID, data.ThreadID, data.ProcessName, "Unload", data.FileName, "");
            source.Kernel.ProcessStart += data => Enqueue("Proceso", data.ProcessID, data.ThreadID, data.ProcessName, "Start", $"padre={data.ParentID}  {data.CommandLine}", "");
            source.Kernel.ProcessStop += data => Enqueue("Proceso", data.ProcessID, data.ThreadID, data.ProcessName, "Stop", $"exit={data.ExitStatus}", "");
            if (Syscalls)
            {
                source.Kernel.PerfInfoSysClEnter += data => Enqueue("Syscall", data.ProcessID, data.ThreadID, data.ProcessName, "Enter", data.ToString() ?? "", "");
            }

            source.Dynamic.All += data =>
            {
                if (data.ProviderName != "Microsoft-Windows-DotNETRuntime") return;
                if (data.EventName.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                    data.EventName.Contains("GC", StringComparison.OrdinalIgnoreCase) ||
                    data.EventName.Contains("Allocation", StringComparison.OrdinalIgnoreCase) ||
                    data.EventName.Contains("Contention", StringComparison.OrdinalIgnoreCase))
                {
                    Enqueue(".NET", data.ProcessID, data.ThreadID, data.ProcessName, data.EventName, Summarize(data), "");
                }
            };

            var token = _cts.Token;
            _pump = Task.Factory.StartNew(() =>
            {
                try { source.Process(); }
                catch (Exception ex) { LastError = ex.Message; }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            SessionState.Current.EtwRunning = true;
            SessionState.Current.EtwFailed = false;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SessionState.Current.EtwRunning = false;
            SessionState.Current.EtwFailed = true;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _session?.Source.StopProcessing(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        _pump = null;
        _cts = null;
        SessionState.Current.EtwRunning = false;
    }

    public List<SyscallEvent> DequeueBatch(int max)
    {
        var list = new List<SyscallEvent>(Math.Min(max, _queue.Count));
        while (list.Count < max && _queue.TryDequeue(out var item))
            list.Add(item);
        return list;
    }

    public void Dispose() => Stop();

    private void Enqueue(string kind, int pid, int tid, string process, string operation, string detail, string result)
    {
        if (FilterPid is { } filter && filter != 0 && pid != filter) return;
        if (string.IsNullOrWhiteSpace(process))
            process = _names.GetOrAdd(pid, p =>
            {
                try { return System.Diagnostics.Process.GetProcessById(p).ProcessName; }
                catch { return $"pid:{p}"; }
            });
        else
            _names[pid] = process;

        if (_queue.Count > MaxBuffered)
        {
            _queue.TryDequeue(out _);
        }

        var evt = new SyscallEvent
        {
            Timestamp = DateTimeOffset.Now,
            Kind = kind,
            Pid = pid,
            Tid = tid,
            Process = process,
            Operation = operation,
            Detail = detail ?? "",
            Result = result
        };
        _queue.Enqueue(evt);
        _recent.Enqueue(evt);
        while (_recent.Count > 400) _recent.TryDequeue(out _);
        EventReceived?.Invoke(evt);
        SessionState.Current.LiveEventCount++;
    }

    public List<SyscallEvent> RecentSnapshot(int? pid, int max = 80)
    {
        var all = _recent.ToArray();
        IEnumerable<SyscallEvent> rows = all;
        if (pid is { } p and not 0)
            rows = rows.Where(e => e.Pid == p);
        return rows.TakeLast(max).Reverse().ToList();
    }

    private static string Status(int status) => status == 0 ? "OK" : "0x" + status.ToString("X8");

    private static string Summarize(TraceEvent data)
    {
        try
        {
            var parts = new List<string>();
            for (var i = 0; i < Math.Min(4, data.PayloadNames.Length); i++)
            {
                var value = data.PayloadValue(i);
                if (value == null) continue;
                parts.Add($"{data.PayloadNames[i]}={value}");
            }
            return string.Join("  ", parts);
        }
        catch
        {
            return data.EventName;
        }
    }
}
