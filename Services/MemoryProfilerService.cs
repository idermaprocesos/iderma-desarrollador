using System.Collections.Concurrent;
using Consola.Helpers;
using Consola.Models;

namespace Consola.Services;

internal sealed class MemoryProfilerService
{
    public static MemoryProfilerService Instance { get; } = new();

    private readonly ConcurrentDictionary<int, List<MemorySample>> _history = new();
    private const int MaxSamples = 90;

    public IReadOnlyList<MemorySample> GetHistory(int pid) =>
        _history.TryGetValue(pid, out var list) ? list.ToArray() : [];

    public MemorySample Sample(ProcessRow row)
    {
        var sample = new MemorySample
        {
            Timestamp = DateTimeOffset.Now,
            PrivateBytes = row.PrivateBytes,
            WorkingSet = row.WorkingSet,
            Handles = row.HandleCount,
            Threads = row.ThreadCount,
            Gdi = row.GdiCount,
            UserObjects = row.UserObjectCount,
            PageFaults = row.PageFaults
        };

        var list = _history.GetOrAdd(row.Pid, _ => []);
        lock (list)
        {
            list.Add(sample);
            if (list.Count > MaxSamples) list.RemoveRange(0, list.Count - MaxSamples);
        }

        return sample;
    }

    public void SampleMany(IEnumerable<ProcessRow> rows)
    {
        foreach (var row in rows) Sample(row);
    }

    public void Prune(HashSet<int> alive)
    {
        foreach (var pid in _history.Keys)
        {
            if (!alive.Contains(pid)) _history.TryRemove(pid, out _);
        }
    }

    public LeakVerdict Analyze(int pid)
    {
        if (!_history.TryGetValue(pid, out var list))
            return new LeakVerdict { Summary = "Sin muestras todavía." };

        List<MemorySample> snapshot;
        lock (list) snapshot = list.ToList();
        return LeakDetector.Analyze(snapshot);
    }

    public List<(ProcessRow process, LeakVerdict leak)> ScanLeaks(IEnumerable<ProcessRow> rows)
    {
        var alerts = new List<(ProcessRow, LeakVerdict)>();
        foreach (var row in rows)
        {
            var verdict = Analyze(row.Pid);
            row.Leak = verdict;
            if (verdict.IsLeak) alerts.Add((row, verdict));
        }
        return alerts.OrderByDescending(a => a.Item2.Score).ToList();
    }
}

internal static class LeakDetector
{
    public static LeakVerdict Analyze(IReadOnlyList<MemorySample> samples)
    {
        if (samples.Count < 8)
        {
            return new LeakVerdict
            {
                Summary = $"Recolectando muestras ({samples.Count}/8)…",
                Details = "Se necesitan al menos 8 muestras para estimar una fuga."
            };
        }

        var first = samples[0];
        var last = samples[^1];
        var elapsed = Math.Max(1, (last.Timestamp - first.Timestamp).TotalSeconds);

        var memSlope = Slope(samples.Select(s => (double)s.PrivateBytes).ToArray());
        var handleSlope = Slope(samples.Select(s => (double)s.Handles).ToArray());
        var threadSlope = Slope(samples.Select(s => (double)s.Threads).ToArray());
        var gdiSlope = Slope(samples.Select(s => (double)s.Gdi).ToArray());

        var memGrowth = last.PrivateBytes - first.PrivateBytes;
        var handleGrowth = last.Handles - first.Handles;
        var threadGrowth = last.Threads - first.Threads;
        var gdiGrowth = last.Gdi - first.Gdi;

        var reasons = new List<string>();
        var score = 0d;
        var kind = "estable";

        if (memGrowth > 12 * 1024 * 1024 && memSlope > 0 && last.PrivateBytes > first.PrivateBytes * 1.08)
        {
            score += Math.Min(70, memGrowth / (1024.0 * 1024.0));
            kind = "memoria";
            reasons.Add($"Memoria privada +{FormatUtil.Bytes(memGrowth)} ({FormatUtil.Rate(memGrowth / elapsed)}).");
        }

        if (handleGrowth > 80 && handleSlope > 0.3)
        {
            score += Math.Min(25, handleGrowth / 8.0);
            if (kind == "estable") kind = "handles";
            reasons.Add($"Handles +{handleGrowth} ({handleGrowth / elapsed:0.1}/s). Posible fuga de identificadores.");
        }

        if (threadGrowth > 8 && threadSlope > 0.05)
        {
            score += 15;
            if (kind == "estable") kind = "hilos";
            reasons.Add($"Hilos +{threadGrowth}. Posible fuga de subprocesos.");
        }

        if (last.Gdi > 5000 || (gdiGrowth > 150 && gdiSlope > 0.2))
        {
            score += 20;
            if (kind == "estable") kind = "GDI";
            reasons.Add($"Objetos GDI {last.Gdi} (Δ {gdiGrowth}). Riesgo de agotar el límite de 10.000.");
        }

        if (last.UserObjects > 5000)
        {
            score += 15;
            reasons.Add($"USER objects {last.UserObjects}. Riesgo de agotar recursos de ventana.");
        }

        var isLeak = score >= 18;
        return new LeakVerdict
        {
            IsLeak = isLeak,
            Score = Math.Round(score, 1),
            Kind = kind,
            Summary = isLeak ? $"Posible fuga de {kind} (índice {score:0})" : "Sin fuga evidente",
            Details = reasons.Count == 0
                ? $"Memoria {FormatUtil.Bytes(last.PrivateBytes)}, handles {last.Handles}, hilos {last.Threads}. Tendencia estable."
                : string.Join(" ", reasons)
        };
    }

    private static double Slope(double[] y)
    {
        var n = y.Length;
        if (n < 2) return 0;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += y[i];
            sumXY += i * y[i];
            sumXX += i * i;
        }
        var den = n * sumXX - sumX * sumX;
        return den == 0 ? 0 : (n * sumXY - sumX * sumY) / den;
    }
}
