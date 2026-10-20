using System.ServiceProcess;
using Consola.Models;

namespace Consola.Services;

internal sealed class WindowsServiceMonitor
{
    public static WindowsServiceMonitor Instance { get; } = new();

    public List<ServiceRow> Refresh()
    {
        var rows = new List<ServiceRow>();
        ServiceController[] services;
        try
        {
            services = ServiceController.GetServices();
        }
        catch (Exception ex)
        {
            return [new ServiceRow { Name = "error", DisplayName = ex.Message, Status = "Error" }];
        }

        foreach (var service in services)
        {
            try
            {
                var start = Safe(() => service.StartType.ToString(), "");
                var canStop = Safe(() => service.CanStop, false);
                rows.Add(new ServiceRow
                {
                    Name = service.ServiceName,
                    DisplayName = service.DisplayName,
                    Status = Translate(service.Status),
                    StartType = TranslateStart(start),
                    CanStop = canStop
                });
            }
            catch
            {
                rows.Add(new ServiceRow
                {
                    Name = service.ServiceName,
                    DisplayName = service.ServiceName,
                    Status = "Desconocido"
                });
            }
            finally
            {
                service.Dispose();
            }
        }

        EnrichWithWmi(rows);
        return rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string Control(string name, string action)
    {
        using var service = new ServiceController(name);
        try
        {
            switch (action)
            {
                case "start":
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    return "Servicio iniciado.";
                case "stop":
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    return "Servicio detenido.";
                case "restart":
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    return "Servicio reiniciado.";
                default:
                    return "Acción no reconocida.";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void EnrichWithWmi(List<ServiceRow> rows)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, ProcessId, StartName, PathName, Description FROM Win32_Service");
            var map = rows.ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (!map.TryGetValue(name, out var row)) continue;
                    int.TryParse(obj["ProcessId"]?.ToString(), out var pid);
                    map[name] = new ServiceRow
                    {
                        Name = row.Name,
                        DisplayName = row.DisplayName,
                        Status = row.Status,
                        StartType = row.StartType,
                        CanStop = row.CanStop,
                        Pid = pid,
                        Account = obj["StartName"]?.ToString() ?? "",
                        Path = obj["PathName"]?.ToString() ?? "",
                        Description = obj["Description"]?.ToString() ?? ""
                    };
                }
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (map.TryGetValue(rows[i].Name, out var enriched))
                    rows[i] = enriched;
            }
        }
        catch
        {
            // WMI no disponible
        }
    }

    private static string Translate(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => "En ejecución",
        ServiceControllerStatus.Stopped => "Detenido",
        ServiceControllerStatus.Paused => "Pausado",
        ServiceControllerStatus.StartPending => "Iniciando",
        ServiceControllerStatus.StopPending => "Deteniendo",
        ServiceControllerStatus.ContinuePending => "Reanudando",
        ServiceControllerStatus.PausePending => "Pausando",
        _ => status.ToString()
    };

    private static string TranslateStart(string start) => start switch
    {
        "Automatic" => "Automático",
        "Manual" => "Manual",
        "Disabled" => "Deshabilitado",
        "Boot" => "Arranque",
        "System" => "Sistema",
        _ => start
    };

    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}
