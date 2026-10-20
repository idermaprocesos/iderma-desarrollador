using Consola.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Consola;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { File.WriteAllText(CrashLog.Path, ""); } catch { }
        CrashLog.Write("Main");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            CrashLog.Write("ComWrappers listo");
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                CrashLog.Write("Creando App");
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            CrashLog.Alert("Consola no pudo iniciar", ex.ToString());
        }
    }
}
