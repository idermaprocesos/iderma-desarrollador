using System.Collections.ObjectModel;
using Consola.Models;
using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class ActivityPage : Page
{
    private readonly ObservableCollection<SyscallEvent> _events = [];
    private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int? _boundPid;
    private bool _syncingSwitch;

    public ActivityPage()
    {
        InitializeComponent();
        EventList.ItemsSource = _events;
        _flush.Tick += (_, _) => Flush();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        _syncingSwitch = true;
        CaptureSwitch.IsOn = EtwActivityService.Instance.IsRunning;
        _syncingSwitch = false;
        ApplyTargetFilter(clear: true);
        EnsureCapture();
        _flush.Start();
        Flush();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        _flush.Stop();
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionState.SelectedProcess) or null)
        {
            ApplyTargetFilter(clear: true);
            EnsureCapture();
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyTargetFilter(clear: true);

    private void ApplyTargetFilter(bool clear)
    {
        var row = SessionState.Current.SelectedProcess;
        var pid = FilterCheck.IsChecked == true ? row?.Pid : null;
        EtwActivityService.Instance.FilterPid = pid;

        TargetText.Text = row == null
            ? "Elige un programa arriba. La captura se filtra a ese PID en cuanto lo selecciones."
            : FilterCheck.IsChecked == true
                ? $"Eventos de {row.Name} ({row.Pid}). Archivo, registro, red, módulos, procesos, excepciones y syscalls."
                : $"Captura de todo el sistema. El objetivo sigue siendo {row.Name} ({row.Pid}).";

        if (clear && !Equals(_boundPid, pid))
        {
            _events.Clear();
            _boundPid = pid;
        }
    }

    private void EnsureCapture()
    {
        if (SessionState.Current.SelectedProcess == null) return;
        if (EtwActivityService.Instance.IsRunning) return;
        _syncingSwitch = true;
        CaptureSwitch.IsOn = true;
        _syncingSwitch = false;
        StartCapture();
    }

    private void Capture_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSwitch) return;
        if (CaptureSwitch.IsOn) StartCapture();
        else
        {
            EtwActivityService.Instance.Stop();
            EtwBar.IsOpen = false;
        }
    }

    private void StartCapture()
    {
        var etw = EtwActivityService.Instance;
        etw.FileIo = FileCheck.IsChecked == true;
        etw.Registry = RegCheck.IsChecked == true;
        etw.Network = NetCheck.IsChecked == true;
        etw.Image = ImgCheck.IsChecked == true;
        etw.Process = ProcCheck.IsChecked == true;
        etw.Exceptions = ExCheck.IsChecked == true;
        etw.Syscalls = SysCheck.IsChecked == true;
        ApplyTargetFilter(clear: false);
        if (etw.IsRunning) return;

        _events.Clear();
        if (!etw.Start())
        {
            EtwBar.Severity = InfoBarSeverity.Error;
            EtwBar.Title = "No se pudo iniciar ETW";
            EtwBar.Message = etw.LastError + "  Ejecuta Consola como administrador y cierra otras sesiones de kernel (WPR, Procmon).";
            EtwBar.IsOpen = true;
            _syncingSwitch = true;
            CaptureSwitch.IsOn = false;
            _syncingSwitch = false;
            return;
        }

        EtwBar.Severity = InfoBarSeverity.Success;
        EtwBar.Title = "Captura activa";
        EtwBar.Message = SessionState.Current.SelectedProcess is { } row
            ? $"Filtrando {row.Name} ({row.Pid}). Los syscalls generan un volumen muy alto."
            : "Eventos del kernel y de .NET Runtime. Los syscalls generan un volumen muy alto.";
        EtwBar.IsOpen = true;
    }

    private void Flush()
    {
        var batch = EtwActivityService.Instance.DequeueBatch(200);
        if (batch.Count == 0) return;
        foreach (var item in batch)
        {
            _events.Insert(0, item);
            if (_events.Count > 2500) _events.RemoveAt(_events.Count - 1);
        }
    }
}
