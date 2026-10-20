using Consola.Helpers;
using Consola.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Consola;

public partial class App : Application
{
    public static App Instance { get; private set; } = null!;
    public static DispatcherQueue Ui { get; private set; } = null!;
    public MainWindow? Window { get; private set; }

    public App()
    {
        Instance = this;
        CrashLog.Write("App ctor");
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            SessionState.Current.StatusDetail = e.Message;
            e.Handled = true;
        };
        CrashLog.Write("App InitializeComponent ok");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashLog.Write("OnLaunched");
        try
        {
            PrivilegeHelper.TryEnableDebugPrivilege();
            SessionState.Current.IsElevated = PrivilegeHelper.IsElevated;
            SessionState.Current.ElevationText = PrivilegeHelper.IsElevated ? "Administrador" : "Usuario estándar";

            CrashLog.Write("Creando MainWindow");
            Window = new MainWindow();
            Ui = Window.DispatcherQueue;
            CrashLog.Write("Activate");
            Window.Activate();
            Window.AppWindow?.Show();
            Window.BringToFront();
            MonitorEngine.Instance.Start(Ui);
            _ = Task.Run(() =>
            {
                try { FaultMonitorService.Instance.StartLive(); } catch { }
            });
            Window.Closed += (_, _) =>
            {
                MonitorEngine.Instance.Stop();
                EtwActivityService.Instance.Dispose();
                FaultMonitorService.Instance.Dispose();
                DebugOutputService.Instance.Dispose();
            };
            CrashLog.Write("OnLaunched ok");
        }
        catch (Exception ex)
        {
            CrashLog.Alert("Consola no pudo abrir la ventana", ex.ToString());
        }
    }

    public static async Task ShowErrorAsync(string title, string message)
    {
        if (Instance.Window?.Content is not FrameworkElement root) return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Cerrar",
            XamlRoot = root.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
