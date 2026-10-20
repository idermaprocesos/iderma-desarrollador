using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class InternalsPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private int _boundPid = -1;
    private int _ticks;
    private string _managed = "";
    private bool _busy;

    public InternalsPage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => _ = RefreshAsync(light: true);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        DebugOutputService.Instance.Start();
        EnsureEtw();
        BindEmpty();
        _timer.Start();
        _ = RefreshAsync(light: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        _timer.Stop();
        DebugOutputService.Instance.Stop();
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SessionState.SelectedProcess) or null)) return;
        _boundPid = -1;
        _managed = "";
        _ticks = 0;
        EnsureEtw();
        _ = RefreshAsync(light: false);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync(light: false);

    private async void Managed_Click(object sender, RoutedEventArgs e)
    {
        var row = SessionState.Current.SelectedProcess;
        if (row == null) return;
        ManagedBox.Text = "Muestreando runtime…";
        var pid = row.Pid;
        var text = await Task.Run(() => InnerMindService.TrySampleManaged(pid));
        _managed = string.IsNullOrWhiteSpace(text) ? "Sin pila administrada útil (nativo, otra arquitectura, o hilos en espera)." : text;
        ManagedBox.Text = _managed;
        _ = RefreshAsync(light: true);
    }

    private void BindEmpty()
    {
        var row = SessionState.Current.SelectedProcess;
        EmptyBar.IsOpen = row == null;
        MetricsGrid.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        DetailPivot.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null)
        {
            TitleText.Text = "Consola interna";
            PathText.Text = "Elige un programa para leer su consola, debug interno y lo que está haciendo.";
        }
    }

    private void EnsureEtw()
    {
        if (SessionState.Current.SelectedProcess == null) return;
        var etw = EtwActivityService.Instance;
        etw.FilterPid = SessionState.Current.SelectedProcess.Pid;
        if (etw.IsRunning) return;
        etw.FileIo = true;
        etw.Registry = true;
        etw.Network = true;
        etw.Image = true;
        etw.Process = true;
        etw.Exceptions = true;
        if (!etw.Start())
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = "ETW no disponible";
            StatusBar.Message = etw.LastError + "  Sigue pudiendo leer consola y OutputDebugString. Como administrador se ve más I/O.";
            StatusBar.IsOpen = true;
        }
    }

    private async Task RefreshAsync(bool light)
    {
        var row = SessionState.Current.SelectedProcess;
        BindEmpty();
        if (row == null || _busy) return;
        _busy = true;
        _ticks++;
        try
        {
            var pid = row.Pid;
            InnerSnapshot snap = await Task.Run(() => InnerMindService.Capture(row, _managed));
            if (SessionState.Current.SelectedProcess?.Pid != pid) return;
            ApplySnap(row, snap);

            var needManaged = row.IsManaged && (pid != _boundPid || (!light && _ticks % 8 == 0) || string.IsNullOrEmpty(_managed));
            _boundPid = pid;
            if (!needManaged) return;

            var thought = await Task.Run(() => InnerMindService.TrySampleManaged(pid));
            if (SessionState.Current.SelectedProcess?.Pid != pid) return;
            _managed = string.IsNullOrWhiteSpace(thought)
                ? "Sin pila administrada útil (nativo, otra arquitectura, o hilos en espera)."
                : thought;
            snap = await Task.Run(() => InnerMindService.Capture(row, _managed));
            if (SessionState.Current.SelectedProcess?.Pid != pid) return;
            ApplySnap(row, snap);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ApplySnap(ProcessRow row, InnerSnapshot snap)
    {
        TitleText.Text = $"{row.Name}  ({row.Pid})";
        PathText.Text = string.IsNullOrWhiteSpace(row.Path) ? row.WindowTitle : row.Path;
        ConsoleStatus.Text = string.IsNullOrWhiteSpace(snap.ConsoleText) ? "Sin texto" : "Leída";
        DebugStatus.Text = DebugOutputService.Instance.LastError is { Length: > 0 }
            ? "Canal ocupado"
            : $"{snap.DebugLines.Count} líneas";
        HintStatus.Text = $"{snap.RecentOps.Count} eventos  ·  {snap.Windows.Count} textos UI";
        NarrativeText.Text = snap.Narrative;
        ConsoleBox.Text = string.IsNullOrWhiteSpace(snap.ConsoleText) ? snap.ConsoleStatus : snap.ConsoleText;
        DebugList.ItemsSource = snap.DebugLines;
        WindowList.ItemsSource = snap.Windows;
        if (!string.IsNullOrWhiteSpace(_managed))
            ManagedBox.Text = _managed;
        else if (row.IsManaged)
            ManagedBox.Text = "Pulsa «Pila .NET» o espera el primer muestreo.";
        else
            ManagedBox.Text = "Este proceso no parece .NET. La pila administrada no aplica; usa consola, debug y ETW.";

        if (DebugOutputService.Instance.LastError is { Length: > 0 } dbgErr)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = "Debug interno";
            StatusBar.Message = dbgErr;
            StatusBar.IsOpen = true;
        }
    }
}
