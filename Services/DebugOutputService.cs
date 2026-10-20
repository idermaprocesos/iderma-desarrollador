using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Text;
using Consola.Models;

namespace Consola.Services;

internal sealed class DebugOutputService : IDisposable
{
    public static DebugOutputService Instance { get; } = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly ConcurrentQueue<DebugLine> _lines = new();
    public string? LastError { get; private set; }
    public bool IsRunning => _loop != null;
    public event Action<DebugLine>? LineReceived;

    public bool Start()
    {
        if (_loop != null) return true;
        LastError = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Factory.StartNew(() => Pump(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return true;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        _loop = null;
    }

    public List<DebugLine> Snapshot(int? pid, int max = 250)
    {
        var all = _lines.ToArray();
        IEnumerable<DebugLine> rows = all;
        if (pid is { } p and not 0)
            rows = rows.Where(l => l.Pid == p);
        return rows.TakeLast(max).Reverse().ToList();
    }

    public void Dispose() => Stop();

    private void Pump(CancellationToken token)
    {
        Mutex? mutex = null;
        EventWaitHandle? ready = null;
        EventWaitHandle? data = null;
        MemoryMappedFile? map = null;
        try
        {
            mutex = new Mutex(false, "DBWinMutex");
            try
            {
                // No retener el mutex: OutputDebugString espera hasta 10 s si alguien lo tiene,
                // y eso deja colgados a los demás programas del equipo.
                if (!mutex.WaitOne(0))
                {
                    LastError = "Hay otro visor de OutputDebugString (p. ej. DebugView). Ciérralo para que Consola reciba el canal.";
                    return;
                }
                mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }

            ready = new EventWaitHandle(true, EventResetMode.AutoReset, "DBWIN_BUFFER_READY");
            data = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY");
            map = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096, MemoryMappedFileAccess.ReadWrite);

            using var view = map.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read);
            var textBytes = new byte[4092];

            while (!token.IsCancellationRequested)
            {
                if (!data.WaitOne(250)) continue;
                var pid = view.ReadInt32(0);
                view.ReadArray(4, textBytes, 0, textBytes.Length);
                var end = Array.IndexOf(textBytes, (byte)0);
                if (end < 0) end = textBytes.Length;
                var text = Encoding.UTF8.GetString(textBytes, 0, end).TrimEnd('\r', '\n', '\0');
                ready.Set();
                if (pid <= 0 || string.IsNullOrWhiteSpace(text)) continue;

                var line = new DebugLine
                {
                    Timestamp = DateTimeOffset.Now,
                    Pid = pid,
                    Text = text
                };
                _lines.Enqueue(line);
                while (_lines.Count > 2500) _lines.TryDequeue(out _);
                LineReceived?.Invoke(line);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            mutex?.Dispose();
            ready?.Dispose();
            data?.Dispose();
            map?.Dispose();
        }
    }
}
