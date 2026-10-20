using Consola.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class DebugPage : Page
{
    private CancellationTokenSource? _cts;
    private int _inspectedPid = -1;
    private bool _busy;

    public DebugPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged += OnSession;
        UpdateTarget();
        _ = InspectTargetAsync(auto: true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SessionState.Current.PropertyChanged -= OnSession;
        _cts?.Cancel();
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SessionState.SelectedProcess) or null)) return;
        UpdateTarget();
        _ = InspectTargetAsync(auto: true);
    }

    private void UpdateTarget()
    {
        var row = SessionState.Current.SelectedProcess;
        TargetText.Text = row == null
            ? "Elige un programa arriba. Si es .NET, la instantánea (ClrMD) se toma al seleccionarlo."
            : $"Objetivo: {row.Name} ({row.Pid})  ·  {row.Architecture}  ·  {row.ManagedText}";
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await InspectTargetAsync(auto: false);

    private async Task InspectTargetAsync(bool auto)
    {
        var row = SessionState.Current.SelectedProcess;
        if (row == null)
        {
            _inspectedPid = -1;
            ResultBar.Severity = InfoBarSeverity.Informational;
            ResultBar.Title = "Sin proceso";
            ResultBar.Message = "Elige un programa en Programas activos. Todas las secciones usan ese mismo objetivo.";
            ResultBar.IsOpen = true;
            ThreadList.ItemsSource = null;
            HeapList.ItemsSource = null;
            DomainList.ItemsSource = null;
            ModuleList.ItemsSource = null;
            return;
        }

        if (auto && _inspectedPid == row.Pid) return;
        if (_busy && auto) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _busy = true;
        _inspectedPid = row.Pid;
        InspectButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Capturando runtime…";
        var pid = row.Pid;
        var progress = new Progress<string>(s => StatusText.Text = s);

        try
        {
            var result = await Task.Run(() => ClrDebugService.Instance.Inspect(pid, _cts.Token, progress), _cts.Token);
            if (_inspectedPid != pid) return;
            ThreadList.ItemsSource = result.Threads;
            HeapList.ItemsSource = result.TopTypes;
            DomainList.ItemsSource = result.AppDomains;
            ModuleList.ItemsSource = result.Modules;
            ResultBar.Title = result.Runtime.Length == 0 ? "Sin CLR" : result.Runtime;
            ResultBar.Message = result.Summary + (result.Warnings.Count == 0 ? "" : "  " + string.Join(" ", result.Warnings));
            ResultBar.Severity = result.Runtime.Length == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            ResultBar.IsOpen = true;
            StatusText.Text = result.ObjectCount > 0 ? $"{result.ObjectCount:N0} objetos" : "Listo";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelado";
        }
        catch (Exception ex)
        {
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Title = "No se pudo adjuntar";
            ResultBar.Message = ex.Message + "  Prueba el mismo bitness, administrador, y un proceso .NET vivo.";
            ResultBar.IsOpen = true;
            StatusText.Text = "Error";
        }
        finally
        {
            _busy = false;
            Busy.IsActive = false;
            InspectButton.IsEnabled = true;
        }
    }
}
