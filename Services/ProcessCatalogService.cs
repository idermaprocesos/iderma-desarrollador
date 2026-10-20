using System.Diagnostics;
using System.Collections.Concurrent;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal sealed class ProcessCatalogService
{
    public static ProcessCatalogService Instance { get; } = new();

    private readonly CpuTracker _cpu = new();
    private readonly ConcurrentDictionary<int, (ulong read, ulong write, DateTime at)> _io = new();
    private readonly ConcurrentDictionary<string, (string company, string description)> _version = new();
    private readonly ConcurrentDictionary<int, string> _users = new();
    private readonly ConcurrentDictionary<int, string> _commands = new();
    private readonly ConcurrentDictionary<int, string> _paths = new();
    private readonly ConcurrentDictionary<int, string> _arch = new();
    private readonly ConcurrentDictionary<int, (bool elevated, string integrity)> _tokens = new();
    private readonly ConcurrentDictionary<int, (int gdi, int user)> _gui = new();
    private readonly char[] _pathBuf = new char[1024];
    private int _cycle;

    public IReadOnlyList<ProcessRow> LastSnapshot => _live;
    private readonly List<ProcessRow> _live = [];
    private readonly Dictionary<int, ProcessRow> _byPid = [];

    public List<ProcessRow> Capture()
    {
        var snaps = ProcessNative.SnapshotProcessTable();
        var titles = ProcessNative.SnapshotWindowTitles();
        var alive = new HashSet<int>();
        var rows = new List<ProcessRow>(snaps.Count);
        var cycle = _cycle++;
        var readGui = cycle > 0 && cycle % 4 == 0;

        foreach (var snap in snaps)
        {
            try
            {
                var row = ReadProcess(snap, titles, readGui);
                rows.Add(row);
                alive.Add(row.Pid);
            }
            catch
            {
                // proceso desapareció durante el muestreo
            }
        }

        _cpu.Prune(alive);
        Prune(_io, alive);
        Prune(_users, alive);
        Prune(_commands, alive);
        Prune(_paths, alive);
        Prune(_arch, alive);
        Prune(_tokens, alive);
        Prune(_gui, alive);
        return rows;
    }

    public void EnrichDetails(ProcessRow row)
    {
        var handle = ProcessNative.OpenQuery(row.Pid, vmRead: true);
        if (handle == IntPtr.Zero) return;
        try
        {
            var path = row.Path;
            if (string.IsNullOrEmpty(path))
                path = ProcessNative.QueryPath(handle) ?? "";

            if (!_commands.TryGetValue(row.Pid, out var cmd))
            {
                cmd = ProcessNative.QueryCommandLine(handle) ?? "";
                if (!string.IsNullOrEmpty(cmd)) _commands[row.Pid] = cmd;
            }

            string company = "", description = "";
            if (!string.IsNullOrEmpty(path) && !_version.TryGetValue(path, out var ver))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var info = FileVersionInfo.GetVersionInfo(path);
                        ver = (info.CompanyName ?? "", info.FileDescription ?? "");
                        _version[path] = ver;
                    }
                }
                catch { ver = ("", ""); }
            }
            if (!string.IsNullOrEmpty(path) && _version.TryGetValue(path, out var cached))
            {
                company = cached.company;
                description = cached.description;
            }

            var pid = row.Pid;
            App.Ui?.TryEnqueue(() =>
            {
                if (row.Pid != pid) return;
                if (!string.IsNullOrEmpty(path)) row.Path = path;
                if (!string.IsNullOrEmpty(cmd)) row.CommandLine = cmd;
                if (!string.IsNullOrEmpty(company)) row.Company = company;
                if (!string.IsNullOrEmpty(description)) row.Description = description;
            });
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public IReadOnlyList<ProcessRow> MergeOnUi(List<ProcessRow> captured)
    {
        var seen = new HashSet<int>(captured.Count);
        foreach (var src in captured)
        {
            seen.Add(src.Pid);
            if (_byPid.TryGetValue(src.Pid, out var dst))
                dst.CopyFrom(src);
            else
            {
                _byPid[src.Pid] = src;
                _live.Add(src);
            }
        }

        for (var i = _live.Count - 1; i >= 0; i--)
        {
            if (seen.Contains(_live[i].Pid)) continue;
            _byPid.Remove(_live[i].Pid);
            _live.RemoveAt(i);
        }

        return _live;
    }

    public List<ProcessNode> BuildTree(IEnumerable<ProcessRow> rows)
    {
        var nodes = rows.ToDictionary(r => r.Pid, r => new ProcessNode
        {
            Pid = r.Pid,
            ParentPid = r.ParentPid,
            Name = r.Name
        });

        var roots = new List<ProcessNode>();
        foreach (var node in nodes.Values)
        {
            if (node.ParentPid != 0 && nodes.TryGetValue(node.ParentPid, out var parent) && parent.Pid != node.Pid)
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        Sort(roots);
        return roots.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

        static void Sort(List<ProcessNode> list)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var n in list) Sort(n.Children);
        }
    }

    private static void Prune<T>(ConcurrentDictionary<int, T> map, HashSet<int> alive)
    {
        foreach (var pid in map.Keys)
        {
            if (!alive.Contains(pid)) map.TryRemove(pid, out _);
        }
    }

    private ProcessRow ReadProcess(ProcessNative.ProcessSnap snap, Dictionary<int, string> titles, bool readGui)
    {
        var name = snap.ExeName;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        NativeMethods.ProcessIdToSessionId((uint)snap.Pid, out var session);
        titles.TryGetValue(snap.Pid, out var title);

        var row = new ProcessRow
        {
            Pid = snap.Pid,
            Name = string.IsNullOrWhiteSpace(name) ? "¿?" : name,
            ThreadCount = snap.Threads,
            SessionId = (int)session,
            Priority = snap.PriorityBase.ToString(),
            Responding = true,
            WindowTitle = title ?? "",
            ParentPid = snap.ParentPid
        };

        var handle = ProcessNative.OpenQuery(snap.Pid, vmRead: false);
        if (handle == IntPtr.Zero)
        {
            row.AccessDenied = true;
            return row;
        }

        try
        {
            if (NativeMethods.GetProcessTimes(handle, out var creation, out _, out var kernel, out var userTime))
            {
                try { if (creation > 0) row.StartTime = DateTime.FromFileTime(creation); } catch { }
                row.CpuPercent = _cpu.UpdateProcess(snap.Pid, TimeSpan.FromTicks(kernel + userTime));
            }

            if (!_paths.TryGetValue(snap.Pid, out var path))
            {
                path = ProcessNative.QueryPath(handle, _pathBuf) ?? "";
                if (path.Length > 0) _paths[snap.Pid] = path;
            }
            row.Path = path;

            if (!_arch.TryGetValue(snap.Pid, out var arch))
            {
                arch = ProcessNative.Architecture(handle);
                _arch[snap.Pid] = arch;
            }
            row.Architecture = arch;
            if (ProcessNative.TryMemory(handle, out var mem))
            {
                row.WorkingSet = (long)mem.WorkingSetSize;
                row.PrivateBytes = (long)mem.PrivateUsage;
                row.PeakWorkingSet = (long)mem.PeakWorkingSetSize;
                row.PageFaults = mem.PageFaultCount;
            }

            if (NativeMethods.GetProcessHandleCount(handle, out var handles))
                row.HandleCount = (int)handles;

            if (readGui)
            {
                row.GdiCount = (int)NativeMethods.GetGuiResources(handle, NativeMethods.GR_GDIOBJECTS);
                row.UserObjectCount = (int)NativeMethods.GetGuiResources(handle, NativeMethods.GR_USEROBJECTS);
                _gui[snap.Pid] = (row.GdiCount, row.UserObjectCount);
            }
            else if (_gui.TryGetValue(snap.Pid, out var gui))
            {
                row.GdiCount = gui.gdi;
                row.UserObjectCount = gui.user;
            }

            if (NativeMethods.GetProcessIoCounters(handle, out var io))
            {
                row.IoReadBytes = io.ReadTransferCount;
                row.IoWriteBytes = io.WriteTransferCount;
                var now = DateTime.UtcNow;
                if (_io.TryGetValue(snap.Pid, out var prev))
                {
                    var sec = (now - prev.at).TotalSeconds;
                    if (sec > 0)
                    {
                        row.IoReadRate = (io.ReadTransferCount - prev.read) / sec;
                        row.IoWriteRate = (io.WriteTransferCount - prev.write) / sec;
                    }
                }
                _io[snap.Pid] = (io.ReadTransferCount, io.WriteTransferCount, now);
            }

            if (!_users.TryGetValue(snap.Pid, out var user))
            {
                user = ProcessNative.QueryUser(handle);
                if (!string.IsNullOrEmpty(user)) _users[snap.Pid] = user;
            }
            row.User = user;

            if (!_tokens.TryGetValue(snap.Pid, out var token))
            {
                token = ProcessNative.QueryToken(handle);
                _tokens[snap.Pid] = token;
            }
            row.IsElevated = token.elevated;
            row.Integrity = token.integrity;

            if (_commands.TryGetValue(snap.Pid, out var cmd))
                row.CommandLine = cmd;
            if (!string.IsNullOrEmpty(row.Path) && _version.TryGetValue(row.Path, out var ver))
            {
                row.Company = ver.company;
                row.Description = ver.description;
            }

            row.IsManaged = IsLikelyManaged(row.Name, row.Path);
            if (row.IsManaged) row.Runtime = ".NET";
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return row;
    }

    private static bool IsLikelyManaged(string name, string path)
    {
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("w3wp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("iisexpress", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrEmpty(path)) return false;
        return path.Contains(@"\dotnet\", StringComparison.OrdinalIgnoreCase)
               || path.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase);
    }
}
