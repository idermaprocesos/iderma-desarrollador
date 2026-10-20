using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal static class ProcessNative
{
    public static IntPtr OpenQuery(int pid, bool vmRead = false)
    {
        uint access = NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
        if (vmRead) access |= NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_QUERY_INFORMATION;
        var handle = NativeMethods.OpenProcess(access, false, pid);
        if (handle != IntPtr.Zero) return handle;
        return NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
    }

    public static string? QueryPath(IntPtr handle, char[]? buffer = null)
    {
        buffer ??= new char[1024];
        var size = buffer.Length;
        if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) || size <= 0) return null;
        return new string(buffer, 0, size);
    }

    public static string Architecture(IntPtr handle)
    {
        if (!Environment.Is64BitProcess) return "x86";
        if (NativeMethods.IsWow64Process(handle, out var wow64) && wow64) return "x86";
        return "x64";
    }

    public static bool TryMemory(IntPtr handle, out NativeMethods.PROCESS_MEMORY_COUNTERS_EX counters)
    {
        counters = new NativeMethods.PROCESS_MEMORY_COUNTERS_EX { cb = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS_EX>() };
        return NativeMethods.GetProcessMemoryInfo(handle, out counters, counters.cb);
    }

    public static string? QueryCommandLine(IntPtr handle)
    {
        try
        {
            NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out var length);
            if (length <= 0 || length > 256 * 1024) return null;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                var status = NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, buffer, length, out length);
                if (status != 0 || length < Marshal.SizeOf<NativeMethods.UNICODE_STRING>()) return null;
                var us = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buffer);
                if (us.Buffer == IntPtr.Zero || us.Length == 0) return null;
                var start = buffer.ToInt64();
                var end = start + length;
                var ptr = us.Buffer.ToInt64();
                if (ptr < start || ptr + us.Length > end) return null;
                return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    public static int QueryParentPid(IntPtr handle)
    {
        var size = Marshal.SizeOf<NativeMethods.PROCESS_BASIC_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessBasicInformation, buffer, size, out _);
            if (status != 0) return 0;
            var info = Marshal.PtrToStructure<NativeMethods.PROCESS_BASIC_INFORMATION>(buffer);
            return info.InheritedFromUniqueProcessId.ToInt32();
        }
        catch
        {
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string QueryUser(IntPtr processHandle)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out var token))
            return "";
        try
        {
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, IntPtr.Zero, 0, out var needed);
            if (needed == 0) return "";
            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, buffer, needed, out needed))
                    return "";
                var sidAndAttr = Marshal.PtrToStructure<NativeMethods.SID_AND_ATTRIBUTES>(buffer);
                var name = new char[256];
                var domain = new char[256];
                uint nameLen = (uint)name.Length;
                uint domainLen = (uint)domain.Length;
                if (!NativeMethods.LookupAccountSid(null, sidAndAttr.Sid, name, ref nameLen, domain, ref domainLen, out _))
                    return "";
                var user = new string(name, 0, (int)nameLen);
                var dom = new string(domain, 0, (int)domainLen);
                return string.IsNullOrEmpty(dom) ? user : $"{dom}\\{user}";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    public static (bool elevated, string integrity) QueryToken(IntPtr processHandle)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out var token))
            return (false, "");
        try
        {
            var elevated = false;
            var elevSize = Marshal.SizeOf<int>() + IntPtr.Size;
            var elevBuf = Marshal.AllocHGlobal(elevSize);
            try
            {
                if (NativeMethods.GetTokenInformation(token, NativeMethods.TOKEN_ELEVATION, elevBuf, (uint)elevSize, out _))
                    elevated = Marshal.ReadInt32(elevBuf) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(elevBuf);
            }

            NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, IntPtr.Zero, 0, out var needed);
            var integrity = "";
            if (needed > 0)
            {
                var buf = Marshal.AllocHGlobal((int)needed);
                try
                {
                    if (NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buf, needed, out _))
                    {
                        var sidAndAttr = Marshal.PtrToStructure<NativeMethods.SID_AND_ATTRIBUTES>(buf);
                        var subAuthCount = Marshal.ReadByte(sidAndAttr.Sid, 1);
                        var rid = Marshal.ReadInt32(sidAndAttr.Sid, 4 + 4 * (subAuthCount - 1));
                        integrity = rid switch
                        {
                            >= 0x4000 => "Sistema",
                            >= 0x3000 => "Alto",
                            >= 0x2000 => "Medio",
                            >= 0x1000 => "Bajo",
                            _ => "Sin etiqueta"
                        };
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }

            return (elevated, integrity);
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    public readonly record struct ProcessSnap(int Pid, int ParentPid, int Threads, int PriorityBase, string ExeName);

    public static Dictionary<int, int> SnapshotParents() =>
        SnapshotProcessTable().ToDictionary(s => s.Pid, s => s.ParentPid);

    public static List<ProcessSnap> SnapshotProcessTable()
    {
        var list = new List<ProcessSnap>(256);
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var entry = new NativeMethods.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>() };
            if (!NativeMethods.Process32First(snap, ref entry)) return list;
            do
            {
                if (entry.th32ProcessID == 0) continue;
                list.Add(new ProcessSnap(
                    (int)entry.th32ProcessID,
                    (int)entry.th32ParentProcessID,
                    (int)entry.cntThreads,
                    entry.pcPriClassBase,
                    entry.szExeFile ?? ""));
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return list;
    }

    public static Dictionary<int, string> SnapshotWindowTitles()
    {
        var map = new Dictionary<int, string>();
        var buf = new char[512];
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;
            var n = NativeMethods.GetWindowText(hwnd, buf, buf.Length);
            if (n <= 0) return true;
            var title = new string(buf, 0, n).Trim();
            if (title.Length == 0) return true;
            var key = (int)pid;
            if (!map.TryGetValue(key, out var prev) || title.Length > prev.Length)
                map[key] = title;
            return true;
        }, IntPtr.Zero);
        return map;
    }

    public static List<ThreadRow> SnapshotThreads(int pid)
    {
        var list = new List<ThreadRow>();
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var entry = new NativeMethods.THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.THREADENTRY32>() };
            if (!NativeMethods.Thread32First(snap, ref entry)) return list;
            do
            {
                if (entry.th32OwnerProcessID != (uint)pid)
                {
                    continue;
                }

                var start = "";
                var cpu = "";
                var state = "Listo";
                var hThread = NativeMethods.OpenThread(NativeMethods.THREAD_QUERY_INFORMATION, false, entry.th32ThreadID);
                if (hThread != IntPtr.Zero)
                {
                    try
                    {
                        if (NativeMethods.NtQueryInformationThread(hThread, NativeMethods.ThreadQuerySetWin32StartAddress, out var addr, IntPtr.Size, IntPtr.Zero) == 0)
                            start = FormatUtil.Hex(addr);
                        if (NativeMethods.GetThreadTimes(hThread, out _, out _, out var kernel, out var user))
                        {
                            var total = TimeSpan.FromTicks(kernel + user);
                            cpu = FormatUtil.Duration(total);
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hThread);
                    }
                }

                list.Add(new ThreadRow
                {
                    Tid = (int)entry.th32ThreadID,
                    Pid = pid,
                    Priority = entry.tpBasePri,
                    StartAddress = start,
                    CpuTime = cpu,
                    State = state
                });
            } while (NativeMethods.Thread32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }

        return list;
    }

    public static List<ModuleRow> SnapshotModules(int pid)
    {
        var list = new List<ModuleRow>();
        var handle = OpenQuery(pid, vmRead: true);
        if (handle == IntPtr.Zero) return list;
        try
        {
            var modules = new IntPtr[1024];
            if (!NativeMethods.EnumProcessModulesEx(handle, modules, (uint)(IntPtr.Size * modules.Length), out var needed, NativeMethods.LIST_MODULES_ALL))
                return list;
            var count = (int)(needed / IntPtr.Size);
            count = Math.Min(count, modules.Length);
            var pathBuf = new char[1024];
            for (var i = 0; i < count; i++)
            {
                var n = NativeMethods.GetModuleFileNameEx(handle, modules[i], pathBuf, (uint)pathBuf.Length);
                var path = n > 0 ? new string(pathBuf, 0, (int)n) : "";
                NativeMethods.GetModuleInformation(handle, modules[i], out var info, (uint)Marshal.SizeOf<NativeMethods.MODULEINFO>());
                string company = "";
                string version = "";
                try
                {
                    if (File.Exists(path))
                    {
                        var vi = FileVersionInfo.GetVersionInfo(path);
                        company = vi.CompanyName ?? "";
                        version = vi.FileVersion ?? "";
                    }
                }
                catch { /* ignore version lookup */ }

                var name = string.IsNullOrEmpty(path) ? modules[i].ToString("X") : Path.GetFileName(path);
                var isClr = name.Contains("clr", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("coreclr", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("mscorlib", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase);

                list.Add(new ModuleRow
                {
                    Name = name,
                    Path = path,
                    BaseAddress = FormatUtil.Hex(info.lpBaseOfDll),
                    Size = FormatUtil.Bytes(info.SizeOfImage),
                    Company = company,
                    Version = version,
                    IsClr = isClr
                });
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return list;
    }
}

internal sealed class CpuTracker
{
    private readonly ConcurrentDictionary<int, (TimeSpan cpu, DateTime at)> _last = new();
    private (long idle, long kernel, long user, DateTime at) _system;

    public double UpdateProcess(int pid, TimeSpan totalProcessorTime)
    {
        var now = DateTime.UtcNow;
        var cpu = 0d;
        if (_last.TryGetValue(pid, out var prev))
        {
            var deltaCpu = (totalProcessorTime - prev.cpu).TotalMilliseconds;
            var deltaWall = (now - prev.at).TotalMilliseconds * Environment.ProcessorCount;
            if (deltaWall > 0) cpu = Math.Clamp(deltaCpu / deltaWall * 100.0, 0, 100);
        }
        _last[pid] = (totalProcessorTime, now);
        return cpu;
    }

    public double UpdateSystem()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var now = DateTime.UtcNow;
        var cpu = 0d;
        if (_system.at != default)
        {
            var idleDelta = idle - _system.idle;
            var totalDelta = (kernel - _system.kernel) + (user - _system.user);
            if (totalDelta > 0) cpu = Math.Clamp((1.0 - idleDelta / (double)totalDelta) * 100.0, 0, 100);
        }
        _system = (idle, kernel, user, now);
        return cpu;
    }

    public void Prune(HashSet<int> alive)
    {
        foreach (var key in _last.Keys)
        {
            if (!alive.Contains(key)) _last.TryRemove(key, out _);
        }
    }
}
