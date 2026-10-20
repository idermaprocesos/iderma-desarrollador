using System.Diagnostics;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal sealed class TargetIoService
{
    public static TargetIoService Instance { get; } = new();

    private readonly object _gate = new();
    private int _requestId;
    public TargetIoSnapshot? Current { get; private set; }
    public event Action? Updated;

    public void BeginCapture(ProcessRow? row)
    {
        if (row is not { Pid: > 0 })
        {
            lock (_gate) Current = null;
            Updated?.Invoke();
            return;
        }

        var id = Interlocked.Increment(ref _requestId);
        var pid = row.Pid;
        var path = row.Path;
        var name = row.Name;
        _ = Task.Run(() =>
        {
            try
            {
                var snap = Capture(pid, path, name);
                if (id != _requestId) return;
                lock (_gate) Current = snap;
                Updated?.Invoke();
            }
            catch
            {
                if (id != _requestId) return;
                lock (_gate)
                {
                    Current = new TargetIoSnapshot
                    {
                        Pid = pid,
                        ExePath = path,
                        InternetText = "Sin datos",
                        Summary = "No se pudo leer dependencias de este proceso."
                    };
                }
                Updated?.Invoke();
            }
        });
    }

    public void RefreshConnections(int pid)
    {
        TargetIoSnapshot? snap;
        lock (_gate) snap = Current;
        if (snap == null || snap.Pid != pid) return;
        var connections = NetworkMonitorService.ForPid(pid);
        var (internet, lan, listen) = NetworkMonitorService.Counts(connections);
        snap.Connections = connections;
        snap.InternetCount = internet;
        snap.LanCount = lan;
        snap.ListenCount = listen;
        snap.InternetText = InternetCaption(snap.HasNetworkStack, internet, lan, listen);
        snap.Summary = BuildSummary(snap);
    }

    public static TargetIoSnapshot Capture(int pid, string path, string processName)
    {
        var modules = ProcessNative.SnapshotModules(pid);
        var loadedByName = new Dictionary<string, ModuleRow>(StringComparer.OrdinalIgnoreCase);
        var loadedByPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in modules)
        {
            if (!string.IsNullOrEmpty(m.Name) && !loadedByName.ContainsKey(m.Name))
                loadedByName[m.Name] = m;
            if (!string.IsNullOrWhiteSpace(m.Path))
                loadedByPath.Add(m.Path);
        }

        var exe = string.IsNullOrWhiteSpace(path) ? "" : path;
        if (string.IsNullOrWhiteSpace(exe) && loadedByName.Count > 0)
            exe = modules.FirstOrDefault()?.Path ?? "";

        var folder = string.IsNullOrWhiteSpace(exe) ? "" : Path.GetDirectoryName(exe) ?? "";
        var declared = string.IsNullOrWhiteSpace(exe) ? [] : PeImportReader.Read(exe);
        var deps = new List<DependencyRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, kind) in declared)
        {
            seen.Add(name);
            loadedByName.TryGetValue(name, out var mod);
            var local = FindLocal(folder, name);
            var apiSet = name.StartsWith("api-ms-", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("ext-ms-", StringComparison.OrdinalIgnoreCase);
            var status = mod != null ? "Cargada"
                : apiSet ? "Api-set"
                : local != null ? "En carpeta (aún no cargada)"
                : kind == "Delay-load" ? "Delay-load (bajo demanda)"
                : kind == ".NET" ? "Declarada en deps.json"
                : "Importada (no vista en memoria)";
            deps.Add(new DependencyRow
            {
                Name = name,
                Kind = kind,
                Status = status,
                Path = mod?.Path ?? local ?? "",
                Company = mod?.Company ?? TryCompany(local),
                Version = mod?.Version ?? "",
                Detail = apiSet ? "Forwarder del sistema" : kind
            });
        }

        foreach (var mod in modules)
        {
            if (string.IsNullOrEmpty(mod.Name) || !seen.Add(mod.Name)) continue;
            var inFolder = InFolder(folder, mod.Path);
            deps.Add(new DependencyRow
            {
                Name = mod.Name,
                Kind = inFolder ? "Local (carpeta)" : "Cargada (dinámica)",
                Status = "Cargada",
                Path = mod.Path,
                Company = mod.Company,
                Version = mod.Version,
                Detail = inFolder ? "Archivo junto al ejecutable" : "Cargada en runtime"
            });
        }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.dll"))
                {
                    var name = Path.GetFileName(file);
                    if (!seen.Add(name)) continue;
                    deps.Add(new DependencyRow
                    {
                        Name = name,
                        Kind = "En carpeta",
                        Status = "Disponible (no cargada)",
                        Path = file,
                        Company = TryCompany(file),
                        Detail = "Puede cargarse desde la carpeta del programa"
                    });
                }
            }
            catch { /* carpeta protegida */ }
        }

        deps = deps
            .OrderBy(d => StatusRank(d.Status))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var (files, total) = ListFolder(folder, exe, loadedByPath, processName);
        var connections = NetworkMonitorService.ForPid(pid);
        var (internet, lan, listen) = NetworkMonitorService.Counts(connections);
        var stack = HasNetworkStack(modules);
        var snap = new TargetIoSnapshot
        {
            Pid = pid,
            ExePath = exe,
            Folder = folder,
            ImportCount = declared.Count,
            LoadedCount = modules.Count,
            FolderFileCount = files.Count,
            FolderTotal = total,
            InternetCount = internet,
            LanCount = lan,
            ListenCount = listen,
            HasNetworkStack = stack,
            Dependencies = deps,
            FolderFiles = files,
            Connections = connections
        };
        snap.InternetText = InternetCaption(stack, internet, lan, listen);
        snap.Summary = BuildSummary(snap);
        return snap;
    }

    public static string InternetCaption(bool stack, int internet, int lan, int listen)
    {
        if (internet > 0) return $"{internet} conexión(es) a Internet";
        if (lan > 0) return $"{lan} conexión(es) LAN, sin Internet público";
        if (listen > 0) return $"Escucha local ({listen}), sin salida ahora";
        if (stack) return "Pila de red cargada, sin conexiones ahora";
        return "No hay indicios de uso de red";
    }

    private static string BuildSummary(TargetIoSnapshot snap) =>
        $"{snap.Dependencies.Count} dependencias  ·  {snap.FolderTotal} archivos en carpeta  ·  {snap.InternetText}";

    private static bool HasNetworkStack(IEnumerable<ModuleRow> modules)
    {
        string[] names =
        [
            "ws2_32.dll", "wsock32.dll", "winhttp.dll", "wininet.dll", "webio.dll",
            "mswsock.dll", "dnsapi.dll", "httpapi.dll", "winhttp.dll"
        ];
        var set = modules.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Any(set.Contains);
    }

    private static int StatusRank(string status) => status switch
    {
        "Importada (no vista en memoria)" => 0,
        "Delay-load (bajo demanda)" => 1,
        "Declarada en deps.json" => 2,
        "En carpeta (aún no cargada)" => 3,
        "Disponible (no cargada)" => 4,
        "Api-set" => 5,
        _ => 6
    };

    private static string? FindLocal(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        var path = Path.Combine(folder, name);
        return File.Exists(path) ? path : null;
    }

    private static bool InFolder(string folder, string path)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(path)) return false;
        return path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
    }

    private static string TryCompany(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
        try { return FileVersionInfo.GetVersionInfo(path).CompanyName ?? ""; }
        catch { return ""; }
    }

    private static (List<FolderFileRow> files, int total) ListFolder(
        string folder, string exe, HashSet<string> loaded, string processName)
    {
        var list = new List<FolderFileRow>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return (list, 0);

        var total = 0;
        try
        {
            var entries = new List<FileSystemInfo>();
            var dir = new DirectoryInfo(folder);
            entries.AddRange(dir.EnumerateDirectories().Take(200));
            entries.AddRange(dir.EnumerateFiles().Take(1500));
            total = entries.Count;

            var preferred = entries
                .OrderByDescending(e => e is FileInfo && loaded.Contains(e.FullName))
                .ThenByDescending(e => string.Equals(e.FullName, exe, StringComparison.OrdinalIgnoreCase))
                .ThenBy(e => e is FileInfo ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(400)
                .ToList();

            foreach (var e in preferred)
            {
                var isDir = e is DirectoryInfo;
                var file = e as FileInfo;
                var isLoaded = !isDir && loaded.Contains(e.FullName);
                list.Add(new FolderFileRow
                {
                    Name = e.Name,
                    Kind = isDir ? "Carpeta" : (file?.Extension?.TrimStart('.').ToUpperInvariant() ?? "Archivo"),
                    Role = Role(e.FullName, exe, processName, isDir, isLoaded, file?.Extension ?? ""),
                    SizeText = isDir ? "" : FormatUtil.Bytes(file?.Length ?? 0),
                    ModifiedText = FormatUtil.DateTime(e.LastWriteTime),
                    Path = e.FullName,
                    Loaded = isLoaded,
                    IsFolder = isDir
                });
            }
        }
        catch
        {
            // sin permiso
        }

        return (list, total);
    }

    private static string Role(string path, string exe, string processName, bool folder, bool loaded, string ext)
    {
        if (folder) return "Subcarpeta";
        if (string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) return "Ejecutable";
        if (loaded) return "Dependencia en uso";
        ext = ext.ToLowerInvariant();
        return ext switch
        {
            ".dll" => "Biblioteca local",
            ".exe" => "Ejecutable auxiliar",
            ".json" or ".config" or ".xml" or ".ini" or ".yml" or ".yaml" => "Configuración",
            ".pdb" => "Símbolos",
            ".deps.json" => "Manifiesto .NET",
            ".ico" or ".png" or ".jpg" or ".svg" => "Recurso",
            ".dat" or ".db" or ".sqlite" => "Datos",
            ".log" => "Registro",
            _ => Path.GetFileNameWithoutExtension(path).Equals(Path.GetFileNameWithoutExtension(processName), StringComparison.OrdinalIgnoreCase)
                ? "Relacionado"
                : "Archivo"
        };
    }
}
