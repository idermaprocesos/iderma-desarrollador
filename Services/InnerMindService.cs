using System.Text;
using Consola.Models;

namespace Consola.Services;

internal static class InnerMindService
{
    public static InnerSnapshot Capture(ProcessRow row, string? managedThought = null)
    {
        var (consoleStatus, consoleText) = ProcessConsoleReader.Read(row.Pid);
        var windows = ProcessWindowProbe.ForPid(row.Pid);
        var debug = DebugOutputService.Instance.Snapshot(row.Pid, 200);
        var ops = EtwActivityService.Instance.RecentSnapshot(row.Pid, 80);
        var narrative = BuildNarrative(row, consoleStatus, consoleText, windows, debug, ops, managedThought);
        return new InnerSnapshot
        {
            Pid = row.Pid,
            ConsoleStatus = consoleStatus,
            ConsoleText = consoleText,
            Narrative = narrative,
            ManagedThought = managedThought ?? "",
            DebugLines = debug,
            Windows = windows,
            RecentOps = ops
        };
    }

    public static string TrySampleManaged(int pid)
    {
        try
        {
            using var target = Microsoft.Diagnostics.Runtime.DataTarget.CreateSnapshotAndAttach(pid);
            if (target.ClrVersions.Length == 0) return "";
            using var runtime = target.ClrVersions[0].CreateRuntime();
            var lines = new List<string>();
            foreach (var thread in runtime.Threads)
            {
                if (!thread.IsAlive) continue;
                string? top = null;
                try
                {
                    foreach (var frame in thread.EnumerateStackTrace().Take(8))
                    {
                        if (frame.Method == null) continue;
                        var type = frame.Method.Type?.Name;
                        var name = frame.Method.Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (IsNoise(type, name)) continue;
                        top = string.IsNullOrEmpty(type) ? name : $"{type}.{name}";
                        break;
                    }
                }
                catch { continue; }

                if (thread.CurrentException != null)
                    lines.Add($"Excepción en hilo {thread.ManagedThreadId}: {thread.CurrentException.Type?.Name}: {thread.CurrentException.Message}");
                else if (!string.IsNullOrEmpty(top))
                    lines.Add($"Hilo {thread.ManagedThreadId} en {top}");
                if (lines.Count >= 8) break;
            }
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return "No se pudo muestrear el runtime .NET: " + ex.Message;
        }
    }

    private static bool IsNoise(string? type, string name)
    {
        if (name is "WaitOne" or "WaitSleepJoin" or "Wait" or "Sleep" or "Dispatch") return true;
        if (type != null && (type.StartsWith("System.Threading", StringComparison.Ordinal) ||
                             type.StartsWith("System.Runtime", StringComparison.Ordinal)))
            return name is "Wait" or "WaitOne" or "ExecuteInternal";
        return false;
    }

    private static string BuildNarrative(
        ProcessRow row,
        string consoleStatus,
        string consoleText,
        List<WindowInfoRow> windows,
        List<DebugLine> debug,
        List<SyscallEvent> ops,
        string? managed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{row.Name} ({row.Pid}) — {row.StatusText}. CPU {row.CpuText}, memoria {row.MemoryText}, {row.ThreadCount} hilos.");
        if (!string.IsNullOrWhiteSpace(row.WindowTitle))
            sb.AppendLine($"Ventana principal: «{row.WindowTitle}».");

        if (!string.IsNullOrWhiteSpace(consoleText))
        {
            sb.AppendLine("Consola interna (búfer Win32):");
            foreach (var line in consoleText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(6))
                sb.AppendLine("  " + line);
        }
        else
            sb.AppendLine(consoleStatus);

        if (debug.Count > 0)
        {
            sb.AppendLine("Último OutputDebugString:");
            foreach (var line in debug.Take(4))
                sb.AppendLine($"  [{line.TimeText}] {line.Text}");
        }
        else
            sb.AppendLine("Sin OutputDebugString de este PID todavía (el programa tiene que llamar OutputDebugString / Debug.Write).");

        var files = Distinct(ops, "Archivo", 4);
        if (files.Count > 0)
        {
            sb.AppendLine("Está tocando archivos:");
            foreach (var f in files) sb.AppendLine("  " + f);
        }

        var net = Distinct(ops, "Red", 4);
        if (net.Count > 0)
        {
            sb.AppendLine("Actividad de red:");
            foreach (var n in net) sb.AppendLine("  " + n);
        }

        var io = TargetIoService.Instance.Current;
        if (io != null && io.Pid == row.Pid && io.InternetCount > 0)
            sb.AppendLine($"Ahora mismo tiene {io.InternetCount} conexión(es) a Internet.");

        var reg = Distinct(ops, "Registro", 3);
        if (reg.Count > 0)
        {
            sb.AppendLine("Consulta el registro:");
            foreach (var r in reg) sb.AppendLine("  " + r);
        }

        var visible = windows.Where(w => w.Visible).Select(w => w.Title).Distinct().Take(6).ToList();
        if (visible.Count > 0)
        {
            sb.AppendLine("Textos visibles en su UI:");
            foreach (var t in visible) sb.AppendLine("  «" + t + "»");
        }

        if (!string.IsNullOrWhiteSpace(managed))
        {
            sb.AppendLine("Razonamiento / pila .NET:");
            sb.AppendLine(managed);
        }
        else if (row.IsManaged)
            sb.AppendLine("Es un proceso .NET: se muestreará la pila administrada en esta vista.");

        if (files.Count == 0 && net.Count == 0 && debug.Count == 0 && string.IsNullOrWhiteSpace(consoleText))
            sb.AppendLine("Poca huella interna por ahora. Si ETW está activo y el proceso hace I/O o debug, aparecerá aquí.");

        return sb.ToString().Trim();
    }

    private static List<string> Distinct(List<SyscallEvent> ops, string kind, int take) =>
        ops.Where(o => o.Kind == kind && !string.IsNullOrWhiteSpace(o.Detail))
            .Select(o => $"{o.Operation}: {o.Detail}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
}
