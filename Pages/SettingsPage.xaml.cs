using Consola.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Consola.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ElevationStatus.Text = PrivilegeHelper.IsElevated
            ? "Esta sesión ya se ejecuta como administrador. SeDebugPrivilege se intenta habilitar al arrancar."
            : "Sesión estándar: algunos procesos del sistema, ETW del kernel y volcados completos estarán bloqueados.";
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        PrivilegeHelper.RestartAsAdministrator();
        Application.Current.Exit();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (App.Instance.Window?.Content is not FrameworkElement root) return;
        root.RequestedTheme = ThemeBox.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
    }
}
