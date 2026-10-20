using Consola.Helpers;
using Consola.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Consola;

public sealed partial class MainWindow : Window
{
    public SessionState Session => SessionState.Current;

    public MainWindow()
    {
        CrashLog.Write("MainWindow ctor");
        InitializeComponent();
        CrashLog.Write("MainWindow XAML ok");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Iderma.Shell.BotonLauncher.Colocar(AppTitleBar);
        try { AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; } catch { }
        try { AppWindow.SetIcon("Assets/logo.ico"); } catch { }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 920));
        Title = "Consola — Analizador de sistemas";
        NavFrame.Navigate(typeof(HomePage));
        CrashLog.Write("MainWindow listo");
    }

    public void BringToFront()
    {
        try
        {
            AppWindow.Show();
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void GoTo(string tag)
    {
        if (tag == "settings")
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>().Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack) NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item) return;
        var page = item.Tag switch
        {
            "home" => typeof(HomePage),
            "processes" => typeof(ProcessesPage),
            "inspect" => typeof(InspectPage),
            "deps" => typeof(DependenciesPage),
            "internals" => typeof(InternalsPage),
            "memory" => typeof(MemoryPage),
            "activity" => typeof(ActivityPage),
            "services" => typeof(ServicesPage),
            "faults" => typeof(FaultsPage),
            "debug" => typeof(DebugPage),
            "about" => typeof(AboutPage),
            _ => typeof(HomePage)
        };
        NavFrame.Navigate(page);
    }
}
