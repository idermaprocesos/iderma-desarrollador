using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using Consola.Models;

namespace Consola.Services;

internal sealed class FaultMonitorService : IDisposable
{
    public static FaultMonitorService Instance { get; } = new();

    private EventLogWatcher? _watcher;
    private static readonly Regex PidRegex = new(@"pid[:\s]+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AppRegex = new(@"nombre de la aplicación:\s*([^\r\n]+)|Faulting application name:\s*([^\r\n,]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public event Action<FaultEvent>? FaultArrived;

    public List<FaultEvent> LoadRecent(int hours = 24, int max = 400)
    {
        var list = new List<FaultEvent>();
        foreach (var log in new[] { "Application", "System" })
        {
            try
            {
                var ms = hours * 3600 * 1000L;
                var query = new EventLogQuery(log, PathType.LogName,
                    $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
                using var reader = new EventLogReader(query);
                for (var rec = reader.ReadEvent(); rec != null && list.Count < max; rec = reader.ReadEvent())
                {
                    using (rec)
                    {
                        list.Add(FromRecord(rec, log));
                    }
                }
            }
            catch
            {
                // registro no accesible
            }
        }

        return list.OrderByDescending(f => f.Timestamp).Take(max).ToList();
    }

    public bool StartLive()
    {
        StopLive();
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName, "*[System[(Level=1 or Level=2)]]");
            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += (_, args) =>
            {
                if (args.EventRecord == null) return;
                try
                {
                    var fault = FromRecord(args.EventRecord, "Application");
                    FaultArrived?.Invoke(fault);
                }
                catch { /* ignore parse errors */ }
            };
            _watcher.Enabled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StopLive()
    {
        if (_watcher == null) return;
        try { _watcher.Enabled = false; } catch { }
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose() => StopLive();

    private static FaultEvent FromRecord(EventRecord rec, string log)
    {
        string message;
        try { message = rec.FormatDescription() ?? rec.TaskDisplayName ?? ""; }
        catch { message = rec.TaskDisplayName ?? ""; }

        int? pid = null;
        var pidMatch = PidRegex.Match(message);
        if (pidMatch.Success && int.TryParse(pidMatch.Groups[1].Value, out var parsed)) pid = parsed;

        var process = "";
        var appMatch = AppRegex.Match(message);
        if (appMatch.Success)
            process = (appMatch.Groups[1].Success ? appMatch.Groups[1].Value : appMatch.Groups[2].Value).Trim();

        var level = rec.LevelDisplayName ?? rec.Level switch
        {
            1 => "Crítico",
            2 => "Error",
            3 => "Advertencia",
            _ => "Info"
        };

        return new FaultEvent
        {
            Timestamp = rec.TimeCreated ?? DateTime.Now,
            Level = level,
            Source = rec.ProviderName ?? log,
            EventId = rec.Id,
            Pid = pid,
            Process = process,
            Message = Truncate(message, 600)
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
