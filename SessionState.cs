using CommunityToolkit.Mvvm.ComponentModel;
using Consola.Helpers;
using Consola.Models;
using Consola.Services;

namespace Consola;

public sealed partial class SessionState : ObservableObject
{
    public static SessionState Current { get; } = new();

    [ObservableProperty] private ProcessRow? _selectedProcess;
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private bool _etwRunning;
    [ObservableProperty] private bool _etwFailed;
    [ObservableProperty] private string _status = "Iniciando…";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private int _liveEventCount;
    [ObservableProperty] private string _elevationText = "Usuario estándar";
    [ObservableProperty] private string _targetCaption = "Ningún programa en monitoreo";

    public SessionState()
    {
        IsElevated = PrivilegeHelper.IsElevated;
        ElevationText = IsElevated ? "Administrador" : "Usuario estándar";
    }

    partial void OnSelectedProcessChanged(ProcessRow? oldValue, ProcessRow? newValue)
    {
        EtwActivityService.Instance.FilterPid = newValue is { Pid: > 0 } ? newValue.Pid : null;
        if (oldValue?.Pid != newValue?.Pid)
        {
            TargetIoService.Instance.BeginCapture(newValue);
            if (newValue is { Pid: > 0 } selected)
            {
                _ = Task.Run(() =>
                {
                    try { ProcessCatalogService.Instance.EnrichDetails(selected); }
                    catch { /* detalles opcionales */ }
                    App.Ui?.TryEnqueue(RefreshTargetCaption);
                });
            }
        }
        RefreshTargetCaption();
    }

    public void RefreshTargetCaption()
    {
        var p = SelectedProcess;
        if (p == null)
        {
            TargetCaption = "Ningún programa en monitoreo";
            StatusDetail = "";
            return;
        }

        var io = TargetIoService.Instance.Current is { Pid: var pid } snap && pid == p.Pid
            ? $"  ·  {snap.InternetText}  ·  {snap.Dependencies.Count} deps"
            : "";
        TargetCaption = $"Monitoreando {p.Name} ({p.Pid})  ·  CPU {p.CpuText}  ·  {p.MemoryText}  ·  {p.ThreadCount} hilos  ·  {p.HandleCount} handles{io}";
        StatusDetail = $"{p.Name} ({p.Pid})";
    }
}
