using System.Text;
using Consola.Models;
using Microsoft.Diagnostics.Runtime;

namespace Consola.Services;

internal sealed class ClrDebugService
{
    public static ClrDebugService Instance { get; } = new();

    public ClrInspectResult Inspect(int pid, CancellationToken token, IProgress<string>? progress = null)
    {
        var result = new ClrInspectResult();
        progress?.Report("Creando instantánea del proceso…");
        using var target = DataTarget.CreateSnapshotAndAttach(pid);
        if (target.ClrVersions.Length == 0)
        {
            result.Summary = "No se detectó un runtime de .NET en este proceso (puede ser nativo o de otra arquitectura).";
            result.Warnings.Add("ClrMD solo inspecciona procesos administrados de la misma arquitectura.");
            return result;
        }

        var info = target.ClrVersions[0];
        result.Runtime = info.Version.ToString();
        result.Dac = info.ModuleInfo.FileName ?? info.Version.ToString();
        using var runtime = info.CreateRuntime();

        result.AppDomainCount = runtime.AppDomains.Length;
        foreach (var domain in runtime.AppDomains)
            result.AppDomains.Add($"{domain.Name}  (id {domain.Id})");

        var modules = runtime.EnumerateModules().ToList();
        result.ModuleCount = modules.Count;
        foreach (var module in modules.Take(80))
        {
            token.ThrowIfCancellationRequested();
            result.Modules.Add(module.Name ?? module.AssemblyName ?? "módulo");
        }

        progress?.Report("Leyendo hilos administrados…");
        foreach (var thread in runtime.Threads)
        {
            token.ThrowIfCancellationRequested();
            var stack = new StringBuilder();
            try
            {
                foreach (var frame in thread.EnumerateStackTrace().Take(24))
                {
                    if (frame.Method != null)
                        stack.AppendLine($"  {frame.Method.Type?.Name}.{frame.Method.Name}");
                    else
                        stack.AppendLine($"  {frame}");
                }
            }
            catch
            {
                stack.AppendLine("  (pila no disponible)");
            }

            var hasEx = false;
            var exception = "";
            try
            {
                var current = thread.CurrentException;
                if (current != null)
                {
                    hasEx = true;
                    exception = $"{current.Type?.Name}: {current.Message}";
                }
            }
            catch { /* sin excepción actual */ }

            result.Threads.Add(new ManagedThreadInfo
            {
                Osnr = thread.ManagedThreadId,
                Tid = (int)thread.OSThreadId,
                Kind = thread.IsFinalizer ? "Finalizer" : thread.IsGc ? "GC" : thread.State.ToString(),
                IsAlive = thread.IsAlive,
                HasException = hasEx,
                Exception = exception,
                Stack = stack.ToString().TrimEnd()
            });
        }
        result.ThreadCount = result.Threads.Count;

        progress?.Report("Recorriendo el heap administrado…");
        var heap = runtime.Heap;
        result.CanWalkHeap = heap.CanWalkHeap;
        if (!heap.CanWalkHeap)
        {
            result.Warnings.Add("El heap no se puede recorrer ahora (posible GC en curso). Reintenta en unos segundos.");
        }
        else
        {
            var stats = new Dictionary<string, (int count, long size)>(StringComparer.Ordinal);
            var objects = 0;
            long total = 0;
            foreach (var obj in heap.EnumerateObjects(carefully: true))
            {
                token.ThrowIfCancellationRequested();
                if (!obj.IsValid) continue;
                objects++;
                total += (long)obj.Size;
                var type = obj.Type?.Name ?? "<desconocido>";
                stats.TryGetValue(type, out var acc);
                stats[type] = (acc.count + 1, acc.size + (long)obj.Size);
                if (objects >= 1_500_000) break;
            }

            result.ObjectCount = objects;
            result.HeapSize = total;
            result.TopTypes.AddRange(stats
                .Select(kv => new HeapTypeStat { Type = kv.Key, Count = kv.Value.count, Size = kv.Value.size })
                .OrderByDescending(t => t.Size)
                .Take(60));
        }

        var hung = result.Threads.Count(t => t.HasException);
        result.Summary = $"{result.Runtime} · {result.AppDomainCount} AppDomain · {result.ThreadCount} hilos · {result.ObjectCount:N0} objetos · {result.HeapSizeText}"
                         + (hung > 0 ? $" · {hung} hilos con excepción" : "");
        return result;
    }
}
