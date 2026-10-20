using Consola.Helpers;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Consola.Pages;

public sealed partial class InspectPage : Page
{
    public InspectPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSessionChanged;
        MonitorEngine.Instance.Updated += Load;
        Load();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSessionChanged;
        MonitorEngine.Instance.Updated -= Load;
    }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null) Load();
    }

    private int _loadedPid = -1;

    private void Load()
    {
        var row = SessionState.Current.SelectedProcess;
        EmptyBar.IsOpen = row == null;
        DetailPivot.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null)
        {
            TitleText.Text = "Inspección";
            PathText.Text = "Elige un programa en Programas activos. Resumen, hilos y módulos aparecen aquí.";
            SummaryGrid.ItemsSource = null;
            ThreadList.ItemsSource = null;
            ModuleList.ItemsSource = null;
            ChildList.ItemsSource = null;
            _loadedPid = -1;
            return;
        }

        var live = MonitorEngine.Instance.Processes.FirstOrDefault(p => p.Pid == row.Pid) ?? row;
        TitleText.Text = $"{live.Name}  ({live.Pid})  ·  {live.CpuText}  ·  {live.MemoryText}";
        PathText.Text = string.IsNullOrWhiteSpace(live.Path) ? live.CommandLine : live.Path;

        var cards = new List<string>
        {
            $"CPU  {live.CpuText}",
            $"Memoria privada  {live.MemoryText}",
            $"Working set  {live.WorkingSetText}",
            $"Virtual  {FormatUtil.Bytes(live.VirtualMemory)}",
            $"Hilos  {live.ThreadCount}",
            $"Handles  {live.HandleCount}",
            $"GDI / USER  {live.GdiCount} / {live.UserObjectCount}",
            $"Arquitectura  {live.Architecture}",
            $"Integridad  {live.Integrity}",
            $"Usuario  {live.User}",
            $"Inicio  {live.StartText}",
            $"Prioridad  {live.Priority}",
            $"Sesión  {live.SessionId}",
            $"I/O lectura  {FormatUtil.Bytes((long)live.IoReadBytes)}  ({FormatUtil.Rate(live.IoReadRate)})",
            $"I/O escritura  {FormatUtil.Bytes((long)live.IoWriteBytes)}  ({FormatUtil.Rate(live.IoWriteRate)})",
            $"Línea de comandos  {live.CommandLine}",
            $"Empresa  {live.Company}",
            $"Descripción  {live.Description}",
            live.Leak?.IsLeak == true ? live.Leak.Details : "Sin fuga detectada en las muestras actuales."
        };

        if (TargetIoService.Instance.Current is { } io && io.Pid == live.Pid)
        {
            cards.Add($"Internet  {io.InternetText}");
            cards.Add($"Dependencias  {io.Dependencies.Count}  ·  {io.LoadedCount} módulos cargados");
            cards.Add($"Carpeta  {io.FolderTotal} archivos  ·  {io.Folder}");
        }

        SummaryGrid.ItemsSource = cards;

        var pidChanged = _loadedPid != live.Pid;
        if (!pidChanged) return;

        _loadedPid = live.Pid;

        try { ThreadList.ItemsSource = ProcessNative.SnapshotThreads(live.Pid); }
        catch { ThreadList.ItemsSource = Array.Empty<object>(); }

        try
        {
            var modules = ProcessNative.SnapshotModules(live.Pid);
            ModuleList.ItemsSource = modules;
            if (modules.Any(m => m.IsClr))
            {
                live.IsManaged = true;
                live.Runtime = ".NET";
            }
        }
        catch { ModuleList.ItemsSource = Array.Empty<object>(); }

        ChildList.ItemsSource = MonitorEngine.Instance.Processes.Where(p => p.ParentPid == live.Pid).ToList();
    }

    private async void Dump_Click(object sender, RoutedEventArgs e)
    {
        var row = SessionState.Current.SelectedProcess;
        if (row == null) return;
        try
        {
            var path = MiniDumpService.WriteDump(row.Pid, fullMemory: false);
            await App.ShowErrorAsync("Volcado creado", path);
        }
        catch (Exception ex)
        {
            await App.ShowErrorAsync("No se pudo crear el volcado", ex.Message);
        }
    }

    private void OpenDumps_Click(object sender, RoutedEventArgs e) => MiniDumpService.OpenFolder();

    private void CopyCmd_Click(object sender, RoutedEventArgs e)
    {
        var cmd = SessionState.Current.SelectedProcess?.CommandLine;
        if (string.IsNullOrWhiteSpace(cmd)) return;
        var data = new DataPackage();
        data.SetText(cmd);
        Clipboard.SetContent(data);
    }
}
