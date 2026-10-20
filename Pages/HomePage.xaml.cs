using Consola.Helpers;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        AdminBar.IsOpen = !PrivilegeHelper.IsElevated;
        SessionState.Current.PropertyChanged += OnSession;
        MonitorEngine.Instance.Updated += OnUpdated;
        TargetIoService.Instance.Updated += OnIo;
        OnUpdated();
        ShowSelected();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        MonitorEngine.Instance.Updated -= OnUpdated;
        TargetIoService.Instance.Updated -= OnIo;
    }

    private void OnIo() => DispatcherQueue.TryEnqueue(ShowSelected);

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) ShowSelected();
    }

    private void OnUpdated()
    {
        var snap = MonitorEngine.Instance.Snapshot;
        CpuValue.Text = FormatUtil.Percent(snap.CpuPercent);
        CpuBar.Value = snap.CpuPercent;
        RamValue.Text = $"{snap.UsedMemoryText} / {snap.TotalMemoryText}";
        RamBar.Value = snap.MemoryLoad;
        ProcValue.Text = $"{snap.ProcessCount} / {snap.ThreadCount}";
        HandleValue.Text = $"~{snap.HandleEstimate:N0} handles";
        UptimeValue.Text = FormatUtil.Duration(snap.Uptime);
        OsValue.Text = $"{snap.Machine} · {snap.Os}";

        if (!MonitorEngine.Instance.FreezeUiLists)
        {
            var selectedPid = SessionState.Current.SelectedProcess?.Pid;
            IEnumerable<(ProcessRow process, LeakVerdict leak)> leaks = MonitorEngine.Instance.Leaks;
            if (selectedPid is int pid)
                leaks = leaks.Where(l => l.process.Pid == pid);

            LeakList.ItemsSource = leaks
                .Take(12)
                .Select(l => new DashboardAlert
                {
                    Title = $"{l.process.Name} ({l.process.Pid})",
                    Message = l.leak.Details,
                    Severity = "Error",
                    Pid = l.process.Pid
                })
                .ToList();

            if (TopCpuList.ItemsSource is not IReadOnlyList<ProcessRow> { Count: > 0 })
            {
                TopCpuList.ItemsSource = MonitorEngine.Instance.Processes
                    .OrderByDescending(p => p.CpuPercent)
                    .Take(12)
                    .ToList();
            }
        }

        ShowSelected();
    }

    private void ShowSelected()
    {
        var row = SessionState.Current.SelectedProcess;
        if (row != null)
            row = MonitorEngine.Instance.Processes.FirstOrDefault(p => p.Pid == row.Pid) ?? row;

        var has = row != null;
        PickHint.IsOpen = !has;
        SelectedPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (row == null) return;

        SelectedTitle.Text = $"{row.Name}  ({row.Pid})";
        SelectedPath.Text = string.IsNullOrWhiteSpace(row.Path) ? (row.WindowTitle ?? "") : row.Path;
        SelCpu.Text = row.CpuText;
        SelMem.Text = row.MemoryText;
        SelThreads.Text = $"{row.ThreadCount} / {row.HandleCount}";
        SelStatus.Text = row.StatusText;
        var io = TargetIoService.Instance.Current;
        if (io != null && io.Pid == row.Pid)
        {
            SelNet.Text = io.InternetCount > 0 ? "Conectado" : "Sin Internet";
            SelDeps.Text = $"{io.Dependencies.Count} deps · {io.FolderTotal} archivos";
        }
        else
        {
            SelNet.Text = "Leyendo…";
            SelDeps.Text = "Leyendo…";
        }
        SelectedDetails.Text =
            $"Usuario: {row.User}    Arquitectura: {row.Architecture}    Runtime: {row.ManagedText}    Integridad: {row.Integrity}\n" +
            $"Inicio: {row.StartText}    Prioridad: {row.Priority}    GDI/USER: {row.GdiCount}/{row.UserObjectCount}\n" +
            $"I/O lectura: {FormatUtil.Bytes((long)row.IoReadBytes)} ({FormatUtil.Rate(row.IoReadRate)})    " +
            $"escritura: {FormatUtil.Bytes((long)row.IoWriteBytes)} ({FormatUtil.Rate(row.IoWriteRate)})\n" +
            $"Empresa: {row.Company}    {row.Description}\n" +
            $"Comando: {row.CommandLine}\n" +
            (io != null && io.Pid == row.Pid ? $"{io.InternetText}    Carpeta: {io.Folder}\n" : "") +
            (row.Leak?.IsLeak == true ? row.Leak.Details : "Sin fuga evidente en las muestras actuales.");
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        PrivilegeHelper.RestartAsAdministrator();
        Application.Current.Exit();
    }

    private void GoInspect_Click(object sender, RoutedEventArgs e) => App.Instance.Window?.GoTo("inspect");
    private void GoDeps_Click(object sender, RoutedEventArgs e) => App.Instance.Window?.GoTo("deps");
    private void GoInternals_Click(object sender, RoutedEventArgs e) => App.Instance.Window?.GoTo("internals");

    private void Leak_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DashboardAlert alert && alert.Pid is { } pid)
            SelectPid(pid);
    }

    private void Process_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProcessRow row)
            SessionState.Current.SelectedProcess = row;
    }

    private void SelectPid(int pid)
    {
        var row = MonitorEngine.Instance.Processes.FirstOrDefault(p => p.Pid == pid);
        if (row != null) SessionState.Current.SelectedProcess = row;
    }
}
