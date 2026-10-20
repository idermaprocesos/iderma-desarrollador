using Consola.Helpers;
using Consola.Models;
using Microsoft.UI.Dispatching;

namespace Consola.Services;

internal sealed class MonitorEngine
{
    public static MonitorEngine Instance { get; } = new();

    private const int IntervalMs = 2500;
    private CancellationTokenSource? _cts;
    private int _uiQueued;
    public IReadOnlyList<ProcessRow> Processes { get; private set; } = [];
    public SystemSnapshot Snapshot { get; private set; } = new();
    public List<(ProcessRow process, LeakVerdict leak)> Leaks { get; private set; } = [];
    public bool FreezeUiLists { get; set; }

    public event Action? Updated;

    public void Start(DispatcherQueue dispatcher)
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var started = Environment.TickCount64;
                try
                {
                    var captured = ProcessCatalogService.Instance.Capture();
                    var selectedPid = SessionState.Current.SelectedProcess?.Pid;
                    var tracked = PickTracked(captured, selectedPid);
                    MemoryProfilerService.Instance.SampleMany(tracked);
                    MemoryProfilerService.Instance.Prune(captured.Select(r => r.Pid).ToHashSet());
                    var leaks = MemoryProfilerService.Instance.ScanLeaks(tracked);
                    var snap = SystemMetricsService.Instance.Capture(captured);

                    if (Interlocked.CompareExchange(ref _uiQueued, 1, 0) == 0)
                    {
                        var posted = dispatcher.TryEnqueue(() =>
                        {
                            try { Publish(captured, snap, leaks); }
                            finally { Interlocked.Exchange(ref _uiQueued, 0); }
                        });
                        if (!posted) Interlocked.Exchange(ref _uiQueued, 0);
                    }
                }
                catch (Exception ex)
                {
                    dispatcher.TryEnqueue(() => SessionState.Current.StatusDetail = ex.Message);
                }

                var elapsed = Environment.TickCount64 - started;
                var wait = IntervalMs - (int)Math.Min(elapsed, IntervalMs);
                if (wait < 400) wait = 400;
                try { await Task.Delay(wait, token); }
                catch (TaskCanceledException) { break; }
            }
        }, token);
    }

    private void Publish(List<ProcessRow> captured, SystemSnapshot snap, List<(ProcessRow process, LeakVerdict leak)> leaks)
    {
        var rows = ProcessCatalogService.Instance.MergeOnUi(captured);
        Processes = rows;
        Snapshot = snap;
        Leaks = leaks;
        if (SessionState.Current.SelectedProcess is { } selected)
        {
            var live = rows.FirstOrDefault(r => r.Pid == selected.Pid);
            if (live != null && !ReferenceEquals(live, selected))
                SessionState.Current.SelectedProcess = live;
            else
                SessionState.Current.RefreshTargetCaption();
            TargetIoService.Instance.RefreshConnections(selected.Pid);
        }
        SessionState.Current.Status = $"{rows.Count} procesos · CPU {FormatUtil.Percent(snap.CpuPercent)} · RAM {snap.MemoryLoad} %";
        Updated?.Invoke();
    }

    private static List<ProcessRow> PickTracked(List<ProcessRow> rows, int? selectedPid)
    {
        const int extra = 16;
        var tracked = new List<ProcessRow>(extra + 1);
        ProcessRow? selected = null;
        if (selectedPid is int pid)
            selected = rows.Find(r => r.Pid == pid);

        foreach (var row in rows.OrderByDescending(r => r.PrivateBytes).Take(extra))
        {
            if (selected != null && row.Pid == selected.Pid) continue;
            tracked.Add(row);
        }

        if (selected != null) tracked.Add(selected);
        return tracked;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }
}
