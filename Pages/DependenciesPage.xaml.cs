using System.Diagnostics;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class DependenciesPage : Page
{
    private TargetIoSnapshot? _snap;
    private int _boundPid = -1;

    public DependenciesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        TargetIoService.Instance.Updated += OnIo;
        MonitorEngine.Instance.Updated += OnTick;
        Bind();
        EnsureCapture();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        TargetIoService.Instance.Updated -= OnIo;
        MonitorEngine.Instance.Updated -= OnTick;
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null)
        {
            EnsureCapture();
            Bind();
        }
    }

    private void OnIo() => DispatcherQueue.TryEnqueue(Bind);

    private void OnTick()
    {
        var pid = SessionState.Current.SelectedProcess?.Pid;
        if (pid is int p && TargetIoService.Instance.Current?.Pid == p)
            BindConnectionsOnly();
    }

    private void EnsureCapture()
    {
        var row = SessionState.Current.SelectedProcess;
        if (row == null) return;
        if (TargetIoService.Instance.Current?.Pid != row.Pid)
            TargetIoService.Instance.BeginCapture(row);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var row = SessionState.Current.SelectedProcess;
        if (row != null) TargetIoService.Instance.BeginCapture(row);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _snap?.Folder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void Filter_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilters();

    private void Bind()
    {
        var row = SessionState.Current.SelectedProcess;
        EmptyBar.IsOpen = row == null;
        if (row == null)
        {
            TitleText.Text = "Dependencias y red";
            PathText.Text = "Elige un programa arriba para ver DLLs, carpeta madre y conexiones a Internet.";
            MetricsGrid.Visibility = Visibility.Collapsed;
            DetailPivot.Visibility = Visibility.Collapsed;
            _snap = null;
            _boundPid = -1;
            return;
        }

        MetricsGrid.Visibility = Visibility.Visible;
        DetailPivot.Visibility = Visibility.Visible;
        var snap = TargetIoService.Instance.Current;
        if (snap == null || snap.Pid != row.Pid)
        {
            TitleText.Text = $"{row.Name}  ({row.Pid})";
            PathText.Text = string.IsNullOrWhiteSpace(row.Path) ? "Leyendo dependencias y carpeta…" : row.Path;
            DepsValue.Text = "…";
            FolderValue.Text = "…";
            NetValue.Text = "…";
            SockValue.Text = "…";
            DepsDetail.Text = "Analizando imports PE, delay-load y módulos cargados.";
            FolderDetail.Text = Path.GetDirectoryName(row.Path) ?? "";
            NetDetail.Text = "Consultando tabla TCP/UDP.";
            SockDetail.Text = "";
            return;
        }

        _snap = snap;
        _boundPid = snap.Pid;
        TitleText.Text = $"{row.Name}  ({row.Pid})";
        PathText.Text = string.IsNullOrWhiteSpace(snap.Folder) ? snap.ExePath : snap.Folder;
        DepsValue.Text = snap.Dependencies.Count.ToString("N0");
        DepsDetail.Text = $"{snap.ImportCount} en PE/.NET  ·  {snap.LoadedCount} cargadas ahora";
        FolderValue.Text = snap.FolderTotal.ToString("N0");
        FolderDetail.Text = snap.FolderTotal > snap.FolderFileCount
            ? $"{snap.Folder}  (mostrando {snap.FolderFileCount})"
            : snap.Folder;
        NetValue.Text = snap.InternetCount > 0 ? "Sí" : "No";
        NetDetail.Text = snap.InternetText;
        SockValue.Text = snap.Connections.Count.ToString("N0");
        SockDetail.Text = $"{snap.LanCount} LAN  ·  {snap.ListenCount} en escucha";
        ApplyFilters();
    }

    private void BindConnectionsOnly()
    {
        var snap = TargetIoService.Instance.Current;
        if (snap == null || snap.Pid != _boundPid) return;
        _snap = snap;
        NetValue.Text = snap.InternetCount > 0 ? "Sí" : "No";
        NetDetail.Text = snap.InternetText;
        SockValue.Text = snap.Connections.Count.ToString("N0");
        SockDetail.Text = $"{snap.LanCount} LAN  ·  {snap.ListenCount} en escucha";
        ApplyNetFilter();
    }

    private void ApplyFilters()
    {
        if (_snap == null) return;
        var dq = DepSearch.Text?.Trim() ?? "";
        IEnumerable<DependencyRow> deps = _snap.Dependencies;
        if (!string.IsNullOrEmpty(dq))
        {
            deps = deps.Where(d =>
                d.Name.Contains(dq, StringComparison.OrdinalIgnoreCase) ||
                d.Kind.Contains(dq, StringComparison.OrdinalIgnoreCase) ||
                d.Status.Contains(dq, StringComparison.OrdinalIgnoreCase) ||
                d.Path.Contains(dq, StringComparison.OrdinalIgnoreCase));
        }
        DepList.ItemsSource = deps.ToList();

        var fq = FileSearch.Text?.Trim() ?? "";
        IEnumerable<FolderFileRow> files = _snap.FolderFiles;
        if (!string.IsNullOrEmpty(fq))
        {
            files = files.Where(f =>
                f.Name.Contains(fq, StringComparison.OrdinalIgnoreCase) ||
                f.Role.Contains(fq, StringComparison.OrdinalIgnoreCase) ||
                f.Kind.Contains(fq, StringComparison.OrdinalIgnoreCase));
        }
        FileList.ItemsSource = files.ToList();
        ApplyNetFilter();
    }

    private void ApplyNetFilter()
    {
        if (_snap == null) return;
        var nq = NetSearch.Text?.Trim() ?? "";
        IEnumerable<NetConnectionRow> nets = _snap.Connections;
        if (!string.IsNullOrEmpty(nq))
        {
            nets = nets.Where(c =>
                c.Remote.Contains(nq, StringComparison.OrdinalIgnoreCase) ||
                c.Local.Contains(nq, StringComparison.OrdinalIgnoreCase) ||
                c.Scope.Contains(nq, StringComparison.OrdinalIgnoreCase) ||
                c.Hint.Contains(nq, StringComparison.OrdinalIgnoreCase) ||
                c.Protocol.Contains(nq, StringComparison.OrdinalIgnoreCase));
        }
        NetList.ItemsSource = nets.ToList();
    }
}
