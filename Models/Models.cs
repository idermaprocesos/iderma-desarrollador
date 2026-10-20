using CommunityToolkit.Mvvm.ComponentModel;
using Consola.Helpers;

namespace Consola.Models;

public partial class ProcessRow : ObservableObject
{
    [ObservableProperty] private int _pid;
    [ObservableProperty] private int _parentPid;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _commandLine = "";
    [ObservableProperty] private string _architecture = "";
    [ObservableProperty] private string _integrity = "";
    [ObservableProperty] private bool _isManaged;
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private bool _responding = true;
    [ObservableProperty] private bool _accessDenied;
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private long _workingSet;
    [ObservableProperty] private long _privateBytes;
    [ObservableProperty] private long _virtualMemory;
    [ObservableProperty] private long _peakWorkingSet;
    [ObservableProperty] private uint _pageFaults;
    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _handleCount;
    [ObservableProperty] private int _gdiCount;
    [ObservableProperty] private int _userObjectCount;
    [ObservableProperty] private int _sessionId;
    [ObservableProperty] private string _priority = "";
    [ObservableProperty] private DateTime? _startTime;
    [ObservableProperty] private string _windowTitle = "";
    [ObservableProperty] private ulong _ioReadBytes;
    [ObservableProperty] private ulong _ioWriteBytes;
    [ObservableProperty] private double _ioReadRate;
    [ObservableProperty] private double _ioWriteRate;
    [ObservableProperty] private string _runtime = "";
    [ObservableProperty] private LeakVerdict? _leak;

    public string MemoryText => FormatUtil.Bytes(PrivateBytes);
    public string WorkingSetText => FormatUtil.Bytes(WorkingSet);
    public string CpuText => FormatUtil.Percent(CpuPercent);
    public string StartText => StartTime is { } t ? FormatUtil.DateTime(t) : "—";
    public string ManagedText => IsManaged ? (string.IsNullOrWhiteSpace(Runtime) ? ".NET" : Runtime) : "Nativo";
    public string StatusText => AccessDenied ? "Sin acceso" : Responding ? "OK" : "No responde";
    public string LeakText => Leak?.Summary ?? "";
    public string ComboLabel => string.IsNullOrWhiteSpace(WindowTitle)
        ? $"{Name}  ({Pid})"
        : $"{Name}  ({Pid})  —  {WindowTitle}";

    public override string ToString() => ComboLabel;

    partial void OnPrivateBytesChanged(long value) => OnPropertyChanged(nameof(MemoryText));
    partial void OnWorkingSetChanged(long value) => OnPropertyChanged(nameof(WorkingSetText));
    partial void OnCpuPercentChanged(double value) => OnPropertyChanged(nameof(CpuText));
    partial void OnStartTimeChanged(DateTime? value) => OnPropertyChanged(nameof(StartText));
    partial void OnIsManagedChanged(bool value) => OnPropertyChanged(nameof(ManagedText));
    partial void OnRuntimeChanged(string value) => OnPropertyChanged(nameof(ManagedText));
    partial void OnAccessDeniedChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnRespondingChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnLeakChanged(LeakVerdict? value) => OnPropertyChanged(nameof(LeakText));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(ComboLabel));
    partial void OnPidChanged(int value) => OnPropertyChanged(nameof(ComboLabel));
    partial void OnWindowTitleChanged(string value) => OnPropertyChanged(nameof(ComboLabel));

    public void CopyFrom(ProcessRow o)
    {
        if (Name != o.Name) Name = o.Name;
        if (ParentPid != o.ParentPid) ParentPid = o.ParentPid;
        if (!string.IsNullOrEmpty(o.Path) && Path != o.Path) Path = o.Path;
        if (!string.IsNullOrEmpty(o.User) && User != o.User) User = o.User;
        if (!string.IsNullOrEmpty(o.Company) && Company != o.Company) Company = o.Company;
        if (!string.IsNullOrEmpty(o.Description) && Description != o.Description) Description = o.Description;
        if (!string.IsNullOrEmpty(o.CommandLine) && CommandLine != o.CommandLine) CommandLine = o.CommandLine;
        if (Architecture != o.Architecture) Architecture = o.Architecture;
        if (Integrity != o.Integrity) Integrity = o.Integrity;
        if (IsManaged != o.IsManaged) IsManaged = o.IsManaged;
        if (IsElevated != o.IsElevated) IsElevated = o.IsElevated;
        if (Responding != o.Responding) Responding = o.Responding;
        if (AccessDenied != o.AccessDenied) AccessDenied = o.AccessDenied;
        if (Math.Abs(CpuPercent - o.CpuPercent) > 0.05) CpuPercent = o.CpuPercent;
        if (WorkingSet != o.WorkingSet) WorkingSet = o.WorkingSet;
        if (PrivateBytes != o.PrivateBytes) PrivateBytes = o.PrivateBytes;
        if (VirtualMemory != o.VirtualMemory) VirtualMemory = o.VirtualMemory;
        if (PeakWorkingSet != o.PeakWorkingSet) PeakWorkingSet = o.PeakWorkingSet;
        if (PageFaults != o.PageFaults) PageFaults = o.PageFaults;
        if (ThreadCount != o.ThreadCount) ThreadCount = o.ThreadCount;
        if (HandleCount != o.HandleCount) HandleCount = o.HandleCount;
        if (GdiCount != o.GdiCount) GdiCount = o.GdiCount;
        if (UserObjectCount != o.UserObjectCount) UserObjectCount = o.UserObjectCount;
        if (SessionId != o.SessionId) SessionId = o.SessionId;
        if (Priority != o.Priority) Priority = o.Priority;
        if (StartTime != o.StartTime) StartTime = o.StartTime;
        if (WindowTitle != o.WindowTitle) WindowTitle = o.WindowTitle;
        if (IoReadBytes != o.IoReadBytes) IoReadBytes = o.IoReadBytes;
        if (IoWriteBytes != o.IoWriteBytes) IoWriteBytes = o.IoWriteBytes;
        if (Math.Abs(IoReadRate - o.IoReadRate) > 1) IoReadRate = o.IoReadRate;
        if (Math.Abs(IoWriteRate - o.IoWriteRate) > 1) IoWriteRate = o.IoWriteRate;
        if (Runtime != o.Runtime) Runtime = o.Runtime;
        if (!ReferenceEquals(Leak, o.Leak)) Leak = o.Leak;
    }
}

public sealed class ProcessNode
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public string Name { get; init; } = "";
    public string Label => $"{Name}  ({Pid})";
    public List<ProcessNode> Children { get; } = [];
}

public sealed class ThreadRow
{
    public int Tid { get; init; }
    public int Pid { get; init; }
    public int Priority { get; init; }
    public string StartAddress { get; init; } = "";
    public string CpuTime { get; init; } = "";
    public string State { get; init; } = "";
    public string WaitReason { get; init; } = "";
}

public sealed class ModuleRow
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string BaseAddress { get; init; } = "";
    public string Size { get; init; } = "";
    public string Company { get; init; } = "";
    public string Version { get; init; } = "";
    public bool IsClr { get; init; }
}

public sealed class ServiceRow
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Status { get; init; } = "";
    public string StartType { get; init; } = "";
    public int Pid { get; init; }
    public string Account { get; init; } = "";
    public string Path { get; init; } = "";
    public string Description { get; init; } = "";
    public bool CanStop { get; init; }
}

public sealed class SyscallEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string Kind { get; init; } = "";
    public int Pid { get; init; }
    public int Tid { get; init; }
    public string Process { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Result { get; init; } = "";
    public string TimeText => FormatUtil.Time(Timestamp);
}

public sealed class FaultEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "";
    public string Source { get; init; } = "";
    public int EventId { get; init; }
    public int? Pid { get; init; }
    public string Process { get; init; } = "";
    public string Message { get; init; } = "";
    public string TimeText => Timestamp.ToLocalTime().ToString("dd/MM HH:mm:ss");
}

public sealed class MemorySample
{
    public DateTimeOffset Timestamp { get; init; }
    public long PrivateBytes { get; init; }
    public long WorkingSet { get; init; }
    public int Handles { get; init; }
    public int Threads { get; init; }
    public int Gdi { get; init; }
    public int UserObjects { get; init; }
    public uint PageFaults { get; init; }
    public string TimeText => FormatUtil.Time(Timestamp);
    public string PrivateText => FormatUtil.Bytes(PrivateBytes);
    public string WorkingSetText => FormatUtil.Bytes(WorkingSet);
}

public sealed class LeakVerdict
{
    public bool IsLeak { get; init; }
    public double Score { get; init; }
    public string Summary { get; init; } = "";
    public string Details { get; init; } = "";
    public string Kind { get; init; } = "";
}

public sealed class HeapTypeStat
{
    public string Type { get; init; } = "";
    public int Count { get; init; }
    public long Size { get; init; }
    public string SizeText => FormatUtil.Bytes(Size);
}

public sealed class ManagedThreadInfo
{
    public int Osnr { get; init; }
    public int Tid { get; init; }
    public string Kind { get; init; } = "";
    public bool IsAlive { get; init; }
    public bool HasException { get; init; }
    public string Exception { get; init; } = "";
    public string Stack { get; init; } = "";
}

public sealed class ClrInspectResult
{
    public string Runtime { get; set; } = "";
    public string Dac { get; set; } = "";
    public int AppDomainCount { get; set; }
    public int ThreadCount { get; set; }
    public int ModuleCount { get; set; }
    public bool CanWalkHeap { get; set; }
    public long HeapSize { get; set; }
    public int ObjectCount { get; set; }
    public string HeapSizeText => FormatUtil.Bytes(HeapSize);
    public string Summary { get; set; } = "";
    public List<string> AppDomains { get; } = [];
    public List<string> Modules { get; } = [];
    public List<ManagedThreadInfo> Threads { get; } = [];
    public List<HeapTypeStat> TopTypes { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class SystemSnapshot
{
    public double CpuPercent { get; init; }
    public uint MemoryLoad { get; init; }
    public ulong TotalPhys { get; init; }
    public ulong AvailPhys { get; init; }
    public ulong UsedPhys => TotalPhys > AvailPhys ? TotalPhys - AvailPhys : 0;
    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public int HandleEstimate { get; init; }
    public int ServicesRunning { get; init; }
    public int ServicesStopped { get; init; }
    public TimeSpan Uptime { get; init; }
    public string Machine { get; init; } = Environment.MachineName;
    public string Os { get; init; } = Environment.OSVersion.ToString();
    public string UsedMemoryText => FormatUtil.Bytes((long)UsedPhys);
    public string TotalMemoryText => FormatUtil.Bytes((long)TotalPhys);
}

public sealed class DashboardAlert
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Severity { get; init; } = "Informational";
    public int? Pid { get; init; }
}

public sealed class DependencyRow
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Status { get; init; } = "";
    public string Path { get; init; } = "";
    public string Company { get; init; } = "";
    public string Version { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class FolderFileRow
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Role { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string ModifiedText { get; init; } = "";
    public string Path { get; init; } = "";
    public bool Loaded { get; init; }
    public bool IsFolder { get; init; }
    public string LoadedText => Loaded ? "En uso" : IsFolder ? "Carpeta" : "En disco";
}

public sealed class NetConnectionRow
{
    public string Protocol { get; init; } = "";
    public string Local { get; init; } = "";
    public string Remote { get; init; } = "";
    public string State { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool IsInternet { get; init; }
}

public sealed class TargetIoSnapshot
{
    public int Pid { get; init; }
    public string ExePath { get; init; } = "";
    public string Folder { get; init; } = "";
    public string InternetText { get; set; } = "";
    public string Summary { get; set; } = "";
    public int ImportCount { get; init; }
    public int LoadedCount { get; init; }
    public int FolderFileCount { get; init; }
    public int FolderTotal { get; init; }
    public int InternetCount { get; set; }
    public int LanCount { get; set; }
    public int ListenCount { get; set; }
    public bool HasNetworkStack { get; init; }
    public List<DependencyRow> Dependencies { get; init; } = [];
    public List<FolderFileRow> FolderFiles { get; init; } = [];
    public List<NetConnectionRow> Connections { get; set; } = [];
}

public sealed class DebugLine
{
    public DateTimeOffset Timestamp { get; init; }
    public int Pid { get; init; }
    public string Text { get; init; } = "";
    public string TimeText => FormatUtil.Time(Timestamp);
}

public sealed class WindowInfoRow
{
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public bool Visible { get; init; }
    public string VisibleText => Visible ? "Visible" : "Oculta";
}

public sealed class InnerSnapshot
{
    public int Pid { get; init; }
    public string ConsoleStatus { get; init; } = "";
    public string ConsoleText { get; init; } = "";
    public string Narrative { get; init; } = "";
    public string ManagedThought { get; set; } = "";
    public List<DebugLine> DebugLines { get; init; } = [];
    public List<WindowInfoRow> Windows { get; init; } = [];
    public List<SyscallEvent> RecentOps { get; init; } = [];
}
